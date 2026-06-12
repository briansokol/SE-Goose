using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        /// <summary>Why the script is currently standing down, if at all.</summary>
        private enum HaltReason
        {
            /// <summary>Running normally.</summary>
            None,

            /// <summary>Another Goose reports the identical construct: an accidental same-grid duplicate. Both halt.</summary>
            DuplicateScope,

            /// <summary>A higher-priority docked Goose owns the shared scope; this instance defers in full standby.</summary>
            DeferredFederation
        }

        /// <summary>Current halt state. Set each tick by <see cref="FederationTick"/>; gates inventory work and the bridge.</summary>
        private HaltReason _haltReason = HaltReason.None;

        /// <summary>User-facing banner describing the active halt, shown on Echo and <c>[GError]</c> surfaces.</summary>
        private string _haltMessage = "";

        /// <summary>Goose-to-Goose presence beacon. <c>null</c> until lazily initialized, or when construction failed.</summary>
        private GooseBeacon _beacon;

        /// <summary>Backing IGC transport for <see cref="_beacon"/> (connected-construct distance, reaches docked grids).</summary>
        private IBridgeTransport _beaconTransport;

        /// <summary>True when the beacon is live and arbitration drives scope; selects the arbitration-aware <see cref="ScopeBuilder.BuildScope"/> overload in <see cref="RebuildScope"/>.</summary>
        private bool _federationArbitrationActive;

        /// <summary>Remote grids cleared to federate this tick, produced by <see cref="ScopeArbitration"/> and consumed by <see cref="RebuildScope"/>.</summary>
        private readonly LongSet _approvedFederateGrids = new LongSet();

        /// <summary>Raw mechanical-block buffer for the federation enumeration (kept separate from the pipeline's buffers to avoid clobbering mid-iteration).</summary>
        private readonly MechBlockList _fedMechRaw = new MechBlockList();

        /// <summary>Raw connector buffer for the federation enumeration.</summary>
        private readonly ConnectorList _fedConnRaw = new ConnectorList();

        /// <summary>Projected mechanical edges for the federation enumeration.</summary>
        private readonly MechanicalEdgeList _fedMechBuf = new MechanicalEdgeList();

        /// <summary>Projected connector edges for the federation enumeration; also the connector input to arbitration.</summary>
        private readonly ConnectorEdgeList _fedConnBuf = new ConnectorEdgeList();

        /// <summary>This instance's mechanical-only construct grid set, refreshed each tick and announced over the beacon.</summary>
        private readonly LongSet _constructGrids = new LongSet();

        /// <summary>This instance's mechanical-only construct signature, announced over the beacon.</summary>
        private ulong _constructSignature;

        /// <summary>Hash of the last federation decision (halt reason + approved grids); a change triggers a scope rescan even without physical scope drift.</summary>
        private ulong _federationDecisionHash;

        /// <summary>True while the script is standing down (duplicate or deferral); inventory steps and the bridge are suspended.</summary>
        private bool ManagementSuspended
        {
            get { return _haltReason != HaltReason.None; }
        }

        /// <summary>Drives the presence beacon and multi-Goose arbitration. Runs every tick (even while halted) so duplicate/standby states recover once peers leave. Sets <see cref="_haltReason"/> and <see cref="_approvedFederateGrids"/>.</summary>
        /// <param name="tick">Monotonic main-loop tick.</param>
        private void FederationTick(long tick)
        {
            if (!_config.EnableMultiGooseArbitration)
            {
                if (_federationArbitrationActive || _haltReason != HaltReason.None)
                {
                    _federationArbitrationActive = false;
                    _haltReason = HaltReason.None;
                    _approvedFederateGrids.Clear();
                    _rescanRequested = true;
                }
                if (_beacon != null)
                {
                    _beacon.Enabled = false;
                }
                return;
            }

            ScopeEdgeEnumerator.EnumerateLiveEdges(GridTerminalSystem, _fedMechRaw, _fedConnRaw, _fedMechBuf, _fedConnBuf);
            ScopeBuilder.BuildScope(Me.CubeGrid.EntityId, _fedMechBuf, (LongSet)null, _constructGrids);
            _constructSignature = ScopeBuilder.ComputeConstructSignature(_constructGrids);

            if (_beacon == null)
            {
                InitFederation();
            }
            if (_beacon == null)
            {
                _federationArbitrationActive = false;
                return;
            }

            _federationArbitrationActive = true;
            _beacon.Enabled = true;
            _beacon.HeartbeatTicks = _config.FederationHeartbeatTicks;
            _beacon.Tick(tick);

            PeerList peers = _beacon.GetLivePeers(tick);
            IList<ConnectorEdge> connForArbitration = _config.EnableConnectorFederation ? (IList<ConnectorEdge>)_fedConnBuf : null;
            ArbitrationResult result = ScopeArbitration.Decide(
                Me.CubeGrid.EntityId, Me.EntityId, _constructSignature, connForArbitration, peers);

            _approvedFederateGrids.Clear();
            foreach (long g in result.ApprovedFederateGrids)
            {
                _approvedFederateGrids.Add(g);
            }

            HaltReason newReason = result.DuplicateScope
                ? HaltReason.DuplicateScope
                : (result.StandDown ? HaltReason.DeferredFederation : HaltReason.None);

            if (newReason != _haltReason)
            {
                _haltReason = newReason;
                _haltMessage = HaltMessageFor(newReason);
                LogAction(newReason == HaltReason.None ? "Federation: resumed" : "Federation: " + _haltMessage);
            }

            ulong decisionHash = ComputeFederationDecisionHash(newReason, _approvedFederateGrids);
            if (decisionHash != _federationDecisionHash)
            {
                _federationDecisionHash = decisionHash;
                _rescanRequested = true;
            }
        }

        /// <summary>Lazily constructs the presence beacon once the construct signature is available. On failure, leaves <see cref="_beacon"/> null so arbitration cleanly falls back to legacy federation.</summary>
        private void InitFederation()
        {
            string tag = _config != null && !string.IsNullOrEmpty(_config.FederationChannelTag)
                ? _config.FederationChannelTag
                : FederationProtocol.DefaultChannelTag;

            try
            {
                _beaconTransport = new IgcFederationTransport(IGC, tag);
                _beacon = new GooseBeacon(
                    _beaconTransport,
                    Me.EntityId,
                    () => _constructSignature,
                    () => _constructGrids,
                    FederationLogWarning);
                _beacon.HeartbeatTicks = _config.FederationHeartbeatTicks;
                _beacon.Initialize();
            }
            catch (System.Exception ex)
            {
                LogWarning("Federation init failed: " + ex.Message);
                _beacon = null;
                _beaconTransport = null;
            }
        }

        /// <summary>Surfaces beacon-internal warnings through the one-shot warning channel.</summary>
        private void FederationLogWarning(string message)
        {
            LogWarningOnce("federation", "[Goose] federation: " + message);
        }

        /// <summary>Returns the user-facing banner for a halt reason.</summary>
        private static string HaltMessageFor(HaltReason reason)
        {
            switch (reason)
            {
                case HaltReason.DuplicateScope:
                    return "HALT: another Goose runs on this exact grid. Disable one of them.";
                case HaltReason.DeferredFederation:
                    return "STANDBY: deferring to a higher-priority docked Goose.";
                default:
                    return "";
            }
        }

        /// <summary>Order-independent hash of the federation decision (reason plus approved grids), used to detect decision changes that warrant a scope rescan.</summary>
        private static ulong ComputeFederationDecisionHash(HaltReason reason, LongSet approved)
        {
            ulong h = ScopeBuilder.ComputeConstructSignature(approved);
            h ^= (ulong)((int)reason + 1);
            h *= 1099511628211UL;
            return h;
        }
    }
}
