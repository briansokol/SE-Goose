using System.Collections.Generic;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    public partial class Program : Sandbox.ModAPI.Ingame.MyGridProgram
    {
        /// <summary>Tunable runtime configuration parsed from the PB's CustomData.</summary>
        public class CraneConfig
        {
            /// <summary>Name of the block group Crane scans for managed blocks.</summary>
            public string GroupName = "Crane";

            /// <summary>Master kill-switch for autocraft.</summary>
            public bool EnableAutocraft = true;

            /// <summary>Maximum total queue depth Crane will maintain per (assembler, blueprint) pair.</summary>
            public int AutocraftMaxQueueDepth = 100;

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
            _config.GroupName = MyIniHelpers.GetString(_ini, "Crane", "groupName", _config.GroupName);
            _config.EnableAutocraft = MyIniHelpers.GetBool(_ini, "Crane", "enableAutocraft", true);
            _config.AutocraftMaxQueueDepth = MyIniHelpers.GetInt(_ini, "Crane", "autocraftMaxQueueDepth", 100);
            _config.RescanIntervalTicks = MyIniHelpers.GetInt(_ini, "Crane", "rescanIntervalTicks", 600);
            _config.BudgetFraction = MyIniHelpers.GetFloat(_ini, "Crane", "budgetFraction", 0.8f);
            _config.DebugLogging = MyIniHelpers.GetBool(_ini, "Crane", "debugLogging", false);
            _config.MaxActionLogEntries = MyIniHelpers.GetInt(_ini, "Crane", "maxActionLogEntries", 48);
            _config.MaxWarningEntries = MyIniHelpers.GetInt(_ini, "Crane", "maxWarningEntries", 32);

            EnsureConfigKeysPopulated();

            _logger.MaxActionLogEntries = _config.MaxActionLogEntries;
            _logger.MaxWarningEntries = _config.MaxWarningEntries;

            _configDirty = false;
            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>Live-merges the recognised keys into the PB CustomData when they are missing. Existing keys and user comments are preserved.</summary>
        private void EnsureConfigKeysPopulated()
        {
            bool changed = false;
            if (!_ini.ContainsKey("Crane", "groupName"))
            {
                _ini.Set("Crane", "groupName", _config.GroupName);
                _ini.SetComment("Crane", "groupName",
                    "Block group Crane will manage. Add assemblers, [CCraft] LCDs, and [CError] LCDs to this group.");
                changed = true;
            }
            if (!_ini.ContainsKey("Crane", "enableAutocraft"))
            {
                _ini.Set("Crane", "enableAutocraft", true);
                _ini.SetComment("Crane", "enableAutocraft", "Master autocraft kill-switch.");
                changed = true;
            }
            if (!_ini.ContainsKey("Crane", "autocraftMaxQueueDepth"))
            {
                _ini.Set("Crane", "autocraftMaxQueueDepth", 100);
                _ini.SetComment("Crane", "autocraftMaxQueueDepth",
                    "Maximum total queue depth Crane will maintain per (assembler, blueprint) pair.");
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
