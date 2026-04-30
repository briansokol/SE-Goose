using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript {
    public partial class Program : MyGridProgram {
        /// <summary>How an item's <see cref="StockQuota.Amount"/> should be interpreted.</summary>
        public enum QuotaMode {
            /// <summary>Pull up to, and push excess above, the target amount.</summary>
            Exact,
            /// <summary>Pull up to the target amount; never push.</summary>
            Minimum,
            /// <summary>Push excess above the target amount; never pull.</summary>
            Limiter,
            /// <summary>Pull all available stock without an upper bound.</summary>
            All
        }

        /// <summary>A single stock-quota rule parsed from a <c>[Stock]</c> container's CustomData.</summary>
        public class StockQuota {
            /// <summary>Target item count (ignored when <see cref="Mode"/> is <see cref="QuotaMode.All"/>).</summary>
            public long Amount;

            /// <summary>How <see cref="Amount"/> is enforced.</summary>
            public QuotaMode Mode;
        }

        /// <summary>Tunable runtime configuration parsed from the PB's CustomData.</summary>
        public class GooseConfig {
            /// <summary>Ticks between automatic rescans of managed blocks.</summary>
            public int RescanIntervalTicks = 600;

            /// <summary>Fraction of the per-tick instruction budget the script may consume before yielding.</summary>
            public float BudgetFraction = 0.5f;

            /// <summary>When true, every transfer is added to the action log.</summary>
            public bool DebugLogging = false;

            /// <summary>Maximum number of recent actions retained for the Echo display.</summary>
            public int MaxActionLogEntries = 48;

            /// <summary>Maximum number of distinct warnings retained before eviction.</summary>
            public int MaxWarningEntries = 32;
        }

        /// <summary>Reusable INI parser for both PB and per-block CustomData.</summary>
        MyIni _ini = new MyIni();

        /// <summary>Active configuration; replaced on each successful parse.</summary>
        GooseConfig _config = new GooseConfig();

        /// <summary>Set when configuration must be reparsed on the next config step.</summary>
        bool _configDirty = true;

        /// <summary>CustomData string seen on the previous parse; used for change detection.</summary>
        string _lastSeenCustomData = null;

        /// <summary>Reparses PB CustomData into <see cref="_config"/> when it has changed or a rescan was requested.</summary>
        IEnumerator<YieldReason> StepParseConfigIfDirty() {
            if (Me.CustomData != _lastSeenCustomData) _configDirty = true;
            if (!_configDirty) {
                yield return YieldReason.ChunkBoundary;
                yield break;
            }
            _lastSeenCustomData = Me.CustomData;
            MyIniParseResult result;
            if (!_ini.TryParse(Me.CustomData, out result)) {
                LogWarning("[Goose] CustomData parse failed: " + result.ToString());
                yield return YieldReason.ChunkBoundary;
                yield break;
            }
            _config.RescanIntervalTicks = _ini.Get("Goose", "rescanIntervalTicks").ToInt32(600);
            _config.BudgetFraction = (float)_ini.Get("Goose", "budgetFraction").ToDouble(0.5);
            _config.DebugLogging = _ini.Get("Goose", "debugLogging").ToBoolean(false);
            _config.MaxActionLogEntries = _ini.Get("Goose", "maxActionLogEntries").ToInt32(48);
            _config.MaxWarningEntries = _ini.Get("Goose", "maxWarningEntries").ToInt32(32);

            _categoryOverrides.Clear();
            List<MyIniKey> keys = new List<MyIniKey>();
            _ini.GetKeys("Goose", keys);
            for (int i = 0; i < keys.Count; i++) {
                string name = keys[i].Name;
                if (!name.StartsWith("Override.")) continue;
                string fullId = name.Substring("Override.".Length);
                string val = _ini.Get(keys[i]).ToString();
                ItemCategory cat;
                if (Enum.TryParse(val, true, out cat)) {
                    _categoryOverrides[fullId] = cat;
                } else {
                    LogWarning("[Goose] Unknown category '" + val + "' for override " + name);
                }
            }
            _configDirty = false;
            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>
        /// Resolves an <c>Override.&lt;subtype&gt; = &lt;value&gt;</c> entry into a typed quota.
        /// Numeric values may end with <c>M</c> for <see cref="QuotaMode.Minimum"/> or
        /// <c>L</c> for <see cref="QuotaMode.Limiter"/>; the literal <c>All</c> selects <see cref="QuotaMode.All"/>.
        /// </summary>
        /// <param name="key">Subtype id used to look up the matching <see cref="MyItemType"/>.</param>
        /// <param name="raw">Raw value string from CustomData.</param>
        /// <param name="type">Resolved item type.</param>
        /// <param name="quota">Parsed quota; <c>null</c> on failure.</param>
        /// <returns>True if both the type and the quota parse successfully.</returns>
        bool TryReadStockQuota(string key, string raw, out MyItemType type, out StockQuota quota) {
            type = default(MyItemType);
            quota = null;
            if (string.IsNullOrEmpty(raw)) return false;
            if (!_knownSubtypes.TryGetValue(key, out type)) return false;

            if (raw.Equals("All", StringComparison.OrdinalIgnoreCase)) {
                quota = new StockQuota { Amount = 0, Mode = QuotaMode.All };
                return true;
            }
            char suffix = raw[raw.Length - 1];
            string numericPart = raw;
            QuotaMode mode = QuotaMode.Exact;
            if (suffix == 'M' || suffix == 'm') { mode = QuotaMode.Minimum; numericPart = raw.Substring(0, raw.Length - 1); }
            else if (suffix == 'L' || suffix == 'l') { mode = QuotaMode.Limiter; numericPart = raw.Substring(0, raw.Length - 1); }
            long amount;
            if (!long.TryParse(numericPart, out amount)) return false;
            quota = new StockQuota { Amount = amount, Mode = mode };
            return true;
        }
    }
}
