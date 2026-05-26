using System.Collections.Generic;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    public partial class Program : Sandbox.ModAPI.Ingame.MyGridProgram
    {
        /// <summary>Tunable runtime configuration parsed from the PB's CustomData.</summary>
        public class CraneConfig
        {
            /// <summary>Master kill-switch for autocraft.</summary>
            public bool EnableAutocraft = true;

            /// <summary>When true, [Federate]-tagged connectors on Me.CubeGrid admit the docked remote grid into Crane's management scope.</summary>
            public bool EnableConnectorFederation = true;

            /// <summary>Maximum total queue depth Crane will maintain per (assembler, blueprint) pair.</summary>
            public int AutocraftMaxQueueDepth = 100;

            /// <summary>Per-ingot floor the feeder maintains in each assembler's input inventory while it has assembly work queued.</summary>
            public float AssemblerIngotKeep = 50f;

            /// <summary>Ticks between automatic rescans of managed blocks.</summary>
            public int RescanIntervalTicks = 600;

            /// <summary>Fraction of the per-tick instruction budget Crane may consume before yielding.</summary>
            public float BudgetFraction = 0.8f;

            /// <summary>When true, every queue mutation is added to the action log.</summary>
            public bool DebugLogging = false;

            /// <summary>Maximum number of recent actions retained for the Echo display.</summary>
            public int MaxActionLogEntries = 48;

            /// <summary>Maximum number of distinct warnings retained before eviction.</summary>
            public int MaxWarningEntries = 32;

            /// <summary>Master kill-switch for the Goose-Crane bridge. When false, all bridge traffic and peer-aware behavior are suppressed.</summary>
            public bool EnableBridge = true;

            /// <summary>IGC broadcast tag used by the bridge. Peers must agree on this string to exchange messages.</summary>
            public string BridgeChannelTag = BridgeProtocol.DefaultChannelTag;

            /// <summary>Heartbeat cadence in main-loop ticks. Also drives <c>hello</c> resend while no peer is linked.</summary>
            public int BridgeHeartbeatTicks = 60;

            /// <summary>TTL (in main-loop ticks) attached to each assembler-hold announcement. Long enough to cover one Goose balance cycle plus margin; short enough that a crashed Crane self-heals quickly.</summary>
            public int AssemblerHoldTtlTicks = 30;
        }

        /// <summary>Reusable INI parser for both PB and per-block CustomData.</summary>
        private readonly MyIni _ini = new MyIni();

        /// <summary>Active configuration; replaced on each successful parse.</summary>
        private readonly CraneConfig _config = new CraneConfig();

        /// <summary>Set when configuration must be reparsed on the next config step.</summary>
        private bool _configDirty = true;

        /// <summary>CustomData string seen on the previous parse; used for change detection.</summary>
        private string _lastSeenCustomData = null;

        /// <summary>Parses the PB CustomData into <see cref="_config"/> when it has changed or a rescan was requested.</summary>
        private IEnumerator<YieldReason> StepParseConfigIfDirty()
        {
            if (Me.CustomData != _lastSeenCustomData)
            {
                _configDirty = true;
            }

            if (!_configDirty)
            {
                yield return YieldReason.ChunkBoundary;
                yield break;
            }
            _lastSeenCustomData = Me.CustomData;
            MyIniParseResult result;
            if (!_ini.TryParse(Me.CustomData, out result))
            {
                _logger.LogWarning("[Crane] CustomData parse failed: " + result.ToString());
                yield return YieldReason.ChunkBoundary;
                yield break;
            }
            _config.EnableAutocraft = MyIniHelpers.GetBool(_ini, "Crane", "enableAutocraft", true);
            _config.EnableConnectorFederation = MyIniHelpers.GetBool(_ini, "Crane", "enableConnectorFederation", true);
            _config.AutocraftMaxQueueDepth = MyIniHelpers.GetInt(_ini, "Crane", "autocraftMaxQueueDepth", 100);
            _config.AssemblerIngotKeep = MyIniHelpers.GetFloat(_ini, "Crane", "assemblerIngotKeep", 50f);
            _config.RescanIntervalTicks = MyIniHelpers.GetInt(_ini, "Crane", "rescanIntervalTicks", 600);
            _config.BudgetFraction = MyIniHelpers.GetFloat(_ini, "Crane", "budgetFraction", 0.8f);
            _config.DebugLogging = MyIniHelpers.GetBool(_ini, "Crane", "debugLogging", false);
            _config.MaxActionLogEntries = MyIniHelpers.GetInt(_ini, "Crane", "maxActionLogEntries", 48);
            _config.MaxWarningEntries = MyIniHelpers.GetInt(_ini, "Crane", "maxWarningEntries", 32);
            _config.EnableBridge = MyIniHelpers.GetBool(_ini, "Crane", "enableBridge", true);
            _config.BridgeChannelTag = _ini.Get("Crane", "bridgeChannelTag").ToString(BridgeProtocol.DefaultChannelTag);
            int bridgeHbRaw = MyIniHelpers.GetInt(_ini, "Crane", "bridgeHeartbeatTicks", 60);
            _config.BridgeHeartbeatTicks = bridgeHbRaw < 6 ? 6 : bridgeHbRaw;
            int holdTtlRaw = MyIniHelpers.GetInt(_ini, "Crane", "assemblerHoldTtlTicks", 30);
            _config.AssemblerHoldTtlTicks = holdTtlRaw < 1 ? 1 : holdTtlRaw;

            EnsureConfigKeysPopulated();
            ApplyBridgeConfig();

            _logger.MaxActionLogEntries = _config.MaxActionLogEntries;
            _logger.MaxWarningEntries = _config.MaxWarningEntries;

            _configDirty = false;
            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>Live-merges the recognised keys into the PB CustomData when they are missing. Existing keys and user comments are preserved.</summary>
        private void EnsureConfigKeysPopulated()
        {
            bool changed = false;
            if (!_ini.ContainsKey("Crane", "enableAutocraft"))
            {
                _ini.Set("Crane", "enableAutocraft", true);
                _ini.SetComment("Crane", "enableAutocraft", "Master autocraft kill-switch.");
                changed = true;
            }
            if (!_ini.ContainsKey("Crane", "enableConnectorFederation"))
            {
                _ini.Set("Crane", "enableConnectorFederation", true);
                _ini.SetComment("Crane", "enableConnectorFederation",
                    "When true, [Federate]-tagged connectors on this grid admit the docked remote grid into Crane's management scope.");
                changed = true;
            }
            if (!_ini.ContainsKey("Crane", "autocraftMaxQueueDepth"))
            {
                _ini.Set("Crane", "autocraftMaxQueueDepth", 100);
                _ini.SetComment("Crane", "autocraftMaxQueueDepth",
                    "Maximum total queue depth Crane will maintain per (assembler, blueprint) pair.");
                changed = true;
            }
            if (!_ini.ContainsKey("Crane", "assemblerIngotKeep"))
            {
                _ini.Set("Crane", "assemblerIngotKeep", 50f);
                _ini.SetComment("Crane", "assemblerIngotKeep",
                    "Per-ingot floor the feeder maintains in each assembler's input inventory while it has assembly work queued.");
                changed = true;
            }
            if (!_ini.ContainsKey("Crane", "enableBridge"))
            {
                _ini.Set("Crane", "enableBridge", true);
                _ini.SetComment("Crane", "enableBridge",
                    "Master kill-switch for the Goose-Crane bridge. When false, the bridge sends nothing, " +
                    "ignores incoming traffic, and behaves identically to a script without the bridge.");
                changed = true;
            }
            if (!_ini.ContainsKey("Crane", "bridgeChannelTag"))
            {
                _ini.Set("Crane", "bridgeChannelTag", BridgeProtocol.DefaultChannelTag);
                _ini.SetComment("Crane", "bridgeChannelTag",
                    "IGC broadcast tag used by the bridge. Use a custom value if you run multiple Goose/Crane pairs on one grid.");
                changed = true;
            }
            if (!_ini.ContainsKey("Crane", "bridgeHeartbeatTicks"))
            {
                _ini.Set("Crane", "bridgeHeartbeatTicks", 60);
                _ini.SetComment("Crane", "bridgeHeartbeatTicks",
                    "Heartbeat cadence in main-loop ticks (Update10 ~ 6 ticks/sec; default 60 ~ 10s). " +
                    "Also drives hello resend while no peer is linked.");
                changed = true;
            }
            if (!_ini.ContainsKey("Crane", "assemblerHoldTtlTicks"))
            {
                _ini.Set("Crane", "assemblerHoldTtlTicks", 30);
                _ini.SetComment("Crane", "assemblerHoldTtlTicks",
                    "TTL on assembler-hold announcements (in main-loop ticks). " +
                    "Default 30 ~ 5s: long enough for one Goose balance cycle, short enough to self-heal on Crane crash.");
                changed = true;
            }
            if (changed)
            {
                Me.CustomData = _ini.ToString();
                _lastSeenCustomData = Me.CustomData;
            }
        }
    }
}
