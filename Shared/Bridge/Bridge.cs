using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    /// <summary>
    /// Same-grid script-to-script bridge between Goose (inventory) and Crane (autocrafting).
    /// Owns peer-status tracking, catalog convergence, and assembler-hold bookkeeping.
    /// Wraps an injectable <see cref="IBridgeTransport"/> so the protocol can be unit-tested without IGC.
    /// All public methods short-circuit cleanly when <see cref="Enabled"/> is <c>false</c>.
    /// </summary>
    public class Bridge
    {
        private readonly IBridgeTransport _transport;
        private readonly BridgeRole _role;
        private readonly Action<string> _onPeerCatalogKey;
        private readonly Func<int> _getLocalCatalogCount;
        private readonly Func<IEnumerable<string>> _getLocalCatalogKeys;
        private readonly Action<string> _logWarning;

        private Func<string, long> _itemCountResolver;
        private Action<string, long> _onPeerItemCount;

        private readonly List<string> _inboxBuf = new List<string>();
        private readonly Dictionary<long, BridgeHoldHint> _holds = new Dictionary<long, BridgeHoldHint>();
        private readonly Queue<long> _holdInsertOrder = new Queue<long>();
        private readonly List<long> _holdSweepBuf = new List<long>();

        private bool _enabled;
        private bool _initialized;

        private long _currentTick;
        private long _nextHeartbeatTick;
        private long _peerLastSeenTick = -1;
        private long _nextSnapshotEligibleTick;
        private int _peerCatalogCount;
        private bool _peerLinked;

        /// <summary>Heartbeat cadence in main-loop ticks. Also drives <c>hello</c> resend while no peer is linked.</summary>
        public int HeartbeatTicks { get; set; }

        /// <summary>Maximum number of concurrent assembler holds tracked on the receiver side; older holds are evicted FIFO when exceeded.</summary>
        public int MaxHolds { get; set; }

        /// <summary>Per-peer debounce window (in main-loop ticks) between successive <c>catalogSnapshot</c> sends.</summary>
        public int SnapshotDebounceTicks { get; set; }

        /// <summary>Number of consecutive missed heartbeats before the peer is reported stale.</summary>
        public int PeerStaleMultiplier { get; set; }

        /// <summary>Minimum gap between local and peer catalog counts that triggers a snapshot resend.</summary>
        public int HoldDriftThreshold { get; set; }

        /// <summary>Master kill-switch. Defaults to <c>true</c>. When <c>false</c>, all public methods are no-ops.</summary>
        public bool Enabled
        {
            get { return _enabled; }
            set { _enabled = value; }
        }

        /// <summary>
        /// Constructs a bridge instance.
        /// </summary>
        /// <param name="transport">Transport implementation. Real PB use injects an <see cref="IgcBridgeTransport"/>; tests inject a fake.</param>
        /// <param name="role">Local script role.</param>
        /// <param name="onPeerCatalogKey">Invoked exactly once per peer-announced catalog key (deduplication is the caller's responsibility). Wires into <c>RecordItem</c> on each script.</param>
        /// <param name="getLocalCatalogCount">Returns the local catalog's current key count, sampled on each <see cref="Tick"/>.</param>
        /// <param name="getLocalCatalogKeys">Enumerates the local catalog's keys for <c>catalogSnapshot</c> emission.</param>
        /// <param name="logWarning">Invoked with a short description on any parse/transport error. Bridge code never throws into the main loop.</param>
        public Bridge(IBridgeTransport transport, BridgeRole role,
            Action<string> onPeerCatalogKey,
            Func<int> getLocalCatalogCount,
            Func<IEnumerable<string>> getLocalCatalogKeys,
            Action<string> logWarning)
        {
            _transport = transport;
            _role = role;
            _onPeerCatalogKey = onPeerCatalogKey;
            _getLocalCatalogCount = getLocalCatalogCount;
            _getLocalCatalogKeys = getLocalCatalogKeys;
            _logWarning = logWarning;
            _enabled = true;
            HeartbeatTicks = 60;
            MaxHolds = 64;
            SnapshotDebounceTicks = 360;
            PeerStaleMultiplier = 3;
            HoldDriftThreshold = 5;
        }

        /// <summary>One-shot bootstrap. Idempotent; the second call is ignored. Sends an initial <c>hello</c> and seeds the heartbeat clock.</summary>
        public void Initialize()
        {
            if (!_enabled || _initialized || _transport == null)
            {
                return;
            }

            _initialized = true;
            _nextHeartbeatTick = _currentTick + HeartbeatTicks;
            TrySend(BridgeMessage.Hello(_role));
        }

        /// <summary>Per-cycle driver. Drains the inbox, sweeps expired holds, and emits <c>hello</c> or <c>heartbeat</c> on cadence.</summary>
        /// <param name="currentTick">Monotonically increasing main-loop tick. Used both for staleness checks and hold expiry.</param>
        public void Tick(long currentTick)
        {
            if (!_enabled || !_initialized || _transport == null)
            {
                return;
            }

            _currentTick = currentTick;
            DrainInbox();
            SweepExpiredHolds();
            UpdatePeerLinked();
            EmitHeartbeatIfDue();
        }

        /// <summary>Announces a freshly added local catalog key to the peer. Caller invokes this only when a local <c>RecordItem</c> returned "newly added"; no idempotency guards live here.</summary>
        public void AnnounceCatalogAdd(string key)
        {
            if (!_enabled || !_initialized || _transport == null || string.IsNullOrEmpty(key))
            {
                return;
            }

            TrySend(BridgeMessage.CatalogAdd(key));
        }

        /// <summary>Announces that Crane is actively crafting on the given assembler. Goose stores the hold and uses it to gate its balancer.</summary>
        /// <param name="entityId">Assembler EntityId resolved via <c>GridTerminalSystem</c> on the receiver side.</param>
        /// <param name="ttlTicks">Lifetime in main-loop ticks; expired holds are swept on each <see cref="Tick"/>.</param>
        /// <param name="needCsv">Optional hint list (<c>Ingot/Iron:200,Ingot/Cobalt:50</c>) describing the inputs Crane just queued; the receiver may use this to bias feeding priority but never as a commitment.</param>
        public void AnnounceAssemblerHold(long entityId, int ttlTicks, string needCsv)
        {
            if (!_enabled || !_initialized || _transport == null)
            {
                return;
            }

            TrySend(BridgeMessage.AssemblerHold(entityId, ttlTicks, needCsv));
        }

        /// <summary>Registers the Goose-side resolver that returns the grid-wide count for a given item key (0 when the item is absent). Drives replies to <c>itemCountReq</c>.</summary>
        public void SetItemCountResponder(Func<string, long> resolve)
        {
            _itemCountResolver = resolve;
        }

        /// <summary>Registers the Crane-side handler invoked once per <c>key</c>/<c>amount</c> pair carried by an inbound <c>itemCountRes</c>.</summary>
        public void SetOnPeerItemCount(Action<string, long> handler)
        {
            _onPeerItemCount = handler;
        }

        /// <summary>Requests grid-wide counts for <paramref name="keys"/> from the peer. The reply arrives asynchronously and is delivered to the handler registered via <see cref="SetOnPeerItemCount"/>.</summary>
        public void RequestItemCounts(IEnumerable<string> keys)
        {
            if (!_enabled || !_initialized || _transport == null)
            {
                return;
            }

            TrySend(BridgeMessage.ItemCountRequest(keys));
        }

        /// <summary>Returns <c>true</c> when an unexpired hold has been received for <paramref name="entityId"/>.</summary>
        public bool IsAssemblerHeld(long entityId)
        {
            if (!_enabled)
            {
                return false;
            }

            BridgeHoldHint hint;
            return _holds.TryGetValue(entityId, out hint) && hint.ExpiryTick >= _currentTick;
        }

        /// <summary>Returns the active hold for <paramref name="entityId"/>, or <c>null</c> when no live hold exists.</summary>
        public BridgeHoldHint GetHoldHint(long entityId)
        {
            if (!_enabled)
            {
                return null;
            }

            BridgeHoldHint hint;
            if (!_holds.TryGetValue(entityId, out hint))
            {
                return null;
            }
            return hint.ExpiryTick >= _currentTick ? hint : null;
        }

        /// <summary>Lightweight peer-status snapshot for status displays.</summary>
        public BridgePeerStatus PeerStatus
        {
            get
            {
                var s = new BridgePeerStatus();
                s.Linked = _peerLinked;
                s.LastSeenTick = _peerLastSeenTick;
                s.PeerCatalogCount = _peerCatalogCount;
                return s;
            }
        }

        /// <summary>Number of currently tracked assembler holds. Exposed for diagnostics and tests.</summary>
        public int ActiveHoldCount
        {
            get { return _holds.Count; }
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
            if (msg == null)
            {
                return;
            }

            int peerVersion = msg.GetInt(BridgeProtocol.KeyVersion, -1);
            string kind = msg.Kind;
            switch (kind)
            {
                case BridgeProtocol.KindHello:
                    if (peerVersion != BridgeProtocol.ProtocolVersion)
                    {
                        Warn("hello: version mismatch " + peerVersion);
                        return;
                    }
                    OnPeerSeen(msg.GetInt(BridgeProtocol.KeyCatalogCount, 0));
                    MaybeSendSnapshot();
                    break;

                case BridgeProtocol.KindHeartbeat:
                    if (peerVersion != BridgeProtocol.ProtocolVersion)
                    {
                        Warn("heartbeat: version mismatch " + peerVersion);
                        return;
                    }
                    int peerCount = msg.GetInt(BridgeProtocol.KeyCatalogCount, 0);
                    OnPeerSeen(peerCount);
                    int localCount = SafeLocalCatalogCount();
                    if (localCount - peerCount > HoldDriftThreshold)
                    {
                        MaybeSendSnapshot();
                    }
                    break;

                case BridgeProtocol.KindCatalogAdd:
                    OnPeerSeen(_peerCatalogCount);
                    InvokeOnKey(msg.GetString(BridgeProtocol.KeyKey, null));
                    break;

                case BridgeProtocol.KindCatalogSnapshot:
                    OnPeerSeen(_peerCatalogCount);
                    string keys = msg.GetString(BridgeProtocol.KeyKeys, string.Empty);
                    if (!string.IsNullOrEmpty(keys))
                    {
                        string[] parts = keys.Split(BridgeProtocol.KeysDelimiter);
                        for (int i = 0; i < parts.Length; i++)
                        {
                            InvokeOnKey(parts[i]);
                        }
                    }
                    break;

                case BridgeProtocol.KindAssemblerHold:
                    if (_role != BridgeRole.Goose)
                    {
                        return;
                    }
                    OnPeerSeen(_peerCatalogCount);
                    long id = msg.GetLong(BridgeProtocol.KeyEntityId, 0);
                    int ttl = msg.GetInt(BridgeProtocol.KeyTtl, 0);
                    if (id == 0 || ttl <= 0)
                    {
                        return;
                    }
                    RecordHold(id, ttl, msg.GetString(BridgeProtocol.KeyNeed, string.Empty));
                    break;

                case BridgeProtocol.KindItemCountRequest:
                    if (_role != BridgeRole.Goose)
                    {
                        return;
                    }
                    OnPeerSeen(_peerCatalogCount);
                    RespondToItemCountRequest(msg.GetString(BridgeProtocol.KeyKeys, string.Empty));
                    break;

                case BridgeProtocol.KindItemCountResponse:
                    if (_role != BridgeRole.Crane)
                    {
                        return;
                    }
                    OnPeerSeen(_peerCatalogCount);
                    DispatchItemCounts(msg.GetString(BridgeProtocol.KeyCounts, string.Empty));
                    break;
            }
        }

        /// <summary>Resolves a count for each requested key via the registered responder and replies with a single <c>itemCountRes</c>. No-op when no responder is registered.</summary>
        private void RespondToItemCountRequest(string keysCsv)
        {
            if (_itemCountResolver == null || string.IsNullOrEmpty(keysCsv))
            {
                return;
            }

            var counts = new List<KeyValuePair<string, long>>();
            string[] keys = keysCsv.Split(BridgeProtocol.KeysDelimiter);
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }
                counts.Add(new KeyValuePair<string, long>(key, _itemCountResolver(key)));
            }

            TrySend(BridgeMessage.ItemCountResponse(counts, BridgeProtocol.SnapshotMaxPayloadChars,
                () => Warn("itemCountRes: payload capped")));
        }

        /// <summary>Parses an inbound <c>counts=</c> payload and invokes the registered handler once per valid <c>key:amount</c> pair. Malformed entries are skipped.</summary>
        private void DispatchItemCounts(string countsCsv)
        {
            if (_onPeerItemCount == null || string.IsNullOrEmpty(countsCsv))
            {
                return;
            }

            string[] entries = countsCsv.Split(BridgeProtocol.KeysDelimiter);
            for (int i = 0; i < entries.Length; i++)
            {
                string entry = entries[i];
                if (string.IsNullOrEmpty(entry))
                {
                    continue;
                }
                int sep = entry.IndexOf(BridgeProtocol.CountPairSeparator);
                if (sep <= 0)
                {
                    continue;
                }
                string key = entry.Substring(0, sep);
                long amount;
                if (!long.TryParse(entry.Substring(sep + 1), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out amount))
                {
                    continue;
                }
                _onPeerItemCount(key, amount);
            }
        }

        private void OnPeerSeen(int peerCatalogCount)
        {
            _peerLastSeenTick = _currentTick;
            _peerCatalogCount = peerCatalogCount;
            _peerLinked = true;
        }

        private void UpdatePeerLinked()
        {
            if (_peerLastSeenTick < 0)
            {
                _peerLinked = false;
                return;
            }
            long staleAfter = _peerLastSeenTick + (long)HeartbeatTicks * PeerStaleMultiplier;
            _peerLinked = _currentTick <= staleAfter;
        }

        private void EmitHeartbeatIfDue()
        {
            if (_currentTick < _nextHeartbeatTick)
            {
                return;
            }
            _nextHeartbeatTick = _currentTick + HeartbeatTicks;
            int count = SafeLocalCatalogCount();
            BridgeMessage msg = _peerLinked
                ? BridgeMessage.Heartbeat(_role, count)
                : BridgeMessage.Hello(_role);
            TrySend(msg);
        }

        private void MaybeSendSnapshot()
        {
            if (_currentTick < _nextSnapshotEligibleTick)
            {
                return;
            }

            IEnumerable<string> keys = SafeLocalCatalogKeys();
            if (keys == null)
            {
                return;
            }

            IEnumerable<BridgeMessage> chunks = BridgeMessage.CatalogSnapshotChunks(
                keys, BridgeProtocol.SnapshotMaxPayloadChars);
            int sent = 0;
            foreach (BridgeMessage chunk in chunks)
            {
                TrySend(chunk);
                sent++;
            }
            if (sent > 0)
            {
                _nextSnapshotEligibleTick = _currentTick + SnapshotDebounceTicks;
            }
        }

        private void RecordHold(long entityId, int ttlTicks, string needRaw)
        {
            BridgeHoldHint hint;
            if (_holds.TryGetValue(entityId, out hint))
            {
                hint.ExpiryTick = _currentTick + ttlTicks;
                hint.NeedRaw = needRaw;
                return;
            }

            if (_holds.Count >= MaxHolds && _holdInsertOrder.Count > 0)
            {
                long evictId = _holdInsertOrder.Dequeue();
                _holds.Remove(evictId);
            }

            hint = new BridgeHoldHint();
            hint.EntityId = entityId;
            hint.ExpiryTick = _currentTick + ttlTicks;
            hint.NeedRaw = needRaw;
            _holds[entityId] = hint;
            _holdInsertOrder.Enqueue(entityId);
        }

        private void SweepExpiredHolds()
        {
            if (_holds.Count == 0)
            {
                return;
            }
            _holdSweepBuf.Clear();
            foreach (KeyValuePair<long, BridgeHoldHint> kv in _holds)
            {
                if (kv.Value.ExpiryTick < _currentTick)
                {
                    _holdSweepBuf.Add(kv.Key);
                }
            }
            for (int i = 0; i < _holdSweepBuf.Count; i++)
            {
                _holds.Remove(_holdSweepBuf[i]);
            }
        }

        private void InvokeOnKey(string key)
        {
            if (string.IsNullOrEmpty(key) || _onPeerCatalogKey == null)
            {
                return;
            }
            try
            {
                _onPeerCatalogKey(key);
            }
            catch (Exception ex)
            {
                Warn("onPeerCatalogKey(" + key + "): " + ex.Message);
            }
        }

        private int SafeLocalCatalogCount()
        {
            if (_getLocalCatalogCount == null)
            {
                return 0;
            }
            try
            {
                return _getLocalCatalogCount();
            }
            catch (Exception ex)
            {
                Warn("getLocalCatalogCount: " + ex.Message);
                return 0;
            }
        }

        private IEnumerable<string> SafeLocalCatalogKeys()
        {
            if (_getLocalCatalogKeys == null)
            {
                return null;
            }
            try
            {
                return _getLocalCatalogKeys();
            }
            catch (Exception ex)
            {
                Warn("getLocalCatalogKeys: " + ex.Message);
                return null;
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
                Warn("send(" + msg.Kind + "): " + ex.Message);
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
    }

    /// <summary>Default <see cref="IBridgeTransport"/> backed by the PB's <see cref="IMyIntergridCommunicationSystem"/>. Registers a same-construct broadcast listener at construction time.</summary>
    public class IgcBridgeTransport : IBridgeTransport
    {
        private readonly IMyIntergridCommunicationSystem _igc;
        private readonly string _tag;
        private readonly IMyBroadcastListener _listener;

        /// <summary>Registers the broadcast listener for <paramref name="tag"/> on <paramref name="igc"/>.</summary>
        public IgcBridgeTransport(IMyIntergridCommunicationSystem igc, string tag)
        {
            _igc = igc;
            _tag = tag;
            _listener = igc.RegisterBroadcastListener(tag);
        }

        /// <summary>Sends <paramref name="payload"/> as a <see cref="TransmissionDistance.CurrentConstruct"/> broadcast.</summary>
        public void Send(string payload)
        {
            _igc.SendBroadcastMessage(_tag, payload, TransmissionDistance.CurrentConstruct);
        }

        /// <summary>Pulls every pending broadcast into <paramref name="outBuf"/>. Non-string payloads are silently dropped.</summary>
        public void DrainInbox(List<string> outBuf)
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
