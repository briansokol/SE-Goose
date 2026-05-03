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

            /// <summary>Per-reactor target as a percent (0-100) of the reactor's inventory volume to fill with <c>Ingot/Uranium</c>; <c>0</c> disables reactor balancing.</summary>
            public int ReactorUraniumFillPercent = 0;

            /// <summary>Per-block target as a percent (0-100) of the gas generator's or irrigation system's inventory volume to fill with <c>Ore/Ice</c>; <c>0</c> disables.</summary>
            public int GasIceFillPercent = 0;

            /// <summary>Per-weapon target fill as a percent (0-100) of the weapon's inventory volume; <c>0</c> disables.</summary>
            public int WeaponAmmoFillPercent = 0;

            /// <summary>Master kill-switch for connector federation. When false, <c>[Federate]</c>-tagged connectors are ignored.</summary>
            public bool EnableConnectorFederation = true;
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
            _config.EnableConnectorFederation = _ini.Get("Goose", "enableConnectorFederation").ToBoolean(true);

            int reactorRaw = _ini.Get("Goose", "reactorUraniumFillPercent").ToInt32(0);
            int reactorClamped = ClampPercent(reactorRaw);
            if (reactorRaw != reactorClamped) {
                LogWarningOnce("balancer:bad-percent:reactorUraniumFillPercent",
                    "[Goose] reactorUraniumFillPercent must be 0-100; clamped to " + reactorClamped + " (was " + reactorRaw + ")");
            }
            _config.ReactorUraniumFillPercent = reactorClamped;

            int gasRaw = _ini.Get("Goose", "gasIceFillPercent").ToInt32(0);
            int gasClamped = ClampPercent(gasRaw);
            if (gasRaw != gasClamped) {
                LogWarningOnce("balancer:bad-percent:gasIceFillPercent",
                    "[Goose] gasIceFillPercent must be 0-100; clamped to " + gasClamped + " (was " + gasRaw + ")");
            }
            _config.GasIceFillPercent = gasClamped;

            int weaponRaw = _ini.Get("Goose", "weaponAmmoFillPercent").ToInt32(0);
            int weaponClamped = ClampPercent(weaponRaw);
            if (weaponRaw != weaponClamped) {
                LogWarningOnce("balancer:bad-percent:weaponAmmoFillPercent",
                    "[Goose] weaponAmmoFillPercent must be 0-100; clamped to " + weaponClamped + " (was " + weaponRaw + ")");
            }
            _config.WeaponAmmoFillPercent = weaponClamped;

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

            EnsureBalancerKeysPopulated();

            _configDirty = false;
            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>Splits a quota key shaped like <c>Type/Subtype</c> into its prefixed
        /// type id and subtype id. Pure: validates only the textual shape; does not call
        /// <see cref="MyItemType.Parse"/>.</summary>
        /// <param name="key">Quota key (e.g. <c>Component/SteelPlate</c> or <c>MyObjectBuilder_Ingot/Iron</c>).</param>
        /// <param name="typeIdWithPrefix">Full type id including the <c>MyObjectBuilder_</c> prefix.</param>
        /// <param name="subtypeId">Subtype portion of the key.</param>
        /// <returns><c>true</c> when the key has a valid <c>Type/Subtype</c> shape.</returns>
        internal static bool TryParseQuotaKeyShape(string key, out string typeIdWithPrefix, out string subtypeId) {
            typeIdWithPrefix = null;
            subtypeId = null;
            if (string.IsNullOrEmpty(key)) return false;
            int slash = key.IndexOf('/');
            if (slash <= 0 || slash >= key.Length - 1) return false;
            string typeHalf = key.Substring(0, slash);
            string subHalf = key.Substring(slash + 1);
            typeIdWithPrefix = typeHalf.StartsWith("MyObjectBuilder_", StringComparison.Ordinal)
                ? typeHalf
                : "MyObjectBuilder_" + typeHalf;
            subtypeId = subHalf;
            return true;
        }

        /// <summary>Wraps <see cref="MyItemType.Parse"/> to return a nullable on failure
        /// instead of throwing. Used as the default type resolver in production paths.</summary>
        internal static MyItemType? ResolveItemTypeViaParse(string fullyQualified) {
            try {
                return MyItemType.Parse(fullyQualified);
            } catch {
                return null;
            }
        }

        /// <summary>Parses a quota value into an amount and mode. Accepts a bare integer
        /// (Exact), integer + <c>M</c>/<c>m</c> (Minimum), integer + <c>L</c>/<c>l</c>
        /// (Limiter), or literal <c>All</c>/<c>all</c> (uncapped). Pure helper.</summary>
        /// <param name="raw">Raw value (e.g. <c>100</c>, <c>500M</c>, <c>250L</c>, <c>All</c>).</param>
        /// <param name="amount">Parsed amount (0 when <paramref name="mode"/> is <see cref="QuotaMode.All"/>).</param>
        /// <param name="mode">Resolved quota mode.</param>
        /// <returns><c>true</c> when the value parses cleanly.</returns>
        internal static bool TryParseQuotaValue(string raw, out long amount, out QuotaMode mode) {
            amount = 0;
            mode = QuotaMode.Exact;
            if (string.IsNullOrEmpty(raw)) return false;
            if (raw.Equals("All", StringComparison.OrdinalIgnoreCase)) {
                mode = QuotaMode.All;
                return true;
            }
            char suffix = raw[raw.Length - 1];
            string numericPart = raw;
            if (suffix == 'M' || suffix == 'm') { mode = QuotaMode.Minimum; numericPart = raw.Substring(0, raw.Length - 1); }
            else if (suffix == 'L' || suffix == 'l') { mode = QuotaMode.Limiter; numericPart = raw.Substring(0, raw.Length - 1); }
            return long.TryParse(numericPart, out amount);
        }


        /// <summary>Clamps a count to be non-negative. Pure helper used by the balancer config parser.</summary>
        /// <param name="raw">Raw integer from CustomData.</param>
        /// <returns><paramref name="raw"/> when non-negative; <c>0</c> otherwise.</returns>
        

        /// <summary>Clamps a percent value to the inclusive range 0-100. Pure helper used by the balancer config parser.</summary>
        /// <param name="raw">Raw integer from CustomData.</param>
        /// <returns><paramref name="raw"/> when in range; <c>0</c> when negative; <c>100</c> when greater than 100.</returns>
        internal static int ClampPercent(int raw) {
            if (raw < 0) return 0;
            if (raw > 100) return 100;
            return raw;
        }

        /// <summary>Parses a CustomData quota line into a typed quota and emits user-facing
        /// warnings on shape or type-resolution failures.</summary>
        /// <param name="key">Quota key (e.g. <c>Component/SteelPlate</c>).</param>
        /// <param name="raw">Raw value (e.g. <c>100</c>, <c>500M</c>, <c>250L</c>, <c>All</c>).</param>
        /// <param name="type">Resolved <see cref="MyItemType"/>.</param>
        /// <param name="quota">Resolved quota when parsing succeeds.</param>
        /// <returns><c>true</c> when both key and value parse cleanly; <c>false</c> otherwise.</returns>
        bool TryReadStockQuota(string key, string raw, out MyItemType type, out StockQuota quota) {
            type = default(MyItemType);
            quota = null;
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(raw)) return false;

            string typeIdWithPrefix, subtypeId;
            if (!TryParseQuotaKeyShape(key, out typeIdWithPrefix, out subtypeId)) {
                LogWarningOnce("stockq:legacy:" + key,
                    "[Goose] Stock quota key '" + key + "' must be fully qualified as Type/Subtype (e.g. Component/SteelPlate). Skipped.");
                return false;
            }

            MyItemType? resolved = ResolveItemTypeViaParse(typeIdWithPrefix + "/" + subtypeId);
            if (!resolved.HasValue) {
                LogWarningOnce("stockq:parse:" + key,
                    "[Goose] Stock quota key '" + key + "' did not resolve to a valid item type.");
                return false;
            }
            type = resolved.Value;

            long amount;
            QuotaMode mode;
            if (!TryParseQuotaValue(raw, out amount, out mode)) return false;
            quota = new StockQuota { Amount = amount, Mode = mode };
            return true;
        }


        /// <summary>Live-merges the three balancer keys into the PB CustomData when they are missing. Existing keys and user comments are preserved; only the absent keys are added with a default value of <c>0</c> and a one-line hint. Writes back to <see cref="MyGridProgram.Me"/>'s CustomData and updates <see cref="_lastSeenCustomData"/> only when something changed, to avoid retriggering a parse on the next cycle.</summary>
        void EnsureBalancerKeysPopulated() {
            bool changed = false;
            if (!_ini.ContainsKey("Goose", "reactorUraniumFillPercent")) {
                _ini.Set("Goose", "reactorUraniumFillPercent", 0);
                _ini.SetComment("Goose", "reactorUraniumFillPercent",
                    "Percent (0-100) of each reactor's inventory volume to fill with Uranium. 0 disables. " +
                    "Per-block override: name-tag [Balance=N] for unit count; [NoBalance] to opt out.");
                changed = true;
            }
            if (!_ini.ContainsKey("Goose", "gasIceFillPercent")) {
                _ini.Set("Goose", "gasIceFillPercent", 0);
                _ini.SetComment("Goose", "gasIceFillPercent",
                    "Percent (0-100) of each gas generator / irrigation block's inventory volume to fill with Ice. 0 disables.");
                changed = true;
            }
            if (!_ini.ContainsKey("Goose", "weaponAmmoFillPercent")) {
                _ini.Set("Goose", "weaponAmmoFillPercent", 0);
                _ini.SetComment("Goose", "weaponAmmoFillPercent",
                    "Percent (0-100) of each weapon's inventory volume to fill with ammo. 0 disables.");
                changed = true;
            }
            if (changed) {
                Me.CustomData = _ini.ToString();
                _lastSeenCustomData = Me.CustomData;
            }
        }
    }
}
