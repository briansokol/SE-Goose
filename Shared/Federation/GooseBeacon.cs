using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    /// <summary>
    /// Goose-to-Goose presence beacon. Broadcasts this instance's PB identity, construct signature, and
    /// construct grid set over IGC, and maintains a table of live peer Goose instances heard in return.
    /// Wraps an injectable <see cref="IBridgeTransport"/> so the protocol can be unit-tested without IGC.
    /// The beacon is driven every main tick (even while the instance is halted) so duplicate/standby states
    /// can auto-recover once peers leave.
    /// </summary>
    public class GooseBeacon
    {
        private readonly IBridgeTransport _transport;
        private readonly long _myPbId;
        private readonly Func<ulong> _getSignature;
        private readonly Func<IEnumerable<long>> _getConstructGrids;
        private readonly Action<string> _logWarning;

        private readonly StringList _inboxBuf = new StringList();
        private readonly Dictionary<long, PeerRecord> _peers = new Dictionary<long, PeerRecord>();
        private readonly List<long> _sweepBuf = new List<long>();
        private readonly PeerList _liveBuf = new PeerList();

        private bool _enabled;
        private bool _initialized;
        private long _currentTick;
        private long _nextHeartbeatTick;

        /// <summary>Announce cadence in main-loop ticks.</summary>
        public int HeartbeatTicks { get; set; }

        /// <summary>Number of consecutive missed announces before a peer is considered gone.</summary>
        public int PeerStaleMultiplier { get; set; }

        /// <summary>Master kill-switch. When <c>false</c>, all public methods are no-ops and the peer table stays empty.</summary>
        public bool Enabled
        {
            get { return _enabled; }
            set { _enabled = value; }
        }

        /// <summary>Constructs a beacon.</summary>
        /// <param name="transport">Transport implementation; real use injects an IGC transport, tests inject a fake.</param>
        /// <param name="myPbId">EntityId of the local programmable block. Identifies this instance and filters self-echo.</param>
        /// <param name="getSignature">Returns the local mechanical-only construct signature, sampled on each announce.</param>
        /// <param name="getConstructGrids">Enumerates the local construct's grid EntityIds, sampled on each announce.</param>
        /// <param name="logWarning">Invoked with a short description on any parse/transport error. The beacon never throws into the main loop.</param>
        public GooseBeacon(
            IBridgeTransport transport,
            long myPbId,
            Func<ulong> getSignature,
            Func<IEnumerable<long>> getConstructGrids,
            Action<string> logWarning)
        {
            _transport = transport;
            _myPbId = myPbId;
            _getSignature = getSignature;
            _getConstructGrids = getConstructGrids;
            _logWarning = logWarning;
            _enabled = true;
            HeartbeatTicks = 6;
            PeerStaleMultiplier = 3;
        }

        /// <summary>One-shot bootstrap. Idempotent. Sends an initial announce and seeds the heartbeat clock.</summary>
        public void Initialize()
        {
            if (!_enabled || _initialized || _transport == null)
            {
                return;
            }

            _initialized = true;
            _nextHeartbeatTick = _currentTick + HeartbeatTicks;
            EmitAnnounce();
        }

        /// <summary>Per-cycle driver. Drains the inbox, sweeps stale peers, and re-announces on cadence.</summary>
        /// <param name="currentTick">Monotonically increasing main-loop tick.</param>
        public void Tick(long currentTick)
        {
            if (!_enabled || !_initialized || _transport == null)
            {
                return;
            }

            _currentTick = currentTick;
            DrainInbox();
            SweepStalePeers();
            if (_currentTick >= _nextHeartbeatTick)
            {
                _nextHeartbeatTick = _currentTick + HeartbeatTicks;
                EmitAnnounce();
            }
        }

        /// <summary>Returns the live (non-stale) peer Goose instances as of <paramref name="currentTick"/>. The returned list is reused between calls; copy it if retained.</summary>
        public PeerList GetLivePeers(long currentTick)
        {
            _liveBuf.Clear();
            if (!_enabled)
            {
                return _liveBuf;
            }

            long staleAfterTicks = (long)HeartbeatTicks * PeerStaleMultiplier;
            foreach (KeyValuePair<long, PeerRecord> kv in _peers)
            {
                if (currentTick - kv.Value.LastSeenTick <= staleAfterTicks)
                {
                    _liveBuf.Add(kv.Value.Peer);
                }
            }
            return _liveBuf;
        }

        /// <summary>Builds an announce envelope. Shared by production sends and tests.</summary>
        /// <param name="pbId">Sender PB EntityId.</param>
        /// <param name="signature">Sender construct signature.</param>
        /// <param name="grids">Sender construct grid EntityIds.</param>
        public static BridgeMessage Announce(long pbId, ulong signature, IEnumerable<long> grids)
        {
            var sb = new StringBuilder();
            if (grids != null)
            {
                bool first = true;
                foreach (long g in grids)
                {
                    if (!first)
                    {
                        sb.Append(FederationProtocol.GridsDelimiter);
                    }
                    sb.Append(g.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    first = false;
                }
            }

            return new BridgeMessage(FederationProtocol.KindAnnounce)
                .Set(BridgeProtocol.KeyVersion, FederationProtocol.ProtocolVersion)
                .Set(FederationProtocol.KeyPbId, pbId)
                .Set(FederationProtocol.KeySignature, signature.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Set(FederationProtocol.KeyGrids, sb.ToString());
        }

        private void EmitAnnounce()
        {
            ulong sig = SafeCall.Run<ulong>(_getSignature, 0, "getSignature", Warn);
            IEnumerable<long> grids = SafeCall.Run<IEnumerable<long>>(_getConstructGrids, null, "getConstructGrids", Warn);
            TrySend(Announce(_myPbId, sig, grids));
        }

        private void DrainInbox()
        {
            _inboxBuf.Clear();
            try
            {
                _transport.DrainInbox(_inboxBuf);
            }
            catch (Exception ex)
            {
                Warn("DrainInbox: " + ex.Message);
                return;
            }

            for (int i = 0; i < _inboxBuf.Count; i++)
            {
                HandleIncoming(_inboxBuf[i]);
            }
        }

        private void HandleIncoming(string payload)
        {
            BridgeMessage msg;
            try
            {
                msg = BridgeMessage.Parse(payload);
            }
            catch (Exception ex)
            {
                Warn("parse: " + ex.Message);
                return;
            }
            if (msg == null || msg.Kind != FederationProtocol.KindAnnounce)
            {
                return;
            }

            if (msg.GetInt(BridgeProtocol.KeyVersion, -1) != FederationProtocol.ProtocolVersion)
            {
                return;
            }

            long pbId = msg.GetLong(FederationProtocol.KeyPbId, 0);
            if (pbId == 0 || pbId == _myPbId)
            {
                return;
            }

            ulong signature;
            if (!ulong.TryParse(msg.GetString(FederationProtocol.KeySignature, string.Empty),
                System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out signature))
            {
                return;
            }

            PeerRecord rec;
            if (!_peers.TryGetValue(pbId, out rec))
            {
                rec = new PeerRecord { Peer = new FederationPeer { ConstructGrids = new LongSet() } };
                _peers[pbId] = rec;
            }

            rec.Peer.PbId = pbId;
            rec.Peer.Signature = signature;
            rec.Peer.ConstructGrids.Clear();
            ParseGrids(msg.GetString(FederationProtocol.KeyGrids, string.Empty), rec.Peer.ConstructGrids);
            rec.LastSeenTick = _currentTick;
        }

        private static void ParseGrids(string raw, LongSet output)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return;
            }

            string[] parts = raw.Split(FederationProtocol.GridsDelimiter);
            for (int i = 0; i < parts.Length; i++)
            {
                long id;
                if (long.TryParse(parts[i], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out id) && id != 0)
                {
                    output.Add(id);
                }
            }
        }

        private void SweepStalePeers()
        {
            if (_peers.Count == 0)
            {
                return;
            }

            long staleAfterTicks = (long)HeartbeatTicks * PeerStaleMultiplier;
            _sweepBuf.Clear();
            foreach (KeyValuePair<long, PeerRecord> kv in _peers)
            {
                if (_currentTick - kv.Value.LastSeenTick > staleAfterTicks)
                {
                    _sweepBuf.Add(kv.Key);
                }
            }
            for (int i = 0; i < _sweepBuf.Count; i++)
            {
                _peers.Remove(_sweepBuf[i]);
            }
        }





        private void TrySend(BridgeMessage msg)
        {
            if (_transport == null)
            {
                return;
            }
            try
            {
                _transport.Send(msg.Serialize());
            }
            catch (Exception ex)
            {
                Warn("send: " + ex.Message);
            }
        }

        private void Warn(string text)
        {
            if (_logWarning == null)
            {
                return;
            }
            try
            {
                _logWarning(text);
            }
            catch (Exception)
            {
                // Logging itself failed; nothing more we can do.
            }
        }

        /// <summary>Internal peer bookkeeping: the projected peer plus the tick it was last heard.</summary>
        private class PeerRecord
        {
            public FederationPeer Peer;
            public long LastSeenTick;
        }
    }

    /// <summary>Default <see cref="IBridgeTransport"/> for the federation beacon, broadcasting at <see cref="TransmissionDistance.ConnectedConstructs"/> so docked grids are reachable. Registers a broadcast listener at construction.</summary>
    public class IgcFederationTransport : IBridgeTransport
    {
        private readonly IMyIntergridCommunicationSystem _igc;
        private readonly string _tag;
        private readonly IMyBroadcastListener _listener;

        /// <summary>Registers the broadcast listener for <paramref name="tag"/> on <paramref name="igc"/>.</summary>
        public IgcFederationTransport(IMyIntergridCommunicationSystem igc, string tag)
        {
            _igc = igc;
            _tag = tag;
            _listener = igc.RegisterBroadcastListener(tag);
        }

        /// <summary>Sends <paramref name="payload"/> to every connected construct (reaches docked grids).</summary>
        public void Send(string payload)
        {
            _igc.SendBroadcastMessage(_tag, payload, TransmissionDistance.ConnectedConstructs);
        }

        /// <summary>Pulls every pending broadcast into <paramref name="outBuf"/>. Non-string payloads are dropped.</summary>
        public void DrainInbox(StringList outBuf)
        {
            while (_listener.HasPendingMessage)
            {
                MyIGCMessage msg = _listener.AcceptMessage();
                string text = msg.Data as string;
                if (!string.IsNullOrEmpty(text))
                {
                    outBuf.Add(text);
                }
            }
        }
    }
}
