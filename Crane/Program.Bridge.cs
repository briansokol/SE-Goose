using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program
    {
        /// <summary>Same-grid bridge to Goose. <c>null</c> when bridge construction failed.</summary>
        private Bridge _bridge;

        /// <summary>Backing IGC transport for <see cref="_bridge"/>.</summary>
        private IgcBridgeTransport _bridgeTransport;

        /// <summary>Monotonic main-loop tick count, advanced on every <see cref="Main"/> invocation. Used as the bridge's clock.</summary>
        private long _mainTickCount;

        /// <summary>Reusable hold-announcement buffer to avoid per-call allocations.</summary>
        private readonly LongSet _bridgeAnnouncedAsmThisCycle = new LongSet();

        /// <summary>Constructs the bridge, registers its broadcast listener, and sends an initial <c>hello</c>. Tag and tunables come from the parsed config when available; defaults are applied otherwise.</summary>
        private void InitBridge()
        {
            string tag = _config != null && !string.IsNullOrEmpty(_config.BridgeChannelTag)
                ? _config.BridgeChannelTag
                : BridgeProtocol.DefaultChannelTag;

            try
            {
                _bridgeTransport = new IgcBridgeTransport(IGC, tag);
                _bridge = new Bridge(
                    _bridgeTransport,
                    BridgeRole.Crane,
                    BridgeHandlePeerCatalogKey,
                    BridgeLocalCatalogCount,
                    BridgeLocalCatalogKeys,
                    BridgeLogWarning);
                ApplyBridgeConfig();
                _bridge.Initialize();
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning("Bridge init failed: " + ex.Message);
                _bridge = null;
                _bridgeTransport = null;
            }
        }

        /// <summary>Pushes the latest <see cref="CraneConfig"/> bridge fields into the live bridge. Safe to call repeatedly; channel-tag changes require a recompile.</summary>
        private void ApplyBridgeConfig()
        {
            if (_bridge == null)
            {
                return;
            }
            _bridge.Enabled = _config.EnableBridge;
            _bridge.HeartbeatTicks = _config.BridgeHeartbeatTicks;
        }

        /// <summary>Resolves a peer-supplied catalog key (<c>Type/Subtype</c>) and merges it into the local catalog. Silently no-ops on malformed input.</summary>
        private void BridgeHandlePeerCatalogKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }
            int slash = key.IndexOf('/');
            if (slash <= 0 || slash >= key.Length - 1)
            {
                return;
            }

            MyItemType type;
            try
            {
                type = MyItemType.Parse("MyObjectBuilder_" + key);
            }
            catch (System.Exception)
            {
                return;
            }
            _catalog.RecordItem(type);
        }

        /// <summary>Forwards a locally added catalog key out to the peer. Wired into <see cref="ItemTotalsBuilder.BuildItemTotals"/> via the optional callback parameter.</summary>
        private void BridgeOnLocalCatalogKey(MyItemType type)
        {
            if (_bridge == null)
            {
                return;
            }
            string key = CatalogKeyHelpers.BuildKey(type);
            _bridge.AnnounceCatalogAdd(key);
        }

        /// <summary>Returns the local catalog's key count, sampled on each bridge heartbeat.</summary>
        private int BridgeLocalCatalogCount()
        {
            return _catalog.KnownItems.Count;
        }

        /// <summary>Enumerates the local catalog's keys for snapshot emission.</summary>
        private IEnumerable<string> BridgeLocalCatalogKeys()
        {
            return _catalog.KnownItems.Keys;
        }

        /// <summary>Surfaces bridge-internal warnings through the standard one-shot warning channel.</summary>
        private void BridgeLogWarning(string message)
        {
            _logger.LogWarningOnce("bridge", "[Crane] bridge: " + message);
        }

        /// <summary>Announces an active assembler hold to Goose. Idempotent within a cycle: only the first call per assembler this tick reaches the wire. Need details are not yet supplied (the PB API exposes no per-blueprint ingot requirement); the hold itself is enough to suppress Goose's balancer drain.</summary>
        private void BridgeAnnounceHold(IMyAssembler asm)
        {
            if (_bridge == null || asm == null)
            {
                return;
            }
            if (!_bridgeAnnouncedAsmThisCycle.Add(asm.EntityId))
            {
                return;
            }
            _bridge.AnnounceAssemblerHold(asm.EntityId, _config.AssemblerHoldTtlTicks, string.Empty);
        }

        /// <summary>Clears per-cycle hold-announcement deduplication. Called at the top of each Autocraft dispatch pass.</summary>
        private void BridgeResetHoldAnnouncementCycle()
        {
            _bridgeAnnouncedAsmThisCycle.Clear();
        }

        /// <summary>Renders the peer-status line for the [CCraft] LCD header (or the Echo display). Appends nothing when the bridge is disabled or absent.</summary>
        public void AppendBridgeStatusLine(StringBuilder sb)
        {
            if (_bridge == null || !_bridge.Enabled)
            {
                return;
            }

            BridgePeerStatus status = _bridge.PeerStatus;
            sb.Append("Goose: ");
            if (status.LastSeenTick < 0)
            {
                sb.Append("not seen");
            }
            else if (status.Linked)
            {
                long ageTicks = _mainTickCount - status.LastSeenTick;
                if (ageTicks < 0)
                {
                    ageTicks = 0;
                }
                sb.Append("linked ").Append(ageTicks).Append("t ago");
            }
            else
            {
                sb.Append("stale");
            }
            sb.Append('\n');
        }
    }
}
