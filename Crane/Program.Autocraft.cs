using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript {
    public partial class Program : MyGridProgram {
        /// <summary>Per-key autocraft target parsed from the <c>[Crane]</c> section of the <c>[CCraft]</c> LCD CustomData.</summary>
        public class AutocraftTarget {
            /// <summary>Catalog key (un-prefixed <c>Type/Subtype</c>).</summary>
            public string Key;
            /// <summary>Resolved item type.</summary>
            public MyItemType ItemType;
            /// <summary>Target amount.</summary>
            public long Target;
            /// <summary>Quota mode.</summary>
            public AutocraftMode Mode;
        }

        /// <summary>Status of one quota line in the rendered <c>[CCraft]</c> surface.</summary>
        public enum AutocraftLineStatus {
            /// <summary>Target met; no queue activity.</summary>
            OK,
            /// <summary>Below target; queue active.</summary>
            Crafting,
            /// <summary>Above Exact target; queue active in Disassembly mode.</summary>
            Disassembling,
            /// <summary>Blueprint unresolved; needs operator to manually queue one to learn.</summary>
            BlockedNeedsLearn,
            /// <summary>No capable assembler in pool can build this item.</summary>
            BlockedNoCapableAsm,
            /// <summary>No assemblers in pool at all.</summary>
            BlockedNoAssembler,
            /// <summary>Autocraft disabled via config.</summary>
            Disabled
        }

        /// <summary>Active autocraft targets (only <c>=N</c>/<c>=NE</c> lines; <c>=x</c> sentinels are excluded).</summary>
        readonly List<AutocraftTarget> _autocraftTargets = new List<AutocraftTarget>();

        /// <summary>Map of catalog key → full active quota line (preserved for round-trip in CustomData merge).</summary>
        readonly Dictionary<string, string> _autocraftActiveQuotaLines = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Snapshot of assembler EntityIds seen in the previous tick. Used to detect pool divergence and clear the capability cache.</summary>
        readonly HashSet<long> _capabilityCacheShadow = new HashSet<long>();

        /// <summary>Capability cache keyed by catalog key, value is the set of assembler EntityIds that successfully probed <see cref="IMyAssembler.CanUseBlueprint"/>.</summary>
        readonly Dictionary<string, HashSet<long>> _capabilityCache = new Dictionary<string, HashSet<long>>();

        /// <summary>EntityId of the assembler currently reserved for Disassembly when both sides have work; 0 when none.</summary>
        long _reservedAsmEntityId;

        /// <summary>Set during <see cref="StepDispatchAndReconcile"/> when an assembler's mode was already flipped this tick (or was non-empty); prevents a re-flip within the same dispatch.</summary>
        readonly HashSet<long> _modeLockedThisTick = new HashSet<long>();

        /// <summary>Reusable per-line status buffer rendered into the <c>[CCraft]</c> surface.</summary>
        readonly StringBuilder _statusBuffer = new StringBuilder(2048);

        /// <summary>Per-quota status, populated during dispatch and consumed by render.</summary>
        readonly Dictionary<string, AutocraftLineStatus> _quotaStatus = new Dictionary<string, AutocraftLineStatus>(StringComparer.Ordinal);

        /// <summary>Per-quota queued-amount sum across capable assemblers; populated during dispatch.</summary>
        readonly Dictionary<string, long> _quotaQueued = new Dictionary<string, long>(StringComparer.Ordinal);

        /// <summary>Per-quota active-assembler count; populated during dispatch.</summary>
        readonly Dictionary<string, int> _quotaAsmCount = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Currently active mode for each assembler (Assemble or Disassemble), keyed by EntityId.</summary>
        readonly Dictionary<long, MyAssemblerMode> _asmMode = new Dictionary<long, MyAssemblerMode>();

        /// <summary>Parses the <c>[Crane]</c> section of the <c>[CCraft]</c> LCD CustomData into <see cref="_autocraftTargets"/> + <see cref="_autocraftActiveQuotaLines"/>, then live-merges the catalog into the CustomData (preserving user comments).</summary>
        IEnumerator<YieldReason> StepLoadCCraftConfig() {
            _autocraftTargets.Clear();
            _autocraftActiveQuotaLines.Clear();
            _ccraftConfigHost = FindCCraftConfigHost();
            if (_ccraftConfigHost == null) {
                yield return YieldReason.ChunkBoundary;
                yield break;
            }
            ParseCCraftCustomData(_ccraftConfigHost.CustomData);
            SyncCCraftTemplate(_ccraftConfigHost);
            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>Parses the <c>[Crane]</c> section of <paramref name="customData"/>. Skips comment/blank lines; emits warnings for malformed lines.</summary>
        void ParseCCraftCustomData(string customData) {
            if (string.IsNullOrEmpty(customData)) return;
            string[] lines = customData.Split('\n');
            bool inCrane = false;
            for (int i = 0; i < lines.Length; i++) {
                string trimmed = lines[i].TrimEnd('\r').Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal)) {
                    inCrane = trimmed.Equals("[Crane]", StringComparison.Ordinal);
                    continue;
                }
                if (!inCrane) continue;
                if (trimmed.StartsWith(";", StringComparison.Ordinal)) continue;
                if (trimmed.StartsWith("#", StringComparison.Ordinal)) continue;
                int eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;
                string key = trimmed.Substring(0, eq).Trim();
                string val = trimmed.Substring(eq + 1).Trim();
                if (key.Length == 0 || val.Length == 0) continue;
                if (!QuotaParsing.IsValidQuotaKey(key)) continue;
                _autocraftActiveQuotaLines[key] = key + "=" + val;
                long target;
                AutocraftMode mode;
                bool ignore;
                if (!AutocraftQuotaParsing.TryParseAutocraftQuotaValue(val, out target, out mode, out ignore)) {
                    _logger.LogWarningOnce("ccraft:bad-val:" + key,
                        "[Crane] Bad autocraft value on '" + key + "': '" + val + "'");
                    continue;
                }
                if (ignore) continue;
                string typeIdWithPrefix, subtypeId;
                if (!QuotaParsing.TryParseQuotaKeyShape(key, out typeIdWithPrefix, out subtypeId)) continue;
                MyItemType? resolved = QuotaParsing.ResolveItemTypeViaParse(typeIdWithPrefix + "/" + subtypeId);
                if (!resolved.HasValue) {
                    _logger.LogWarningOnce("ccraft:unres:" + key,
                        "[Crane] Could not resolve item type for '" + key + "'");
                    continue;
                }
                _autocraftTargets.Add(new AutocraftTarget {
                    Key = key,
                    ItemType = resolved.Value,
                    Target = target,
                    Mode = mode
                });
            }
        }

        /// <summary>Reusable merged-keys set for <see cref="SyncCCraftTemplate"/>.</summary>
        readonly HashSet<string> _ccraftMergedKeys = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Reusable sorted-keys list for <see cref="SyncCCraftTemplate"/>.</summary>
        readonly List<string> _ccraftSortedKeys = new List<string>();

        /// <summary>Reusable builder for the <c>[CCraft]</c> CustomData body.</summary>
        readonly StringBuilder _ccraftTemplateSB = new StringBuilder(2048);

        /// <summary>Refreshes the <c>[CCraft]</c> LCD CustomData. Preserves user-active quota lines; appends new catalog items as <c>=x</c>; sorts alphabetical by key. Writes only on diff.</summary>
        void SyncCCraftTemplate(IMyTerminalBlock host) {
            string current = host.CustomData ?? string.Empty;

            string blueprintsSection = ExtractSectionVerbatim(current, "[CCraftBlueprints]");

            StringBuilder sb = _ccraftTemplateSB;
            sb.Length = 0;
            sb.Append("[Crane]\n");
            sb.Append("; Crane Autocraft — edit values to activate management.\n");
            sb.Append("; Replace `x` with a target number to manage that item.\n");
            sb.Append("; Suffix `E` = exact mode (assemble below, disassemble above).\n");
            sb.Append("; No suffix = minimum (assemble below, surplus allowed).\n");
            sb.Append(";\n");

            _ccraftMergedKeys.Clear();
            foreach (string k in _catalog.KnownItems.Keys) _ccraftMergedKeys.Add(k);
            foreach (string k in _autocraftActiveQuotaLines.Keys) _ccraftMergedKeys.Add(k);

            _ccraftSortedKeys.Clear();
            foreach (string k in _ccraftMergedKeys) _ccraftSortedKeys.Add(k);
            _ccraftSortedKeys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < _ccraftSortedKeys.Count; i++) {
                string mergedKey = _ccraftSortedKeys[i];
                string activeLine;
                if (_autocraftActiveQuotaLines.TryGetValue(mergedKey, out activeLine)) {
                    sb.Append(activeLine).Append('\n');
                } else {
                    sb.Append(mergedKey).Append("=x\n");
                }
            }

            if (!string.IsNullOrEmpty(blueprintsSection)) {
                sb.Append('\n').Append(blueprintsSection);
            }

            string desired = sb.ToString();
            if (!string.Equals(current, desired, StringComparison.Ordinal)) {
                host.CustomData = desired;
            }
        }

        /// <summary>Returns the contents of <paramref name="sectionHeader"/> (header line plus body lines up to the next section header) verbatim from <paramref name="customData"/>, or empty when the section is absent.</summary>
        static string ExtractSectionVerbatim(string customData, string sectionHeader) {
            if (string.IsNullOrEmpty(customData) || string.IsNullOrEmpty(sectionHeader)) return string.Empty;
            string[] lines = customData.Split('\n');
            int start = -1;
            for (int i = 0; i < lines.Length; i++) {
                string trimmed = lines[i].TrimEnd('\r').Trim();
                if (trimmed.Equals(sectionHeader, StringComparison.Ordinal)) {
                    start = i;
                    break;
                }
            }
            if (start < 0) return string.Empty;
            StringBuilder sb = new StringBuilder();
            for (int i = start; i < lines.Length; i++) {
                string trimmed = lines[i].TrimEnd('\r').Trim();
                if (i > start && trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal)) {
                    break;
                }
                sb.Append(lines[i].TrimEnd('\r')).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Quota engine: diffs <see cref="_autocraftTargets"/> against <see cref="_itemTotals"/> + assembler queue accumulator, populates the deficit/surplus lists for the dispatch step.</summary>
        IEnumerator<YieldReason> StepQuotaEngine() {
            _quotaStatus.Clear();
            _quotaQueued.Clear();
            _quotaAsmCount.Clear();
            if (!_config.EnableAutocraft) {
                yield return YieldReason.ChunkBoundary;
                yield break;
            }
            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>Refreshes the assembler pool snapshot and invalidates the capability cache on divergence.</summary>
        IEnumerator<YieldReason> StepAssemblerPool() {
            HashSet<long> currentSet = new HashSet<long>();
            for (int i = 0; i < _assemblers.Count; i++) {
                if (_assemblers[i] != null && !_assemblers[i].Closed) {
                    currentSet.Add(_assemblers[i].EntityId);
                }
            }
            bool diverged = currentSet.Count != _capabilityCacheShadow.Count;
            if (!diverged) {
                foreach (long id in currentSet) {
                    if (!_capabilityCacheShadow.Contains(id)) { diverged = true; break; }
                }
            }
            if (diverged) {
                _capabilityCache.Clear();
                _capabilityCacheShadow.Clear();
                foreach (long id in currentSet) _capabilityCacheShadow.Add(id);
                if (!currentSet.Contains(_reservedAsmEntityId)) _reservedAsmEntityId = 0;
            }
            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>Multi-assembler dispatch + per-assembler reconcile. Iterates assemble work then disassemble work; calls <see cref="DistributeAndReconcile"/> for each.</summary>
        IEnumerator<YieldReason> StepDispatchAndReconcile() {
            if (!_config.EnableAutocraft || _assemblers.Count == 0 || _autocraftTargets.Count == 0) {
                yield return YieldReason.ChunkBoundary;
                yield break;
            }
            _modeLockedThisTick.Clear();

            List<AutocraftTarget> assembleWork = new List<AutocraftTarget>();
            List<AutocraftTarget> disassembleWork = new List<AutocraftTarget>();
            for (int i = 0; i < _autocraftTargets.Count; i++) {
                AutocraftTarget t = _autocraftTargets[i];
                long actual;
                _itemTotals.TryGetValue(t.ItemType, out actual);
                long queued = SumQueuedAmount(t);
                _quotaQueued[t.Key] = queued;
                long effective = actual + queued;
                if (effective < t.Target) {
                    assembleWork.Add(t);
                    _quotaStatus[t.Key] = AutocraftLineStatus.Crafting;
                } else if (t.Mode == AutocraftMode.Exact && effective > t.Target) {
                    disassembleWork.Add(t);
                    _quotaStatus[t.Key] = AutocraftLineStatus.Disassembling;
                } else {
                    _quotaStatus[t.Key] = AutocraftLineStatus.OK;
                }
            }

            int assembleCount, disassembleCount;
            bool reservedActive;
            bool reserveEnabled = _assemblers.Count > 1
                                && assembleWork.Count > 0
                                && disassembleWork.Count > 0;
            PoolSplit.ComputePoolSplitWithReservation(_assemblers.Count,
                assembleWork.Count, disassembleWork.Count, reserveEnabled,
                out assembleCount, out disassembleCount, out reservedActive);

            long reservedId = SelectReservedAssembler(disassembleWork, reservedActive);
            if (reservedActive) _reservedAsmEntityId = reservedId;
            else _reservedAsmEntityId = 0;

            List<IMyAssembler> assembleSlice;
            List<IMyAssembler> disassembleSlice;
            BuildAssemblerSlices(assembleCount, disassembleCount, reservedActive, reservedId,
                out assembleSlice, out disassembleSlice);

            DistributeAndReconcile(assembleSlice, assembleWork, MyAssemblerMode.Assembly);
            DistributeAndReconcile(disassembleSlice, disassembleWork, MyAssemblerMode.Disassembly);

            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>Selects the reserved disassembly assembler with hysteresis: keep current reserved if still capable of any disassemble-work key; else pick most-capable; tiebreak lowest EntityId.</summary>
        long SelectReservedAssembler(List<AutocraftTarget> disassembleWork, bool reservedActive) {
            if (!reservedActive || disassembleWork.Count == 0 || _assemblers.Count == 0) return 0;
            if (_reservedAsmEntityId != 0) {
                for (int i = 0; i < _assemblers.Count; i++) {
                    if (_assemblers[i].EntityId == _reservedAsmEntityId) return _reservedAsmEntityId;
                }
            }
            long best = 0;
            int bestScore = -1;
            for (int i = 0; i < _assemblers.Count; i++) {
                long id = _assemblers[i].EntityId;
                int score = 0;
                for (int k = 0; k < disassembleWork.Count; k++) {
                    if (IsAssemblerCapable(_assemblers[i], disassembleWork[k])) score++;
                }
                if (score > bestScore || (score == bestScore && id < best)) {
                    bestScore = score;
                    best = id;
                }
            }
            return best;
        }

        /// <summary>Builds the assemble/disassemble slices from <see cref="_assemblers"/>, putting the reserved assembler first into the disassemble slice when active.</summary>
        void BuildAssemblerSlices(int assembleCount, int disassembleCount, bool reservedActive, long reservedId,
            out List<IMyAssembler> assembleSlice, out List<IMyAssembler> disassembleSlice) {
            assembleSlice = new List<IMyAssembler>();
            disassembleSlice = new List<IMyAssembler>();
            if (reservedActive && reservedId != 0) {
                for (int i = 0; i < _assemblers.Count; i++) {
                    if (_assemblers[i].EntityId == reservedId) {
                        disassembleSlice.Add(_assemblers[i]);
                        break;
                    }
                }
            }
            for (int i = 0; i < _assemblers.Count; i++) {
                IMyAssembler a = _assemblers[i];
                if (reservedActive && a.EntityId == reservedId) continue;
                if (assembleSlice.Count < assembleCount) assembleSlice.Add(a);
                else if (disassembleSlice.Count < (disassembleCount)) disassembleSlice.Add(a);
            }
        }

        /// <summary>Per-(item) distribution + per-assembler reconcile loop. For each work item: build capable slice, split deficit, and reconcile each assembler's queue.</summary>
        void DistributeAndReconcile(List<IMyAssembler> slice, List<AutocraftTarget> work, MyAssemblerMode mode) {
            if (slice == null || slice.Count == 0 || work == null || work.Count == 0) return;
            for (int w = 0; w < work.Count; w++) {
                AutocraftTarget t = work[w];
                List<IMyAssembler> capableSlice = new List<IMyAssembler>();
                for (int s = 0; s < slice.Count; s++) {
                    if (IsAssemblerCapable(slice[s], t)) capableSlice.Add(slice[s]);
                }
                _quotaAsmCount[t.Key] = capableSlice.Count;
                if (capableSlice.Count == 0) {
                    _quotaStatus[t.Key] = HasResolvedBlueprint(t)
                        ? AutocraftLineStatus.BlockedNoCapableAsm
                        : AutocraftLineStatus.BlockedNeedsLearn;
                    continue;
                }
                long deficit;
                if (mode == MyAssemblerMode.Assembly) {
                    long actual; _itemTotals.TryGetValue(t.ItemType, out actual);
                    long queued; _quotaQueued.TryGetValue(t.Key, out queued);
                    deficit = t.Target - (actual + queued);
                } else {
                    long actual; _itemTotals.TryGetValue(t.ItemType, out actual);
                    long queued; _quotaQueued.TryGetValue(t.Key, out queued);
                    deficit = (actual - queued) - t.Target;
                }
                if (deficit <= 0) continue;
                long[] shares = DeficitSplit.SplitDeficit(deficit, capableSlice.Count, 5);
                for (int i = 0; i < capableSlice.Count && i < shares.Length; i++) {
                    ReconcileAssembler(capableSlice[i], t, shares[i], mode);
                }
            }
        }

        /// <summary>Per-assembler reconcile: ensure mode matches (without flipping a non-empty queue), strip non-target items from queue, top-up or trim toward <paramref name="share"/>.</summary>
        void ReconcileAssembler(IMyAssembler asm, AutocraftTarget t, long share, MyAssemblerMode mode) {
            if (asm == null || share <= 0) return;
            if (!CanFlipMode(asm, mode)) return;
            if (asm.Mode != mode) {
                if (!asm.IsQueueEmpty) return;
                try { asm.Mode = mode; } catch { return; }
                _modeLockedThisTick.Add(asm.EntityId);
            }
            MyDefinitionId? bp = ResolveBlueprintForTarget(asm, t);
            if (!bp.HasValue) {
                _quotaStatus[t.Key] = AutocraftLineStatus.BlockedNeedsLearn;
                return;
            }
            long alreadyOnThisAsm = SumQueuedAmountOnAssembler(asm, t);
            long topUp = share - alreadyOnThisAsm;
            if (topUp > 0) {
                try {
                    asm.AddQueueItem(bp.Value, (MyFixedPoint)(double)topUp);
                    if (_config.DebugLogging) _logger.LogAction("queue +" + topUp + " " + t.Key);
                } catch (Exception ex) {
                    _logger.LogWarningOnce("queue:add:" + t.Key,
                        "[Crane] AddQueueItem failed on " + asm.CustomName + " for " + t.Key + ": " + ex.Message);
                }
            }
        }

        /// <summary>True when the assembler's queue is empty or already in the desired mode (i.e. a mode flip is safe or unnecessary).</summary>
        bool CanFlipMode(IMyAssembler asm, MyAssemblerMode desired) {
            if (asm.Mode == desired) return true;
            return asm.IsQueueEmpty && !_modeLockedThisTick.Contains(asm.EntityId);
        }

        /// <summary>True when at least one assembler in the pool can produce <paramref name="t"/> (cache hit). Used to disambiguate <see cref="AutocraftLineStatus.BlockedNoCapableAsm"/> from <see cref="AutocraftLineStatus.BlockedNeedsLearn"/>.</summary>
        bool HasResolvedBlueprint(AutocraftTarget t) {
            HashSet<long> capable;
            if (_capabilityCache.TryGetValue(t.Key, out capable) && capable != null && capable.Count > 0) return true;
            return false;
        }

        /// <summary>Probes <paramref name="asm"/> for the ability to use the blueprint for <paramref name="t"/>. Caches successful probes. Walks the curated map, then direct, then marks NeedsLearn.</summary>
        bool IsAssemblerCapable(IMyAssembler asm, AutocraftTarget t) {
            if (asm == null) return false;
            HashSet<long> cached;
            if (_capabilityCache.TryGetValue(t.Key, out cached) && cached != null && cached.Contains(asm.EntityId)) return true;
            MyDefinitionId? bp = ResolveBlueprintForTarget(asm, t);
            if (!bp.HasValue) return false;
            try {
                if (asm.CanUseBlueprint(bp.Value)) {
                    HashSet<long> set;
                    if (!_capabilityCache.TryGetValue(t.Key, out set)) {
                        set = new HashSet<long>();
                        _capabilityCache[t.Key] = set;
                    }
                    set.Add(asm.EntityId);
                    return true;
                }
            } catch {
                return false;
            }
            return false;
        }

        /// <summary>Sums the queued amount of <paramref name="t"/>'s blueprint across every assembler in the pool. Uses ceiling-rounding to avoid off-by-one drift on fractional in-progress crafts.</summary>
        long SumQueuedAmount(AutocraftTarget t) {
            long total = 0;
            for (int i = 0; i < _assemblers.Count; i++) {
                total += SumQueuedAmountOnAssembler(_assemblers[i], t);
            }
            return total;
        }

        /// <summary>Reusable queue scratch buffer.</summary>
        readonly List<MyProductionItem> _queueBuffer = new List<MyProductionItem>();

        /// <summary>Sums the queued amount of <paramref name="t"/>'s blueprint on a single assembler.</summary>
        long SumQueuedAmountOnAssembler(IMyAssembler asm, AutocraftTarget t) {
            if (asm == null) return 0;
            _queueBuffer.Clear();
            try { asm.GetQueue(_queueBuffer); } catch { return 0; }
            long total = 0;
            string itemSubtype = t.ItemType.SubtypeId ?? string.Empty;
            for (int i = 0; i < _queueBuffer.Count; i++) {
                string queueSub = _queueBuffer[i].BlueprintId.SubtypeName ?? string.Empty;
                if (BlueprintSubtypeMatchesItem(queueSub, itemSubtype)) {
                    total += CeilHelpers.CeilToLong(_queueBuffer[i].Amount);
                }
            }
            return total;
        }

        /// <summary>True when <paramref name="blueprintSubtype"/> corresponds to <paramref name="itemSubtype"/> directly or via the curated map.</summary>
        static bool BlueprintSubtypeMatchesItem(string blueprintSubtype, string itemSubtype) {
            if (blueprintSubtype == itemSubtype) return true;
            string mapped;
            if (BlueprintMisses.CuratedMap.TryGetValue(itemSubtype, out mapped)) {
                return mapped == blueprintSubtype;
            }
            return false;
        }
    }
}
