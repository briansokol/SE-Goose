using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;
using VRage;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript {
    public partial class Program : MyGridProgram {
        /// <summary>How an autocraft quota's target amount is enforced.</summary>
        public enum AutocraftMode {
            /// <summary>Crafts up to <see cref="AutocraftQuota.Amount"/> when actual count is below the target. Allows surplus.</summary>
            Minimum,
            /// <summary>Maintains grid total at exactly <see cref="AutocraftQuota.Amount"/> — crafts when below, disassembles when above.</summary>
            Exact
        }

        /// <summary>The reason an item is or is not being worked on by the autocraft engine.</summary>
        public enum AutocraftStatus {
            /// <summary>Item is within tolerance for its quota — no queue action.</summary>
            OK,
            /// <summary>Item is below its Minimum or Exact target and assembly is queued/in-progress.</summary>
            Crafting,
            /// <summary>Item is above its Exact target and disassembly is queued/in-progress.</summary>
            Disassembling,
            /// <summary>Resolver could not find a blueprint; user must queue one of the item manually so observation can capture the mapping.</summary>
            BlockedNeedsLearn,
            /// <summary>No assemblers are present in the pool.</summary>
            BlockedNoAssembler,
            /// <summary>A blueprint resolved but no assembler currently in the pool can build it
            /// (e.g. a Computer quota on a grid with only a Basic Assembler). Distinct from
            /// <see cref="BlockedNeedsLearn"/>: the user fix is to add a capable assembler subtype.</summary>
            BlockedNoCapableAsm,
            /// <summary>Autocraft is disabled via <c>enableAutocraft</c>.</summary>
            Disabled
        }

        /// <summary>Per-item autocraft target parsed from the <c>[Goose]</c> section of the
        /// <c>[GCraft]</c> LCD's CustomData.</summary>
        public class AutocraftQuota {
            /// <summary>Target unit count.</summary>
            public long Amount;
            /// <summary>How the target is enforced (Minimum = craft up, allow surplus; Exact = hold at this number, both directions).</summary>
            public AutocraftMode Mode;
        }

        /// <summary>Active autocraft quotas keyed by catalog item key (e.g. <c>Component/SteelPlate</c>).</summary>
        Dictionary<string, AutocraftQuota> _autocraftTargets = new Dictionary<string, AutocraftQuota>(StringComparer.Ordinal);

        /// <summary>Raw <c>key=value</c> lines for the <c>[Goose]</c> section, preserved verbatim for the
        /// live-merge regenerator so user formatting survives round-trips.</summary>
        Dictionary<string, string> _autocraftActiveQuotaLines = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Per-item engine status surfaced on the <c>[GCraft]</c> LCD.</summary>
        Dictionary<string, AutocraftStatus> _autocraftItemStatus = new Dictionary<string, AutocraftStatus>(StringComparer.Ordinal);

        /// <summary>Per-item queue depth aggregated across the assembler pool (for the status row).</summary>
        Dictionary<string, long> _autocraftQueuedAmount = new Dictionary<string, long>(StringComparer.Ordinal);

        /// <summary>Subset of <see cref="_productionBlocks"/> that are assemblers; refreshed during rescan.</summary>
        List<IMyAssembler> _assemblers = new List<IMyAssembler>();

        /// <summary>Per-assembler blueprint capability cache: <c>entityId → blueprintId → canUse</c>.
        /// Lazily populated by <see cref="Autocraft_AsmCanUse"/>; invalidated wholesale on pool
        /// divergence in <see cref="Autocraft_RefreshAssemblerPool"/>.</summary>
        Dictionary<long, Dictionary<MyDefinitionId, bool>> _capabilityCache = new Dictionary<long, Dictionary<MyDefinitionId, bool>>();

        /// <summary>EntityId snapshot of <see cref="_assemblers"/> (sorted) used to detect pool
        /// churn cheaply during refresh; mismatch triggers a <see cref="_capabilityCache"/> reset.</summary>
        List<long> _capabilityCacheShadow = new List<long>();

        /// <summary>EntityId of the assembler currently held under the assemble-side reservation,
        /// or 0 when no reservation is active. Honored across ticks for hysteresis (see
        /// <see cref="Autocraft_ReservationStillValid"/>).</summary>
        long _autocraftReservedAsmEntityId;

        /// <summary>Per-tick set of assemblers that could not flip mode this tick because their
        /// queue was non-empty in the opposite direction. Excluded from this tick's distribution.</summary>
        HashSet<long> _modeLockedThisTick = new HashSet<long>();

        /// <summary>Discovered <c>[GCraft]</c> LCD block, or <c>null</c> when none is present.</summary>
        IMyTerminalBlock _gcraftLcd;

        /// <summary>Last CustomData blob seen on the <c>[GCraft]</c> LCD; used to skip work when nothing changed.</summary>
        string _gcraftLastSeenCustomData;

        /// <summary>Catalog version recorded the last time the engine regenerated the LCD template;
        /// triggers a regenerate when new items appear so hints stay current.</summary>
        int _gcraftLastCatalogVersion = -1;

        /// <summary>Scratch buffer for <see cref="MyProductionItem"/> queues; allocated once and reused.</summary>
        List<MyProductionItem> _autocraftQueueScratch = new List<MyProductionItem>();

        /// <summary>Scratch buffer for the autocraft engine to iterate assemble-side work assignments.</summary>
        List<string> _autocraftAssembleKeys = new List<string>();

        /// <summary>Scratch buffer for the autocraft engine to iterate disassemble-side work assignments.</summary>
        List<string> _autocraftDisassembleKeys = new List<string>();

        /// <summary>Scratch buffer for parsing user-typed blueprint overrides.</summary>
        Dictionary<string, string> _autocraftBlueprintParseScratch = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Scratch buffer for serializing the auto-resolved blueprint map.</summary>
        Dictionary<string, string> _autocraftAutoBlueprintScratch = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Scratch buffer for collecting current item totals as catalog-key dictionary.</summary>
        Dictionary<string, long> _autocraftTotalsScratch = new Dictionary<string, long>(StringComparer.Ordinal);

        /// <summary>Reusable buffer for rendering the <c>[GCraft]</c> status surface.</summary>
        StringBuilder _gcraftStatusSB = new StringBuilder();

        /// <summary>Tag name (case-sensitive) that flags the autocraft status LCD.</summary>
        internal const string AutocraftLcdTag = "[GCraft]";

        /// <summary>Hard upper bound on per-item queue depth; the runtime configured value is clamped to this ceiling.</summary>
        internal const int AutocraftMaxQueueDepthCeiling = 100000;


        /// <summary>Hardcoded minimum per-asm deficit before the dispatcher spreads across the
        /// capable pool. Below this threshold, the entire deficit lands on a single capable
        /// assembler (lowest entityId). Locked in the multi-assembler dispatch design.</summary>
        internal const int AutocraftSplitMinBatch = 5;

        /// <summary>Reduces a <see cref="MyFixedPoint"/> queue-amount accumulator to a whole-unit
        /// <see cref="long"/>, rounding any fractional remainder UP. Used by the autocraft
        /// reconciler so that an in-progress craft (which SE represents as a fractional remainder
        /// on <see cref="MyProductionItem.Amount"/>) counts as one full pending unit. Truncating
        /// the cast caused an off-by-one where the engine queued one extra unit, settling the grid
        /// at <c>target ± 1</c>.</summary>
        /// <param name="v">Fixed-point queue accumulator (sum of in-flight queue entries).</param>
        /// <returns>The ceiling of <paramref name="v"/> as a non-negative long. Negative inputs
        /// (not expected in practice) return their floor.</returns>
        internal static long Autocraft_CeilFixedPointToLong(MyFixedPoint v) {
            decimal d = (decimal)v;
            long whole = (long)d;
            if (d > whole) whole++;
            return whole;
        }

        /// <summary>Parses an autocraft quota value (e.g. <c>5000</c>, <c>10000E</c>, or <c>x</c>).
        /// Returns <c>false</c> for unrecognized formats or negative amounts. No suffix maps to
        /// <see cref="AutocraftMode.Minimum"/>; <c>E</c>/<c>e</c> maps to <see cref="AutocraftMode.Exact"/>.
        /// The legacy <c>L</c>/<c>l</c> suffix is silently accepted as an alias for <c>E</c> so user
        /// configs written against the prior one-direction "Limiter" semantics keep parsing (and now
        /// gain bidirectional behavior). The literal <c>x</c>/<c>X</c> is the ignore sentinel — caller
        /// should treat this as "unmanaged" and skip adding the item to
        /// <see cref="_autocraftTargets"/>.</summary>
        /// <param name="raw">Trimmed value portion of a <c>key=value</c> quota line.</param>
        /// <param name="amount">Parsed unit count when successful and not ignored; <c>0</c> otherwise.</param>
        /// <param name="mode">Parsed enforcement mode when successful and not ignored.</param>
        /// <param name="ignore"><c>true</c> when <paramref name="raw"/> is the <c>x</c>/<c>X</c> sentinel.</param>
        /// <returns><c>true</c> when the value is well-formed (including the ignore sentinel).</returns>
        internal static bool Autocraft_TryParseQuotaValue(string raw, out long amount, out AutocraftMode mode, out bool ignore) {
            amount = 0;
            mode = AutocraftMode.Minimum;
            ignore = false;
            if (string.IsNullOrEmpty(raw)) return false;
            if (raw.Length == 1 && (raw[0] == 'x' || raw[0] == 'X')) {
                ignore = true;
                return true;
            }
            char suffix = raw[raw.Length - 1];
            string numericPart = raw;
            if (suffix == 'E' || suffix == 'e' || suffix == 'L' || suffix == 'l') {
                mode = AutocraftMode.Exact;
                numericPart = raw.Substring(0, raw.Length - 1);
            }
            if (!long.TryParse(numericPart, out amount)) return false;
            if (amount < 0) return false;
            return true;
        }

        /// <summary>Splits an assembler pool of size <paramref name="poolSize"/> between assemble-side
        /// and disassemble-side work. Implements the per-tick algorithm from the autocraft design:
        /// <list type="bullet">
        ///   <item>Only one side has work → that side claims the whole pool.</item>
        ///   <item>Both sides have work → proportional split rounded to the assemble side, each ≥ 1.</item>
        ///   <item>Pool size 1 → side with more pending work wins; ties → assemble.</item>
        ///   <item>Pool size 0 → both counts 0.</item>
        /// </list>
        /// </summary>
        /// <param name="poolSize">Total assemblers in pool.</param>
        /// <param name="assemblePending">Count of items needing assembly.</param>
        /// <param name="disassemblePending">Count of items needing disassembly.</param>
        /// <param name="assembleCount">Number of assemblers assigned to assemble side.</param>
        /// <param name="disassembleCount">Number of assemblers assigned to disassemble side.</param>
        internal static void Autocraft_ComputePoolSplit(
            int poolSize,
            int assemblePending,
            int disassemblePending,
            out int assembleCount,
            out int disassembleCount) {
            assembleCount = 0;
            disassembleCount = 0;
            if (poolSize <= 0) return;
            if (assemblePending <= 0 && disassemblePending <= 0) return;
            if (assemblePending > 0 && disassemblePending <= 0) {
                assembleCount = poolSize;
                return;
            }
            if (disassemblePending > 0 && assemblePending <= 0) {
                disassembleCount = poolSize;
                return;
            }
            if (poolSize == 1) {
                if (assemblePending >= disassemblePending) assembleCount = 1;
                else disassembleCount = 1;
                return;
            }
            double share = (double)assemblePending / (assemblePending + disassemblePending);
            int assemble = (int)Math.Round(poolSize * share, MidpointRounding.AwayFromZero);
            if (assemble < 1) assemble = 1;
            int disassemble = poolSize - assemble;
            if (disassemble < 1) {
                disassemble = 1;
                assemble = poolSize - 1;
            }
            assembleCount = assemble;
            disassembleCount = disassemble;
        }


        /// <summary>Splits a deficit across a fixed number of capable assemblers using an
        /// even-share rule with a minimum batch threshold.
        /// <list type="bullet">
        ///   <item><paramref name="capableCount"/> <c>&lt;= 0</c> → empty array.</item>
        ///   <item><paramref name="deficit"/> <c>&lt;= 0</c> → length-<paramref name="capableCount"/> all-zero array.</item>
        ///   <item><paramref name="deficit"/> <c>&lt; minBatch</c> → entire deficit on slot 0; remaining slots 0.</item>
        ///   <item>Else: slots <c>0..N-2</c> get <c>ceil(D/N)</c>; the last slot absorbs the remainder.</item>
        /// </list>
        /// Sum of the returned shares equals <paramref name="deficit"/> exactly when
        /// <paramref name="deficit"/> &gt; 0 and <paramref name="capableCount"/> &gt; 0.</summary>
        /// <param name="deficit">Total amount to spread.</param>
        /// <param name="capableCount">Number of assemblers that can build this item.</param>
        /// <param name="minBatch">Below this deficit, do not split — entire amount on slot 0.</param>
        /// <returns>Per-slot share array of length <paramref name="capableCount"/> (or empty).</returns>
        internal static int[] Autocraft_SplitDeficit(long deficit, int capableCount, int minBatch) {
            if (capableCount <= 0) return new int[0];
            int[] shares = new int[capableCount];
            if (deficit <= 0) return shares;
            if (deficit < minBatch) {
                shares[0] = (int)deficit;
                return shares;
            }
            long perSlot = (deficit + capableCount - 1) / capableCount;
            long sumPrefix = 0;
            for (int i = 0; i < capableCount - 1; i++) {
                shares[i] = (int)perSlot;
                sumPrefix += perSlot;
            }
            shares[capableCount - 1] = (int)(deficit - sumPrefix);
            return shares;
        }

        /// <summary>Picks the index of the assembler best suited to hold the assemble-side
        /// reservation: the one whose entityId appears in the most capability sets across the
        /// current pending assemble work. Ties resolve to the lowest entityId. Returns
        /// <c>-1</c> when reservation is meaningless (pool ≤ 1, empty work-set, or no
        /// assembler can build any pending item).</summary>
        /// <param name="assemblerIds">EntityIds of the assembler pool (expected sorted ascending).</param>
        /// <param name="capableAssemblersPerAssembleKey">Per-assemble-key set of capable entityIds.</param>
        /// <returns>Index into <paramref name="assemblerIds"/>, or <c>-1</c>.</returns>
        internal static int Autocraft_PickReservedAssemblerIndex(
            IList<long> assemblerIds,
            IList<HashSet<long>> capableAssemblersPerAssembleKey) {
            if (assemblerIds == null || assemblerIds.Count <= 1) return -1;
            if (capableAssemblersPerAssembleKey == null || capableAssemblersPerAssembleKey.Count == 0) return -1;
            int bestIdx = -1;
            int bestCount = -1;
            long bestEntity = long.MaxValue;
            for (int i = 0; i < assemblerIds.Count; i++) {
                long entity = assemblerIds[i];
                int count = 0;
                for (int k = 0; k < capableAssemblersPerAssembleKey.Count; k++) {
                    HashSet<long> set = capableAssemblersPerAssembleKey[k];
                    if (set != null && set.Contains(entity)) count++;
                }
                if (count > bestCount || (count == bestCount && entity < bestEntity)) {
                    bestCount = count;
                    bestIdx = i;
                    bestEntity = entity;
                }
            }
            if (bestCount <= 0) return -1;
            return bestIdx;
        }

        /// <summary>Wraps <see cref="Autocraft_ComputePoolSplit"/> with assemble-side reservation
        /// semantics. When <paramref name="reserveEnabled"/> is true, the pool size is &gt; 1,
        /// and both sides have work, one assembler is held back from the proportional split for
        /// the assemble side (caller picks which one). Otherwise this delegates to the unreserved
        /// split unchanged.</summary>
        /// <param name="poolSize">Total assemblers in the pool.</param>
        /// <param name="assemblePending">Count of items needing assembly.</param>
        /// <param name="disassemblePending">Count of items needing disassembly.</param>
        /// <param name="reserveEnabled">Reservation feature toggle from the caller.</param>
        /// <param name="assembleCount">Number of assemblers assigned to assemble side (includes the reserved slot when active).</param>
        /// <param name="disassembleCount">Number of assemblers assigned to disassemble side.</param>
        /// <param name="reservedActive">True when the reservation is in effect this tick.</param>
        internal static void Autocraft_ComputePoolSplitWithReservation(
            int poolSize,
            int assemblePending,
            int disassemblePending,
            bool reserveEnabled,
            out int assembleCount,
            out int disassembleCount,
            out bool reservedActive) {
            reservedActive = false;
            bool bothSidesHaveWork = assemblePending > 0 && disassemblePending > 0;
            bool activate = reserveEnabled && poolSize > 1 && bothSidesHaveWork;
            if (!activate) {
                Autocraft_ComputePoolSplit(poolSize, assemblePending, disassemblePending,
                    out assembleCount, out disassembleCount);
                return;
            }
            int reducedAssemble, reducedDisassemble;
            Autocraft_ComputePoolSplit(poolSize - 1, assemblePending, disassemblePending,
                out reducedAssemble, out reducedDisassemble);
            assembleCount = reducedAssemble + 1;
            disassembleCount = reducedDisassemble;
            reservedActive = true;
        }

        /// <summary>Single decision point for mode-lock: an assembler can flip when either the
        /// desired mode equals its current mode (no change needed) or its queue is empty
        /// (no in-flight work to wipe).</summary>
        /// <param name="queueEmpty">Whether the assembler's queue is currently empty.</param>
        /// <param name="current">Current assembler mode.</param>
        /// <param name="desired">Desired assembler mode for this tick.</param>
        internal static bool Autocraft_CanFlipMode(bool queueEmpty, MyAssemblerMode current, MyAssemblerMode desired) {
            return current == desired || queueEmpty;
        }

        /// <summary>Computes how many additional units to enqueue on a single assembler for
        /// one item: <c>min(share, maxDepth) - alreadyQueuedOnThisAsm</c>, clamped to <c>≥ 0</c>.</summary>
        /// <param name="share">Per-assembler share allocated by <see cref="Autocraft_SplitDeficit"/>.</param>
        /// <param name="alreadyQueuedOnThisAsm">Whole-unit pending amount already on this assembler for this blueprint.</param>
        /// <param name="maxDepth">Per-item queue depth ceiling.</param>
        internal static long Autocraft_PerAssemblerTopUp(long share, long alreadyQueuedOnThisAsm, int maxDepth) {
            long cap = Math.Min(share, (long)maxDepth);
            long topUp = cap - alreadyQueuedOnThisAsm;
            if (topUp < 0) topUp = 0;
            return topUp;
        }

        /// <summary>Surplus amount to trim when a per-assembler share shrunk between ticks and
        /// the queue currently holds more than the new share allows. Returns
        /// <c>max(0, alreadyQueuedOnThisAsm - share)</c>.</summary>
        /// <param name="share">Per-assembler share allocated this tick.</param>
        /// <param name="alreadyQueuedOnThisAsm">Whole-unit pending amount already on this assembler.</param>
        internal static long Autocraft_ComputeShareSurplus(long share, long alreadyQueuedOnThisAsm) {
            long surplus = alreadyQueuedOnThisAsm - share;
            return surplus > 0 ? surplus : 0;
        }

        /// <summary>Hysteresis check for reservation: returns <c>true</c> when the currently
        /// reserved assembler is still capable of building at least one item in the current
        /// pending assemble work-set. Releases the reservation slot only when that ceases.</summary>
        /// <param name="currentReservedId">EntityId of the current reservation holder (0 = none).</param>
        /// <param name="capableSetsForCurrentWorkSet">Per-assemble-key capable entityId sets.</param>
        internal static bool Autocraft_ReservationStillValid(
            long currentReservedId,
            IList<HashSet<long>> capableSetsForCurrentWorkSet) {
            if (currentReservedId == 0) return false;
            if (capableSetsForCurrentWorkSet == null || capableSetsForCurrentWorkSet.Count == 0) return false;
            for (int i = 0; i < capableSetsForCurrentWorkSet.Count; i++) {
                HashSet<long> set = capableSetsForCurrentWorkSet[i];
                if (set != null && set.Contains(currentReservedId)) return true;
            }
            return false;
        }

        /// <summary>Renders one status row for the <c>[GCraft]</c> LCD using fixed-width columns.</summary>
        /// <param name="sb">Buffer to append to.</param>
        /// <param name="itemSubtype">Display name (typically the item subtype).</param>
        /// <param name="target">Quota target column.</param>
        /// <param name="actual">Current grid total.</param>
        /// <param name="status">Human-readable status text.</param>
        internal static void Autocraft_AppendStatusRow(
            StringBuilder sb, string itemSubtype, string target, long actual, string status) {
            if (sb == null) return;
            string item = itemSubtype ?? string.Empty;
            string tgt = target ?? string.Empty;
            string actStr = actual.ToString();
            string st = status ?? string.Empty;
            Autocraft_AppendPadded(sb, item, 20);
            Autocraft_AppendPadded(sb, tgt, 9);
            Autocraft_AppendPadded(sb, actStr, 9);
            sb.Append(st);
            sb.Append('\n');
        }

        /// <summary>Appends <paramref name="value"/> to <paramref name="sb"/> padded with spaces to
        /// <paramref name="width"/>. If <paramref name="value"/> already exceeds the width it is
        /// emitted as-is followed by one separator space.</summary>
        static void Autocraft_AppendPadded(StringBuilder sb, string value, int width) {
            sb.Append(value);
            int pad = width - value.Length;
            if (pad <= 0) {
                sb.Append(' ');
                return;
            }
            for (int i = 0; i < pad; i++) sb.Append(' ');
        }

        /// <summary>Renders the value column for the <c>TARGET</c> field — <c>10000L</c> for limiters,
        /// plain integer for minimums.</summary>
        internal static string Autocraft_FormatTargetColumn(AutocraftQuota quota) {
            if (quota == null) return "-";
            if (quota.Mode == AutocraftMode.Exact) return quota.Amount + "E";
            return quota.Amount.ToString();
        }

        /// <summary>Returns the human-readable status text for the <c>STATUS</c> column.</summary>
        internal static string Autocraft_FormatStatusColumn(AutocraftStatus status, long queued) {
            switch (status) {
                case AutocraftStatus.OK: return "OK";
                case AutocraftStatus.Crafting:
                    return queued > 0 ? "Crafting (" + queued + " queued)" : "Crafting";
                case AutocraftStatus.Disassembling:
                    return queued > 0 ? "Disassembling (" + queued + " queued)" : "Disassembling";
                case AutocraftStatus.BlockedNeedsLearn: return "Blocked: NeedsLearn";
                case AutocraftStatus.BlockedNoAssembler: return "Blocked: NoAssembler";
                case AutocraftStatus.BlockedNoCapableAsm: return "Blocked: NoCapableAsm";
                case AutocraftStatus.Disabled: return "Disabled";
                default: return "?";
            }
        }

        /// <summary>Discovers the <c>[GCraft]</c> LCD by scanning all in-scope text panels for the
        /// <see cref="AutocraftLcdTag"/> in their <c>CustomName</c>. First match wins; subsequent
        /// duplicates are reported once via <see cref="LogWarningOnce"/>.</summary>
        IEnumerator<YieldReason> StepDiscoverGCraftLCD() {
            if (!_config.EnableAutocraft) {
                _gcraftLcd = null;
                yield return YieldReason.ChunkBoundary;
                yield break;
            }
            if (_gcraftLcd != null && ValidateBlock(_gcraftLcd) && Autocraft_HasGCraftTag(_gcraftLcd.CustomName)) {
                yield return YieldReason.ChunkBoundary;
                yield break;
            }
            _gcraftLcd = null;
            List<IMyTextPanel> panels = new List<IMyTextPanel>();
            GridTerminalSystem.GetBlocksOfType(panels, p =>
                p != null && !p.Closed && p.CubeGrid != null
                && _scopeGrids.Contains(p.CubeGrid.EntityId)
                && Autocraft_HasGCraftTag(p.CustomName));
            for (int i = 0; i < panels.Count; i++) {
                if (i == 0) {
                    _gcraftLcd = panels[0];
                    continue;
                }
                LogWarningOnce("autocraft:duplicate-lcd:" + panels[i].EntityId,
                    "[Goose] Multiple [GCraft] LCDs found; using '" + _gcraftLcd.CustomName + "', ignoring '" + panels[i].CustomName + "'");
            }
            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>Returns true when <paramref name="name"/> carries the <see cref="AutocraftLcdTag"/>
        /// marker. Case-sensitive (matches the convention used elsewhere).</summary>
        static bool Autocraft_HasGCraftTag(string name) {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf(AutocraftLcdTag, StringComparison.Ordinal) >= 0;
        }

        /// <summary>Parses the <c>[GCraft]</c> LCD's CustomData on change, refreshing
        /// <see cref="_autocraftTargets"/>, <see cref="_autocraftActiveQuotaLines"/>, and the
        /// user-typed portion of <see cref="_blueprintMap"/>. Idempotent; no-ops when nothing
        /// changed.</summary>
        IEnumerator<YieldReason> StepUpdateAutocraftCfg() {
            if (!_config.EnableAutocraft || _gcraftLcd == null) {
                yield return YieldReason.ChunkBoundary;
                yield break;
            }
            string current = _gcraftLcd.CustomData ?? string.Empty;
            if (string.Equals(current, _gcraftLastSeenCustomData, StringComparison.Ordinal)
                && _gcraftLastCatalogVersion == _catalogVersion) {
                yield return YieldReason.ChunkBoundary;
                yield break;
            }

            Autocraft_ParseGooseQuotaSection(current, _autocraftActiveQuotaLines);
            _autocraftTargets.Clear();
            foreach (var kv in _autocraftActiveQuotaLines) {
                int eq = kv.Value.IndexOf('=');
                if (eq <= 0 || eq >= kv.Value.Length - 1) continue;
                string raw = kv.Value.Substring(eq + 1).Trim();
                long amount;
                AutocraftMode mode;
                bool ignore;
                if (!Autocraft_TryParseQuotaValue(raw, out amount, out mode, out ignore)) {
                    LogWarningOnce("autocraft:bad-quota:" + kv.Key,
                        "[Goose] Autocraft quota '" + kv.Key + "=" + raw + "' is malformed; expected <count>, <count>E, or x. Skipped.");
                    continue;
                }
                if (ignore) continue;
                _autocraftTargets[kv.Key] = new AutocraftQuota { Amount = amount, Mode = mode };
            }

            Autocraft_LoadBlueprintMapFromCustomData(current, _autocraftBlueprintParseScratch);

            _gcraftLastSeenCustomData = current;
            _gcraftLastCatalogVersion = _catalogVersion;
            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>Runs one pass of the autocraft engine: diff totals vs targets, allocate the
        /// assembler pool across assemble/disassemble work, reconcile per-assembler queues, and
        /// observe queued blueprints for passive learning. Idempotent — a partial pass is safe to
        /// resume next tick.</summary>
        IEnumerator<YieldReason> StepRunAutocraftEngine() {
            _autocraftItemStatus.Clear();
            _autocraftQueuedAmount.Clear();
            if (!_config.EnableAutocraft) {
                foreach (string key in _autocraftTargets.Keys) {
                    _autocraftItemStatus[key] = AutocraftStatus.Disabled;
                }
                yield return YieldReason.ChunkBoundary;
                yield break;
            }
            Autocraft_RefreshAssemblerPool();
            if (_assemblers.Count == 0) {
                foreach (string key in _autocraftTargets.Keys) {
                    _autocraftItemStatus[key] = AutocraftStatus.BlockedNoAssembler;
                }
                yield return YieldReason.ChunkBoundary;
                yield break;
            }
            if (BudgetExceeded()) yield return YieldReason.BudgetHit;

            Autocraft_ObserveAssemblerQueuesForLearning();
            yield return YieldReason.ChunkBoundary;
            if (BudgetExceeded()) yield return YieldReason.BudgetHit;

            Autocraft_BuildTotalsByKey(_autocraftTotalsScratch);
            _autocraftAssembleKeys.Clear();
            _autocraftDisassembleKeys.Clear();
            foreach (var kv in _autocraftTargets) {
                string itemKey = kv.Key;
                AutocraftQuota quota = kv.Value;
                long actual;
                _autocraftTotalsScratch.TryGetValue(itemKey, out actual);

                if (quota.Mode == AutocraftMode.Minimum) {
                    long deficit = quota.Amount - actual;
                    if (deficit > 0) _autocraftAssembleKeys.Add(itemKey);
                    else _autocraftItemStatus[itemKey] = AutocraftStatus.OK;
                } else {
                    if (actual < quota.Amount) _autocraftAssembleKeys.Add(itemKey);
                    else if (actual > quota.Amount) _autocraftDisassembleKeys.Add(itemKey);
                    else _autocraftItemStatus[itemKey] = AutocraftStatus.OK;
                }
            }
            _autocraftAssembleKeys.Sort(StringComparer.Ordinal);
            _autocraftDisassembleKeys.Sort(StringComparer.Ordinal);
            yield return YieldReason.ChunkBoundary;
            if (BudgetExceeded()) yield return YieldReason.BudgetHit;

            Autocraft_FilterByResolution(_autocraftAssembleKeys);
            Autocraft_FilterByResolution(_autocraftDisassembleKeys);
            yield return YieldReason.ChunkBoundary;
            if (BudgetExceeded()) yield return YieldReason.BudgetHit;

            Dictionary<string, HashSet<long>> capableByKey =
                new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
            List<HashSet<long>> capableSetsList = new List<HashSet<long>>(_autocraftAssembleKeys.Count);
            for (int k = 0; k < _autocraftAssembleKeys.Count; k++) {
                string itemKey = _autocraftAssembleKeys[k];
                MyDefinitionId bpId;
                if (!_blueprintMap.TryGetValue(itemKey, out bpId)) continue;
                HashSet<long> set = new HashSet<long>();
                Autocraft_GetCapableAssemblerIds(bpId, _assemblers, set);
                capableByKey[itemKey] = set;
                capableSetsList.Add(set);
            }
            yield return YieldReason.ChunkBoundary;
            if (BudgetExceeded()) yield return YieldReason.BudgetHit;

            bool reserveEnabled = _assemblers.Count > 1
                && _autocraftDisassembleKeys.Count > 0
                && _autocraftAssembleKeys.Count > 0;
            int assembleCount, disassembleCount;
            bool reservedActive;
            Autocraft_ComputePoolSplitWithReservation(_assemblers.Count,
                _autocraftAssembleKeys.Count, _autocraftDisassembleKeys.Count,
                reserveEnabled,
                out assembleCount, out disassembleCount, out reservedActive);

            if (reservedActive) {
                bool keep = _autocraftReservedAsmEntityId != 0
                    && Autocraft_ReservationStillValid(_autocraftReservedAsmEntityId, capableSetsList);
                if (keep) {
                    bool foundInPool = false;
                    for (int i = 0; i < _assemblers.Count; i++) {
                        if (_assemblers[i].EntityId == _autocraftReservedAsmEntityId) { foundInPool = true; break; }
                    }
                    keep = foundInPool;
                }
                if (!keep) {
                    List<long> entityIds = new List<long>(_assemblers.Count);
                    for (int i = 0; i < _assemblers.Count; i++) entityIds.Add(_assemblers[i].EntityId);
                    int pickedIdx = Autocraft_PickReservedAssemblerIndex(entityIds, capableSetsList);
                    _autocraftReservedAsmEntityId = pickedIdx >= 0 ? _assemblers[pickedIdx].EntityId : 0;
                }
                if (_autocraftReservedAsmEntityId == 0) {
                    reservedActive = false;
                    Autocraft_ComputePoolSplitWithReservation(_assemblers.Count,
                        _autocraftAssembleKeys.Count, _autocraftDisassembleKeys.Count,
                        false,
                        out assembleCount, out disassembleCount, out reservedActive);
                }
            } else {
                _autocraftReservedAsmEntityId = 0;
            }

            MyAssemblerMode[] desiredMode = new MyAssemblerMode[_assemblers.Count];
            int reservedIdx = -1;
            if (reservedActive && _autocraftReservedAsmEntityId != 0) {
                for (int i = 0; i < _assemblers.Count; i++) {
                    if (_assemblers[i].EntityId == _autocraftReservedAsmEntityId) { reservedIdx = i; break; }
                }
            }
            int nonReservedAssembleSlots = reservedActive ? assembleCount - 1 : assembleCount;
            int placed = 0;
            for (int i = 0; i < _assemblers.Count; i++) {
                if (i == reservedIdx) continue;
                if (placed < nonReservedAssembleSlots) {
                    desiredMode[i] = MyAssemblerMode.Assembly;
                    placed++;
                } else {
                    desiredMode[i] = MyAssemblerMode.Disassembly;
                }
            }
            if (reservedIdx >= 0) desiredMode[reservedIdx] = MyAssemblerMode.Assembly;

            if (_assemblers.Count == 2 && reservedActive && _autocraftAssembleKeys.Count == 0 && reservedIdx >= 0) {
                desiredMode[reservedIdx] = MyAssemblerMode.Disassembly;
            }

            _modeLockedThisTick.Clear();
            for (int i = 0; i < _assemblers.Count; i++) {
                IMyAssembler a = _assemblers[i];
                if (a == null) continue;
                MyAssemblerMode desired = desiredMode[i];
                if (Autocraft_CanFlipMode(a.IsQueueEmpty, a.Mode, desired)) {
                    if (a.Mode != desired) a.Mode = desired;
                } else {
                    _modeLockedThisTick.Add(a.EntityId);
                }
                try { a.CooperativeMode = false; } catch (Exception) { /* modded blocks may reject */ }
            }
            yield return YieldReason.ChunkBoundary;
            if (BudgetExceeded()) yield return YieldReason.BudgetHit;

            int maxDepth = Math.Min(AutocraftMaxQueueDepthCeiling, Math.Max(1, _config.AutocraftMaxQueueDepth));

            List<IMyAssembler> assembleSlice = new List<IMyAssembler>(_assemblers.Count);
            List<IMyAssembler> disassembleSlice = new List<IMyAssembler>(_assemblers.Count);
            for (int i = 0; i < _assemblers.Count; i++) {
                IMyAssembler a = _assemblers[i];
                if (a == null) continue;
                if (_modeLockedThisTick.Contains(a.EntityId)) continue;
                if (a.Mode == MyAssemblerMode.Assembly) assembleSlice.Add(a);
                else disassembleSlice.Add(a);
            }

            Autocraft_DistributeAndReconcile(
                assembleSlice, _autocraftAssembleKeys, capableByKey,
                MyAssemblerMode.Assembly, maxDepth);
            yield return YieldReason.ChunkBoundary;
            if (BudgetExceeded()) yield return YieldReason.BudgetHit;

            Autocraft_DistributeAndReconcile(
                disassembleSlice, _autocraftDisassembleKeys, null,
                MyAssemblerMode.Disassembly, maxDepth);
            yield return YieldReason.ChunkBoundary;
            if (BudgetExceeded()) yield return YieldReason.BudgetHit;

            Autocraft_FeedAssemblers();
            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>Refreshes <see cref="_assemblers"/> by filtering <see cref="_productionBlocks"/>
        /// down to <see cref="IMyAssembler"/> instances that are still valid this tick.</summary>
        void Autocraft_RefreshAssemblerPool() {
            _assemblers.Clear();
            for (int i = 0; i < _productionBlocks.Count; i++) {
                IMyAssembler a = _productionBlocks[i] as IMyAssembler;
                if (a != null && ValidateBlock(a)) _assemblers.Add(a);
            }
            _assemblers.Sort(Autocraft_CompareAssemblersByEntityId);

            bool divergent = _assemblers.Count != _capabilityCacheShadow.Count;
            if (!divergent) {
                for (int i = 0; i < _assemblers.Count; i++) {
                    if (_assemblers[i].EntityId != _capabilityCacheShadow[i]) { divergent = true; break; }
                }
            }
            if (divergent) {
                _capabilityCache.Clear();
                _capabilityCacheShadow.Clear();
                for (int i = 0; i < _assemblers.Count; i++) _capabilityCacheShadow.Add(_assemblers[i].EntityId);
            }

            if (_autocraftReservedAsmEntityId != 0) {
                bool stillPresent = false;
                for (int i = 0; i < _assemblers.Count; i++) {
                    if (_assemblers[i].EntityId == _autocraftReservedAsmEntityId) { stillPresent = true; break; }
                }
                if (!stillPresent) _autocraftReservedAsmEntityId = 0;
            }
        }


        /// <summary>Lazy per-assembler capability probe with caching. On cache miss, calls
        /// <see cref="IMyAssembler.CanUseBlueprint"/> and stores the result in
        /// <see cref="_capabilityCache"/>. Modded assemblers that throw on unknown definitions
        /// are recorded as <c>false</c>.</summary>
        /// <param name="entityId">EntityId of the assembler (used as cache key).</param>
        /// <param name="bpId">Blueprint definition being probed.</param>
        /// <param name="asm">The assembler block (must match <paramref name="entityId"/>).</param>
        bool Autocraft_AsmCanUse(long entityId, MyDefinitionId bpId, IMyAssembler asm) {
            Dictionary<MyDefinitionId, bool> perBp;
            if (!_capabilityCache.TryGetValue(entityId, out perBp)) {
                perBp = new Dictionary<MyDefinitionId, bool>();
                _capabilityCache[entityId] = perBp;
            }
            bool cached;
            if (perBp.TryGetValue(bpId, out cached)) return cached;
            bool result = false;
            if (asm != null) {
                try { result = asm.CanUseBlueprint(bpId); }
                catch (Exception) { result = false; }
            }
            perBp[bpId] = result;
            return result;
        }

        /// <summary>Populates <paramref name="outIds"/> with the entityIds of every assembler in
        /// <paramref name="pool"/> that can build <paramref name="bpId"/>. Sibling to
        /// <see cref="Autocraft_AnyAssemblerCanUse"/> — returns the full capable set rather than
        /// a boolean. Caller owns the output buffer.</summary>
        /// <param name="bpId">Blueprint definition being probed.</param>
        /// <param name="pool">Assembler pool to walk.</param>
        /// <param name="outIds">Output set, cleared before population.</param>
        void Autocraft_GetCapableAssemblerIds(MyDefinitionId bpId, List<IMyAssembler> pool, HashSet<long> outIds) {
            outIds.Clear();
            if (pool == null) return;
            for (int i = 0; i < pool.Count; i++) {
                IMyAssembler asm = pool[i];
                if (asm == null) continue;
                if (Autocraft_AsmCanUse(asm.EntityId, bpId, asm)) outIds.Add(asm.EntityId);
            }
        }

        /// <summary>Removes up to <paramref name="surplus"/> whole units of a given blueprint from
        /// an assembler's queue, walking back-to-front. Used by
        /// <see cref="Autocraft_ReconcileAssembler"/> when an assembler's per-item share shrinks
        /// between ticks (e.g. the reservation moves) and the queue currently overshoots the new
        /// share. Fractional in-progress entries (<c>amount &lt; 1</c>) are skipped to avoid
        /// cancelling an active build.</summary>
        /// <param name="asm">Assembler to trim.</param>
        /// <param name="bpId">Blueprint definition to match against queue entries.</param>
        /// <param name="surplus">Whole-unit surplus to remove (no-op when <c>≤ 0</c>).</param>
        void Autocraft_TrimAssemblerQueueItem(IMyAssembler asm, MyDefinitionId bpId, long surplus) {
            if (surplus <= 0 || asm == null) return;
            _autocraftQueueScratch.Clear();
            asm.GetQueue(_autocraftQueueScratch);
            string targetSubtype = bpId.SubtypeName ?? string.Empty;
            for (int q = _autocraftQueueScratch.Count - 1; q >= 0 && surplus > 0; q--) {
                MyProductionItem prod = _autocraftQueueScratch[q];
                string sub = prod.BlueprintId.SubtypeName ?? string.Empty;
                if (sub != targetSubtype) continue;
                decimal entryAmt = (decimal)prod.Amount;
                long entryWhole = (long)entryAmt;
                if (entryWhole <= 0) continue;
                long removeWhole = entryWhole < surplus ? entryWhole : surplus;
                asm.RemoveQueueItem(q, (MyFixedPoint)((decimal)removeWhole));
                surplus -= removeWhole;
            }
        }

        /// <summary>Stable per-tick assembler ordering used by the round-robin distributor. Sorting
        /// by <see cref="IMyTerminalBlock.EntityId"/> avoids churn when block names change and keeps
        /// queue assignments idempotent across ticks.</summary>
        static int Autocraft_CompareAssemblersByEntityId(IMyAssembler a, IMyAssembler b) {
            long ax = a == null ? long.MinValue : a.EntityId;
            long bx = b == null ? long.MinValue : b.EntityId;
            if (ax < bx) return -1;
            if (ax > bx) return 1;
            return 0;
        }

        /// <summary>Snapshots <see cref="_itemTotals"/> into a catalog-key dictionary for
        /// quota-engine consumption.</summary>
        void Autocraft_BuildTotalsByKey(Dictionary<string, long> output) {
            output.Clear();
            foreach (var kv in _itemTotals) {
                output[Catalog_BuildKey(kv.Key)] = kv.Value;
            }
        }

        /// <summary>Removes items from <paramref name="keys"/> whose blueprint cannot be resolved.
        /// Side effects: sets <see cref="AutocraftStatus.BlockedNeedsLearn"/> for the dropped keys
        /// and accumulates a one-shot warning per failure.</summary>
        void Autocraft_FilterByResolution(List<string> keys) {
            int writeIdx = 0;
            for (int i = 0; i < keys.Count; i++) {
                string key = keys[i];
                MyDefinitionId id;
                if (Autocraft_TryResolve(key, _assemblers, out id)) {
                    keys[writeIdx++] = key;
                    continue;
                }
                _autocraftItemStatus[key] = AutocraftStatus.BlockedNeedsLearn;
                LogWarningOnce("autocraft:needslearn:" + key,
                    "[Goose] Autocraft cannot resolve blueprint for '" + key + "'. Queue one of the item on any assembler to learn.");
            }
            if (writeIdx < keys.Count) keys.RemoveRange(writeIdx, keys.Count - writeIdx);
        }

        /// <summary>Walks every assembler's current queue and records any blueprint whose result
        /// matches an item key we know about, populating <see cref="_blueprintMap"/> entries that
        /// were previously <c>NeedsLearn</c>. Uses the produced item's subtype heuristic: a queued
        /// blueprint definition whose subtype matches the item subtype (with the curated map's
        /// component-suffix awareness) is recorded as the mapping.</summary>
        void Autocraft_ObserveAssemblerQueuesForLearning() {
            if (_blueprintMapNeedsLearn.Count == 0) return;
            for (int a = 0; a < _assemblers.Count; a++) {
                IMyAssembler asm = _assemblers[a];
                if (asm == null) continue;
                _autocraftQueueScratch.Clear();
                asm.GetQueue(_autocraftQueueScratch);
                for (int q = 0; q < _autocraftQueueScratch.Count; q++) {
                    MyProductionItem prod = _autocraftQueueScratch[q];
                    string bpSubtype = prod.BlueprintId.SubtypeName;
                    if (string.IsNullOrEmpty(bpSubtype)) continue;
                    foreach (string needsKey in _blueprintMapNeedsLearn) {
                        string itemSubtype = Autocraft_SubtypeFromKey(needsKey);
                        if (itemSubtype.Length == 0) continue;
                        if (bpSubtype.Equals(itemSubtype, StringComparison.Ordinal)
                            || bpSubtype.Equals(itemSubtype + "Component", StringComparison.Ordinal)) {
                            _blueprintMap[needsKey] = prod.BlueprintId;
                            LogActionOnce("autocraft:learned:" + needsKey,
                                "Autocraft learned '" + needsKey + "' = " + bpSubtype);
                        }
                    }
                }
            }
            List<string> learned = new List<string>();
            foreach (string k in _blueprintMapNeedsLearn) {
                if (_blueprintMap.ContainsKey(k)) learned.Add(k);
            }
            for (int i = 0; i < learned.Count; i++) _blueprintMapNeedsLearn.Remove(learned[i]);
        }

        /// <summary>Round-robin distributes the keys across the assemblers in slice
        /// <c>[<paramref name="startIdx"/>, startIdx+<paramref name="count"/>)</c> and reconciles
        /// each assembler's queue toward the per-item demand. Tracks the per-item queued amount
        /// for the status LCD.</summary>
        void Autocraft_DistributeAndReconcile(
            IList<IMyAssembler> slice,
            IList<string> keys,
            Dictionary<string, HashSet<long>> capableByKey,
            MyAssemblerMode mode,
            int maxDepth) {
            Dictionary<long, List<KeyValuePair<string, long>>> perAsm =
                new Dictionary<long, List<KeyValuePair<string, long>>>();
            if (slice != null) {
                for (int i = 0; i < slice.Count; i++) {
                    IMyAssembler a = slice[i];
                    if (a != null) perAsm[a.EntityId] = new List<KeyValuePair<string, long>>();
                }
            }

            if (keys != null && slice != null && slice.Count > 0) {
                for (int k = 0; k < keys.Count; k++) {
                    string itemKey = keys[k];
                    AutocraftQuota quota;
                    if (!_autocraftTargets.TryGetValue(itemKey, out quota)) continue;
                    long actual;
                    _autocraftTotalsScratch.TryGetValue(itemKey, out actual);
                    long demand = mode == MyAssemblerMode.Assembly
                        ? Math.Max(0, quota.Amount - actual)
                        : Math.Max(0, actual - quota.Amount);
                    if (demand <= 0) continue;

                    HashSet<long> capableSet = null;
                    bool capabilityFiltered = capableByKey != null;
                    if (capabilityFiltered) capableByKey.TryGetValue(itemKey, out capableSet);

                    List<IMyAssembler> capableInSlice = new List<IMyAssembler>(slice.Count);
                    for (int i = 0; i < slice.Count; i++) {
                        IMyAssembler a = slice[i];
                        if (a == null) continue;
                        if (!capabilityFiltered || (capableSet != null && capableSet.Contains(a.EntityId))) {
                            capableInSlice.Add(a);
                        }
                    }

                    if (capableInSlice.Count == 0) {
                        if (capabilityFiltered) {
                            _autocraftItemStatus[itemKey] = AutocraftStatus.BlockedNoCapableAsm;
                        }
                        continue;
                    }

                    int[] shares = Autocraft_SplitDeficit(demand, capableInSlice.Count, AutocraftSplitMinBatch);
                    bool anyAssigned = false;
                    for (int s = 0; s < shares.Length; s++) {
                        if (shares[s] <= 0) continue;
                        IMyAssembler target = capableInSlice[s];
                        perAsm[target.EntityId].Add(new KeyValuePair<string, long>(itemKey, shares[s]));
                        anyAssigned = true;
                    }
                    if (anyAssigned) {
                        _autocraftItemStatus[itemKey] = mode == MyAssemblerMode.Assembly
                            ? AutocraftStatus.Crafting
                            : AutocraftStatus.Disassembling;
                    }
                }
            }

            if (slice != null) {
                for (int i = 0; i < slice.Count; i++) {
                    IMyAssembler asm = slice[i];
                    if (asm == null) continue;
                    List<KeyValuePair<string, long>> assignments;
                    if (!perAsm.TryGetValue(asm.EntityId, out assignments)) {
                        assignments = new List<KeyValuePair<string, long>>();
                    }
                    Autocraft_ReconcileAssembler(asm, assignments, mode, maxDepth);
                }
            }
        }

        /// <summary>Reconciles a single assembler's queue against its assigned item keys: removes
        /// queue entries that are no longer assigned, and tops up assigned entries to the per-item
        /// demand (clamped at <paramref name="maxDepth"/>).</summary>
        void Autocraft_ReconcileAssembler(
            IMyAssembler asm,
            IList<KeyValuePair<string, long>> assignedShares,
            MyAssemblerMode mode,
            int maxDepth) {
            HashSet<string> assignedSubtypes = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < assignedShares.Count; i++) {
                MyDefinitionId mapped;
                if (_blueprintMap.TryGetValue(assignedShares[i].Key, out mapped)) {
                    assignedSubtypes.Add(mapped.SubtypeName ?? string.Empty);
                }
            }

            _autocraftQueueScratch.Clear();
            asm.GetQueue(_autocraftQueueScratch);
            for (int q = _autocraftQueueScratch.Count - 1; q >= 0; q--) {
                MyProductionItem prod = _autocraftQueueScratch[q];
                string bpSubtype = prod.BlueprintId.SubtypeName ?? string.Empty;
                if (!assignedSubtypes.Contains(bpSubtype)) {
                    asm.RemoveQueueItem(q, prod.Amount);
                }
            }

            _autocraftQueueScratch.Clear();
            asm.GetQueue(_autocraftQueueScratch);
            Dictionary<string, MyFixedPoint> alreadyQueued = new Dictionary<string, MyFixedPoint>(StringComparer.Ordinal);
            for (int q = 0; q < _autocraftQueueScratch.Count; q++) {
                string sub = _autocraftQueueScratch[q].BlueprintId.SubtypeName ?? string.Empty;
                MyFixedPoint amt;
                alreadyQueued.TryGetValue(sub, out amt);
                alreadyQueued[sub] = amt + _autocraftQueueScratch[q].Amount;
            }

            for (int i = 0; i < assignedShares.Count; i++) {
                string itemKey = assignedShares[i].Key;
                long share = assignedShares[i].Value;
                MyDefinitionId blueprintId;
                if (!_blueprintMap.TryGetValue(itemKey, out blueprintId)) continue;
                string sub = blueprintId.SubtypeName ?? string.Empty;
                MyFixedPoint already;
                alreadyQueued.TryGetValue(sub, out already);
                long alreadyWhole = Autocraft_CeilFixedPointToLong(already);

                long topUp = Autocraft_PerAssemblerTopUp(share, alreadyWhole, maxDepth);
                if (topUp > 0) asm.AddQueueItem(blueprintId, (decimal)topUp);

                long surplus = Autocraft_ComputeShareSurplus(share, alreadyWhole);
                if (surplus > 0) Autocraft_TrimAssemblerQueueItem(asm, blueprintId, surplus);

                long contribution = alreadyWhole + topUp - surplus;
                if (contribution < 0) contribution = 0;
                long queuedRunning;
                _autocraftQueuedAmount.TryGetValue(itemKey, out queuedRunning);
                _autocraftQueuedAmount[itemKey] = queuedRunning + contribution;
            }
        }

        /// <summary>Renders the status table to the <c>[GCraft]</c> LCD and updates the LCD's
        /// CustomData if the live-merged template differs from what's there now.</summary>
        IEnumerator<YieldReason> StepRenderGCraftLCD() {
            if (_gcraftLcd == null) {
                yield return YieldReason.ChunkBoundary;
                yield break;
            }

            Autocraft_BuildAutoBlueprintSnapshot(_autocraftAutoBlueprintScratch);
            string desiredCustomData = Autocraft_BuildGCraftCustomData(
                _autocraftAutoBlueprintScratch,
                _autocraftBlueprintParseScratch,
                _autocraftActiveQuotaLines,
                Autocraft_CatalogKeysForTemplate(),
                _gcraftCustomDataSB);
            string currentCustomData = _gcraftLcd.CustomData ?? string.Empty;
            if (!string.Equals(currentCustomData, desiredCustomData, StringComparison.Ordinal)) {
                _gcraftLcd.CustomData = desiredCustomData;
                _gcraftLastSeenCustomData = desiredCustomData;
            }
            yield return YieldReason.ChunkBoundary;
            if (BudgetExceeded()) yield return YieldReason.BudgetHit;

            IMyTextSurface surface = Autocraft_GetStatusSurface(_gcraftLcd);
            if (surface == null) yield break;

            int assembleAssemblers = 0;
            int disassembleAssemblers = 0;
            for (int i = 0; i < _assemblers.Count; i++) {
                IMyAssembler a = _assemblers[i];
                if (a == null) continue;
                if (a.Mode == MyAssemblerMode.Assembly) assembleAssemblers++;
                else disassembleAssemblers++;
            }

            _gcraftStatusSB.Length = 0;
            _gcraftStatusSB.Append("=== Goose Autocraft ===\n");
            _gcraftStatusSB.Append("Pool: ").Append(_assemblers.Count).Append(" assemblers (")
                .Append(assembleAssemblers).Append(" Assemble / ")
                .Append(disassembleAssemblers).Append(" Disassemble)\n\n");
            Autocraft_AppendPadded(_gcraftStatusSB, "ITEM", 20);
            Autocraft_AppendPadded(_gcraftStatusSB, "TARGET", 9);
            Autocraft_AppendPadded(_gcraftStatusSB, "ACTUAL", 9);
            _gcraftStatusSB.Append("STATUS\n");

            List<string> sortedKeys = new List<string>(_autocraftTargets.Keys);
            sortedKeys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < sortedKeys.Count; i++) {
                string key = sortedKeys[i];
                AutocraftQuota quota = _autocraftTargets[key];
                long actual;
                _autocraftTotalsScratch.TryGetValue(key, out actual);
                AutocraftStatus status;
                if (!_autocraftItemStatus.TryGetValue(key, out status)) status = AutocraftStatus.OK;
                long queued;
                _autocraftQueuedAmount.TryGetValue(key, out queued);
                Autocraft_AppendStatusRow(_gcraftStatusSB,
                    Autocraft_SubtypeFromKey(key),
                    Autocraft_FormatTargetColumn(quota),
                    actual,
                    Autocraft_FormatStatusColumn(status, queued));
            }
            surface.WriteText(_gcraftStatusSB.ToString());
        }

        /// <summary>Builds a string snapshot of auto-resolved (non-manual) blueprint entries for the
        /// CustomData regenerator.</summary>
        void Autocraft_BuildAutoBlueprintSnapshot(Dictionary<string, string> output) {
            output.Clear();
            foreach (var kv in _blueprintMap) {
                if (_blueprintMapManual.Contains(kv.Key)) continue;
                output[kv.Key] = AutocraftBlueprintTypePrefix + (kv.Value.SubtypeName ?? string.Empty);
            }
        }

        /// <summary>Returns the catalog keys that should appear as commented hints in the
        /// <c>[Goose]</c> template — components-class items only.</summary>
        IEnumerable<string> Autocraft_CatalogKeysForTemplate() {
            List<string> keys = new List<string>(_knownItems.Count);
            foreach (var kv in _knownItems) {
                ItemCategory cat = Classify(kv.Value);
                if (cat == ItemCategory.Components || cat == ItemCategory.Tools) {
                    keys.Add(kv.Key);
                }
            }
            return keys;
        }

        /// <summary>Resolves the rendering surface for the status table. Multi-surface providers
        /// route to surface 0 for v1; plain <see cref="IMyTextPanel"/> uses itself.</summary>
        /// <summary>Scratch buffer for walking assembler input inventories during feed/drain.</summary>
        List<MyInventoryItem> _autocraftInputItemsScratch = new List<MyInventoryItem>();

        /// <summary>Per-assembler input feeder. For each managed assembler:
        /// <list type="bullet">
        ///   <item>Drains items that don't belong in the current mode (e.g. ingots left over after
        ///   a flip to Disassembly, or components left over after a flip to Assembly).</item>
        ///   <item>If <c>Assembly</c> + non-empty queue: tops every known ingot type up to
        ///   <see cref="GooseConfig.AutocraftAssemblerIngotKeep"/> units. The recipe-data API isn't
        ///   in the PB allowlist, so we stage a small buffer of everything and let the assembler
        ///   consume whatever its current item needs.</item>
        ///   <item>If <c>Disassembly</c> + non-empty queue: tops each queued blueprint's matching
        ///   item type up to the queued amount so the disassembler has the components to break down.</item>
        ///   <item>If queue is empty: drains everything from the input back to category routes so
        ///   ingots aren't stranded after the engine stops requesting work.</item>
        /// </list>
        /// Ingots/components inside their "expected for current mode" set are never pulled out
        /// while the queue is non-empty — once the assembler is staging the right material, the
        /// feeder is a no-op.</summary>
        void Autocraft_FeedAssemblers() {
            int ingotKeep = Math.Max(1, _config.AutocraftAssemblerIngotKeep);
            List<MyItemType> ingotTypes = Autocraft_CollectIngotTypes();

            for (int ai = 0; ai < _assemblers.Count; ai++) {
                IMyAssembler asm = _assemblers[ai];
                if (asm == null || !ValidateBlock(asm)) continue;
                IMyInventory input = asm.InputInventory;
                IMyInventory output = asm.OutputInventory;
                if (input == null || output == null) continue;

                bool assemblyMode = asm.Mode == MyAssemblerMode.Assembly;
                bool queueEmpty = asm.IsQueueEmpty;

                if (queueEmpty) {
                    Autocraft_DrainInventory(input, false, null, null);
                    if (BudgetExceeded()) return;
                    Autocraft_DrainInventory(output, false, null, null);
                    if (BudgetExceeded()) return;
                    continue;
                }

                if (assemblyMode) {
                    // Assembly: input holds ingredient ingots, output holds produced components.
                    // Drain anything non-ingot from input, then top each ingot type up to buffer.
                    Autocraft_DrainInventory(input, true, null, null);
                    if (BudgetExceeded()) return;
                    for (int t = 0; t < ingotTypes.Count; t++) {
                        MyItemType type = ingotTypes[t];
                        long current = GetCurrentAmount(input, type);
                        if (current >= ingotKeep) continue;
                        Autocraft_PullItemIntoInventory(input, type, ingotKeep - current);
                        if (BudgetExceeded()) return;
                    }
                    // Output: drain produced components back to category routes.
                    Autocraft_DrainInventory(output, false, null, null);
                    if (BudgetExceeded()) return;
                } else {
                    // Disassembly: SE consumes from output and produces to input.
                    // Drain ingots that accumulated in input.
                    Autocraft_DrainInventory(input, false, null, null);
                    if (BudgetExceeded()) return;
                    // Output: collect queued blueprint subtypes; drain anything not assigned, then
                    // top up the assigned components to match the queued amount.
                    _autocraftQueueScratch.Clear();
                    asm.GetQueue(_autocraftQueueScratch);
                    HashSet<string> assignedSubtypes = new HashSet<string>(StringComparer.Ordinal);
                    for (int q = 0; q < _autocraftQueueScratch.Count; q++) {
                        string sub = _autocraftQueueScratch[q].BlueprintId.SubtypeName ?? string.Empty;
                        if (sub.Length > 0) assignedSubtypes.Add(sub);
                    }
                    Autocraft_DrainInventory(output, false, assignedSubtypes, null);
                    if (BudgetExceeded()) return;
                    for (int q = 0; q < _autocraftQueueScratch.Count; q++) {
                        MyDefinitionId bp = _autocraftQueueScratch[q].BlueprintId;
                        MyItemType type;
                        if (!Autocraft_TryFindItemTypeForBlueprint(bp, out type)) continue;
                        long want = (long)_autocraftQueueScratch[q].Amount;
                        long current = GetCurrentAmount(output, type);
                        if (current >= want) continue;
                        Autocraft_PullItemIntoInventory(output, type, want - current);
                        if (BudgetExceeded()) return;
                    }
                }
            }
        }

        /// <summary>Returns every item type the catalog classifies as <see cref="ItemCategory.Ingots"/>.
        /// Allocated fresh each tick to keep the per-assembler loop above stable against catalog growth.</summary>
        List<MyItemType> Autocraft_CollectIngotTypes() {
            List<MyItemType> result = new List<MyItemType>();
            foreach (var kv in _knownItems) {
                if (Classify(kv.Value) == ItemCategory.Ingots) result.Add(kv.Value);
            }
            return result;
        }

        /// <summary>Drains items from an assembler input that don't belong for the current mode:
        /// <list type="bullet">
        ///   <item><c>Assembly</c> mode keeps <see cref="ItemCategory.Ingots"/>; everything else is drained.</item>
        ///   <item><c>Disassembly</c> mode keeps <see cref="ItemCategory.Components"/> and
        ///   <see cref="ItemCategory.Tools"/>; everything else is drained.</item>
        ///   <item>Queue empty (any mode): everything is drained.</item>
        /// </list>
        /// Drained items are pushed via category routes; if no route exists they stay put.</summary>
        void Autocraft_DrainInventory(
            IMyInventory inv, bool keepIngots,
            HashSet<string> keepSubtypes,
            object reservedForFutureUse) {
            if (inv == null) return;
            _autocraftInputItemsScratch.Clear();
            inv.GetItems(_autocraftInputItemsScratch);
            for (int i = _autocraftInputItemsScratch.Count - 1; i >= 0; i--) {
                MyInventoryItem item = _autocraftInputItemsScratch[i];
                bool keep = false;
                if (keepIngots && Classify(item.Type) == ItemCategory.Ingots) keep = true;
                if (!keep && keepSubtypes != null && keepSubtypes.Contains(item.Type.SubtypeId ?? string.Empty)) keep = true;
                if (keep) continue;
                Autocraft_DrainSingleItem(inv, item.Type, item.Amount, i);
                if (BudgetExceeded()) return;
            }
        }

        /// <summary>Transfers a single stack out of an assembler input to the first category route
        /// (or general inventory) that can accept it. No-op when nothing is willing/able to receive.</summary>
        void Autocraft_DrainSingleItem(IMyInventory src, MyItemType type, MyFixedPoint amount, int srcIdx) {
            ItemCategory cat = Classify(type);
            List<ContainerEntry> routes;
            if (_containersByCategory.TryGetValue(cat, out routes)) {
                for (int r = 0; r < routes.Count; r++) {
                    ContainerEntry dst = routes[r];
                    if (!ValidateBlock(dst.Block) || dst.Inventory == null) continue;
                    if (dst.Inventory == src) continue;
                    if (!src.CanTransferItemTo(dst.Inventory, type)) continue;
                    if (!dst.Inventory.CanItemsBeAdded(amount, type)) continue;
                    if (src.TransferItemTo(dst.Inventory, srcIdx, null, true, amount)) {
                        if (_config.DebugLogging) LogAction("autocraft-drain " + amount + "x" + type.SubtypeId + " ->" + dst.Block.CustomName);
                        return;
                    }
                }
            }
            for (int b = 0; b < _allInventoryBlocks.Count; b++) {
                IMyTerminalBlock block = _allInventoryBlocks[b];
                if (!ValidateBlock(block)) continue;
                IMyInventory dstInv = GetSortableInventory(block);
                if (dstInv == null || dstInv == src) continue;
                if (!src.CanTransferItemTo(dstInv, type)) continue;
                if (!dstInv.CanItemsBeAdded(amount, type)) continue;
                if (src.TransferItemTo(dstInv, srcIdx, null, true, amount)) return;
            }
        }

        /// <summary>Pulls up to <paramref name="need"/> units of <paramref name="type"/> into
        /// <paramref name="dst"/>, sourcing in priority order:
        /// <list type="number">
        ///   <item>Stock containers with surplus of <paramref name="type"/>.</item>
        ///   <item>Category-routed containers for the type's category.</item>
        ///   <item>Any other in-scope inventory block.</item>
        /// </list>
        /// Returns the amount actually moved (may be less than <paramref name="need"/>).</summary>
        long Autocraft_PullItemIntoInventory(IMyInventory dst, MyItemType type, long need) {
            if (dst == null || need <= 0) return 0;
            long remaining = need;

            for (int i = 0; i < _stockContainers.Count && remaining > 0; i++) {
                ContainerEntry src = _stockContainers[i];
                if (!ValidateBlock(src.Block) || src.Inventory == null) continue;
                if (src.Inventory == dst) continue;
                if (!src.Inventory.CanTransferItemTo(dst, type)) continue;
                remaining -= TryMove(src.Inventory, dst, type, remaining, "autocraft-feed");
                if (BudgetExceeded()) return need - remaining;
            }

            ItemCategory cat = Classify(type);
            List<ContainerEntry> routes;
            if (remaining > 0 && _containersByCategory.TryGetValue(cat, out routes)) {
                for (int i = 0; i < routes.Count && remaining > 0; i++) {
                    ContainerEntry src = routes[i];
                    if (src.IsStock) continue;
                    if (!ValidateBlock(src.Block) || src.Inventory == null) continue;
                    if (src.Inventory == dst) continue;
                    if (!src.Inventory.CanTransferItemTo(dst, type)) continue;
                    remaining -= TryMove(src.Inventory, dst, type, remaining, "autocraft-feed");
                    if (BudgetExceeded()) return need - remaining;
                }
            }

            for (int b = 0; b < _allInventoryBlocks.Count && remaining > 0; b++) {
                IMyTerminalBlock block = _allInventoryBlocks[b];
                if (!ValidateBlock(block)) continue;
                ContainerEntry srcEntry;
                if (_entryByBlock.TryGetValue(block, out srcEntry)) {
                    if (srcEntry.IsStock) continue;
                    if (srcEntry.Categories != null && srcEntry.Categories.Count > 0) continue;
                }
                IMyInventory srcInv = GetSortableInventory(block);
                if (srcInv == null || srcInv == dst) continue;
                if (!srcInv.CanTransferItemTo(dst, type)) continue;
                remaining -= TryMove(srcInv, dst, type, remaining, "autocraft-feed");
                if (BudgetExceeded()) return need - remaining;
            }

            return need - remaining;
        }

        /// <summary>Reverse-lookup: given a queued blueprint, find the catalog
        /// <see cref="MyItemType"/> the engine treats as its output. Walks
        /// <see cref="_blueprintMap"/> for a matching <see cref="MyDefinitionId.SubtypeName"/>
        /// then resolves the item key through <see cref="_knownItems"/>. Returns <c>false</c>
        /// when no mapping exists yet (typically during learning).</summary>
        bool Autocraft_TryFindItemTypeForBlueprint(MyDefinitionId bp, out MyItemType type) {
            type = default(MyItemType);
            string bpSub = bp.SubtypeName ?? string.Empty;
            if (bpSub.Length == 0) return false;
            foreach (var kv in _blueprintMap) {
                if ((kv.Value.SubtypeName ?? string.Empty) == bpSub) {
                    if (_knownItems.TryGetValue(kv.Key, out type)) return true;
                }
            }
            return false;
        }

        IMyTextSurface Autocraft_GetStatusSurface(IMyTerminalBlock block) {
            IMyTextSurfaceProvider provider = block as IMyTextSurfaceProvider;
            if (provider != null && provider.SurfaceCount > 0) {
                IMyTextSurface s = provider.GetSurface(0);
                if (s != null) {
                    try {
                        s.ContentType = ContentType.TEXT_AND_IMAGE;
                        s.Font = "Monospace";
                    } catch (Exception) { }
                    return s;
                }
            }
            return block as IMyTextSurface;
        }
    }
}
