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
        public enum QuotaMode { Exact, Minimum, Limiter, All }

        public class StockQuota {
            public long Amount;
            public QuotaMode Mode;
        }

        public class GooseConfig {
            public int RescanIntervalTicks = 600;
            public float BudgetFraction = 0.5f;
            public bool DebugLogging = false;
            public int MaxActionLogEntries = 48;
            public int MaxWarningEntries = 32;
        }

        MyIni _ini = new MyIni();
        GooseConfig _config = new GooseConfig();
        bool _configDirty = true;
        string _lastSeenCustomData = null;

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

            // Override.* keys (full ID -> category enum).
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
