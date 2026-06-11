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

            /// <summary>Optional terminal-group name. When set, Crane manages only that group's member blocks and tagged blocks (e.g. <c>[Federate]</c> connectors) are ignored unless they belong to the group. Empty (default) uses the wider grid-based scope.</summary>
            public string BlockGroup = "";

            /// <summary>Maximum total queue depth Crane will maintain per (assembler, blueprint) pair.</summary>
            public int AutocraftMaxQueueDepth = 100;

            /// <summary>Per-ingot floor the feeder maintains in each assembler's input inventory while it has assembly work queued.</summary>
            public float AssemblerIngotKeep = 50f;

            /// <summary>Ticks between automatic rescans of managed blocks.</summary>
            public int RescanIntervalTicks = 60;

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
            public int BridgeHeartbeatTicks = 6;

            /// <summary>TTL (in main-loop ticks) attached to each assembler-hold announcement. Long enough to cover one Goose balance cycle plus margin; short enough that a crashed Crane self-heals quickly.</summary>
            public int AssemblerHoldTtlTicks = 30;

            /// <summary>Master kill-switch for refinery balancing (ore feeding into managed refineries).</summary>
            public bool EnableRefineryBalancing = true;

            /// <summary>Target fill level (percent of input capacity) Crane tops each managed refinery's input toward.</summary>
            public int RefineryTargetFillPercent = 50;

            /// <summary>Default low watermark: an ingot below this (with no explicit [CRefine] threshold) makes its ore high priority. <c>0</c> disables.</summary>
            public long RefineDefaultIngotMin = 500;

            /// <summary>Default high watermark: a high-priority ingot reverts to normal priority once it reaches this. <c>0</c> disables.</summary>
            public long RefineDefaultIngotMax = 1000;
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
        /// <summary>INI section name for all Crane PB config keys.</summary>
        private const string IniSection = "Crane";

        /// <summary>Config key definitions: name, default value (string/int/float/bool), help comment.</summary>
        private static readonly object[][] ConfigKeyDefs =
        {
            new object[] { "enableAutocraft", true, "Master autocraft kill-switch." },
            new object[] { "enableConnectorFederation", true, "When true, [Federate]-tagged connectors on this grid admit the docked remote grid into Crane's management scope." },
            new object[]
            {
                "blockGroup", "",
                "Optional terminal-group name. When set, Crane manages ONLY blocks in this group; " +
                "[Federate] connectors and grid traversal are ignored. Empty (default) uses grid-based scope. " +
                "Edit then run 'rescan', or wait for the next automatic rescan, to pick up group membership changes."
            },
            new object[] { "autocraftMaxQueueDepth", 100, "Maximum total queue depth Crane will maintain per (assembler, blueprint) pair." },
            new object[] { "assemblerIngotKeep", 50f, "Per-ingot floor the feeder maintains in each assembler's input inventory while it has assembly work queued." },
            new object[]
            {
                "enableBridge", true,
                "Master kill-switch for the Goose-Crane bridge. When false, the bridge sends nothing, " +
                "ignores incoming traffic, and behaves identically to a script without the bridge."
            },
            new object[] { "bridgeChannelTag", BridgeProtocol.DefaultChannelTag, "IGC broadcast tag used by the bridge. Use a custom value if you run multiple Goose/Crane pairs on one grid." },
            new object[]
            {
                "bridgeHeartbeatTicks", 6,
                "Heartbeat cadence in main-loop ticks (Update100 ~ 0.6 runs/sec; default 6 ~ 10s). " +
                "Also drives hello resend while no peer is linked."
            },
            new object[]
            {
                "assemblerHoldTtlTicks", 30,
                "TTL on assembler-hold announcements (in main-loop ticks). " +
                "Default 30 ~ 5s: long enough for one Goose balance cycle, short enough to self-heal on Crane crash."
            },
            new object[]
            {
                "enableRefineryBalancing", true,
                "Master kill-switch for refinery balancing. When true, Crane takes over each in-scope " +
                "refinery's input (turns off its conveyor system) and feeds it ore per the [CRefine] order."
            },
            new object[] { "refineryTargetFillPercent", 50, "Target fill level (percent of input capacity) Crane tops each managed refinery's input toward." },
            new object[]
            {
                "refineDefaultIngotMin", 500,
                "Default low watermark for ingots with no explicit [CRefine] threshold: below this, the ore becomes " +
                "high priority. It stays high until the ingot reaches refineDefaultIngotMax (hysteresis). 0 disables."
            },
            new object[] { "refineDefaultIngotMax", 1000, "Default high watermark: a high-priority ingot reverts to normal priority once it reaches this. 0 disables." },
        };

        /// <summary>Writes any missing config keys from <see cref="ConfigKeyDefs"/> into the INI.</summary>
        /// <returns>True if any key was added.</returns>
        private bool EnsureKeyDefs()
        {
            bool changed = false;
            foreach (object[] def in ConfigKeyDefs)
            {
                string key = (string)def[0];
                if (_ini.ContainsKey(IniSection, key))
                {
                    continue;
                }
                object v = def[1];
                if (v is bool)
                {
                    _ini.Set(IniSection, key, (bool)v);
                }
                else if (v is int)
                {
                    _ini.Set(IniSection, key, (int)v);
                }
                else if (v is float)
                {
                    _ini.Set(IniSection, key, (float)v);
                }
                else
                {
                    _ini.Set(IniSection, key, (string)v);
                }
                _ini.SetComment(IniSection, key, (string)def[2]);
                changed = true;
            }
            return changed;
        }

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
            _config.EnableAutocraft = MyIniHelpers.GetBool(_ini, IniSection, "enableAutocraft", true);
            _config.EnableConnectorFederation = MyIniHelpers.GetBool(_ini, IniSection, "enableConnectorFederation", true);
            _config.BlockGroup = (_ini.Get(IniSection, "blockGroup").ToString("") ?? "").Trim();
            _config.AutocraftMaxQueueDepth = MyIniHelpers.GetInt(_ini, IniSection, "autocraftMaxQueueDepth", 100);
            _config.AssemblerIngotKeep = MyIniHelpers.GetFloat(_ini, IniSection, "assemblerIngotKeep", 50f);
            _config.RescanIntervalTicks = MyIniHelpers.GetInt(_ini, IniSection, "rescanIntervalTicks", 60);
            _config.BudgetFraction = MyIniHelpers.GetFloat(_ini, IniSection, "budgetFraction", 0.8f);
            _config.DebugLogging = MyIniHelpers.GetBool(_ini, IniSection, "debugLogging", false);
            _config.MaxActionLogEntries = MyIniHelpers.GetInt(_ini, IniSection, "maxActionLogEntries", 48);
            _config.MaxWarningEntries = MyIniHelpers.GetInt(_ini, IniSection, "maxWarningEntries", 32);
            _config.EnableBridge = MyIniHelpers.GetBool(_ini, IniSection, "enableBridge", true);
            _config.BridgeChannelTag = _ini.Get(IniSection, "bridgeChannelTag").ToString(BridgeProtocol.DefaultChannelTag);
            int bridgeHbRaw = MyIniHelpers.GetInt(_ini, IniSection, "bridgeHeartbeatTicks", 6);
            _config.BridgeHeartbeatTicks = bridgeHbRaw < 6 ? 6 : bridgeHbRaw;
            int holdTtlRaw = MyIniHelpers.GetInt(_ini, IniSection, "assemblerHoldTtlTicks", 30);
            _config.AssemblerHoldTtlTicks = holdTtlRaw < 1 ? 1 : holdTtlRaw;
            _config.EnableRefineryBalancing = MyIniHelpers.GetBool(_ini, IniSection, "enableRefineryBalancing", true);
            int fillRaw = MyIniHelpers.GetInt(_ini, IniSection, "refineryTargetFillPercent", 50);
            _config.RefineryTargetFillPercent = fillRaw < 0 ? 0 : (fillRaw > 100 ? 100 : fillRaw);
            int defMinRaw = MyIniHelpers.GetInt(_ini, IniSection, "refineDefaultIngotMin", 500);
            _config.RefineDefaultIngotMin = defMinRaw < 0 ? 0 : defMinRaw;
            int defMaxRaw = MyIniHelpers.GetInt(_ini, IniSection, "refineDefaultIngotMax", 1000);
            _config.RefineDefaultIngotMax = defMaxRaw < 0 ? 0 : defMaxRaw;
            LoadRefineConfig();

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
            bool changed = EnsureKeyDefs();
            if (!_ini.ContainsSection("CRefine"))
            {
                _ini.Set("CRefine", "order", DefaultRefineOrder);
                _ini.SetComment("CRefine", "order",
                    "Ore feed priority. Crane fills and orders refinery inputs in this order.\n" +
                    "Per-ingot thresholds (add lines below):  <IngotSubtype> = <min>,<max>\n" +
                    "  ingot below min      -> ore becomes high priority (bumped to the front)\n" +
                    "  stays high until ingot reaches max, then reverts to normal (still fed)\n" +
                    "  omitted ingots use refineDefaultIngotMin/Max. Example:  Iron = 5000,8000");
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
