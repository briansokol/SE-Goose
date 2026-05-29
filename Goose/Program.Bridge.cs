using System.Collections.Generic;
using System.Text;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program
    {
        /// <summary>Same-grid bridge to Crane. <c>null</c> when bridge construction failed.</summary>
        private Bridge _bridge;

        /// <summary>Backing IGC transport for <see cref="_bridge"/>.</summary>
        private IgcBridgeTransport _bridgeTransport;

        /// <summary>Monotonic main-loop tick count, advanced on every <see cref="Main"/> invocation. Used as the bridge's clock.</summary>
        private long _mainTickCount;

        /// <summary>Echo-line buffer reused on each <see cref="RenderEchoStatus"/> call.</summary>
        private readonly StringBuilder _bridgeEchoSb = new StringBuilder();

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
                    BridgeRole.Goose,
                    Catalog_HandlePeerKey,
                    BridgeLocalCatalogCount,
                    BridgeLocalCatalogKeys,
                    BridgeLogWarning);
                _bridge.SetItemCountResponder(GooseResolveItemCount);
                ApplyBridgeConfig();
                _bridge.Initialize();
            }
            catch (System.Exception ex)
            {
                LogWarning("Bridge init failed: " + ex.Message);
                _bridge = null;
                _bridgeTransport = null;
            }
        }

        /// <summary>Pushes the latest <see cref="GooseConfig"/> bridge fields into the live bridge. Safe to call repeatedly; channel-tag changes require a recompile.</summary>
        private void ApplyBridgeConfig()
        {
            if (_bridge == null)
            {
                return;
            }
            _bridge.Enabled = _config.EnableBridge;
            _bridge.HeartbeatTicks = _config.BridgeHeartbeatTicks;
            _bridge.MaxHolds = _config.BridgeMaxHoldsTracked;
        }

        /// <summary>Returns the local catalog's key count, sampled on each bridge heartbeat.</summary>
        private int BridgeLocalCatalogCount()
        {
            return _knownItems.Count;
        }

        /// <summary>Enumerates the local catalog's keys for snapshot emission.</summary>
        private IEnumerable<string> BridgeLocalCatalogKeys()
        {
            return _knownItems.Keys;
        }

        /// <summary>Resolves the grid-wide total for a catalog key (<c>Type/Subtype</c>) from the latest inventory scan. Returns 0 for absent or unparseable keys: Goose scans every inventory block, so an absent key means none exist on the grid.</summary>
        private long GooseResolveItemCount(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return 0;
            }

            MyItemType type;
            try
            {
                type = MyItemType.Parse("MyObjectBuilder_" + key);
            }
            catch (System.Exception)
            {
                return 0;
            }

            long total;
            _itemTotals.TryGetValue(type, out total);
            return total;
        }

        /// <summary>Surfaces bridge-internal warnings through the standard one-shot warning channel.</summary>
        private void BridgeLogWarning(string message)
        {
            LogWarningOnce("bridge", "[Goose] bridge: " + message);
        }

        /// <summary>Appends a peer-status line to <paramref name="sb"/> for the Echo display. Idempotent; appends nothing when the bridge is disabled or absent.</summary>
        private void AppendBridgeEchoLine(StringBuilder sb)
        {
            if (_bridge == null || !_bridge.Enabled)
            {
                return;
            }

            BridgePeerStatus status = _bridge.PeerStatus;
            sb.Append("Crane: ");
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
                if (_bridge.ActiveHoldCount > 0)
                {
                    sb.Append(" (").Append(_bridge.ActiveHoldCount).Append(" holds)");
                }
            }
            else
            {
                sb.Append("stale");
            }
            sb.Append('\n');
        }
    }
}
