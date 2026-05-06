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

        /// <summary>Reused candidate buffer for the same-role balancer; cleared at the top of each category.</summary>
        readonly List<ContainerEntry> _roleBalanceCandidates = new List<ContainerEntry>();

        /// <summary>Reused per-tier holdings dictionary; doubles as a "type seen" set during the pull-up phase (values null) and as <c>per-container counts</c> during the within-tier split phase.</summary>
        readonly Dictionary<MyItemType, long[]> _roleBalanceHoldings = new Dictionary<MyItemType, long[]>();

        /// <summary>Reused list of containers in the currently-processing tier.</summary>
        readonly List<ContainerEntry> _roleBalanceTier = new List<ContainerEntry>();

        /// <summary>Reused flattening of all tiers below the currently-processing one (drain sources for pull-up).</summary>
        readonly List<ContainerEntry> _roleBalanceLowerTiers = new List<ContainerEntry>();

        /// <summary>Largest amount probed by <see cref="ProbeAdditionalCapacity"/>; one billion is well beyond any plausible real stack so a successful probe at this size is treated as "unbounded."</summary>
        const long RoleBalanceProbeMax = 1000000000L;

        /// <summary>
        /// Step 9: redistribute items across non-Stock containers sharing a category tag so each
        /// priority tier holds an equal share per item type. Off by default; opt in via
        /// <c>enableSameRoleBalancing=true</c> in PB CustomData. Higher-priority tiers fully pack
        /// before lower tiers receive items; overflow only flows down once the upper tier is full.
        /// </summary>
        IEnumerator<YieldReason> StepBalanceSameRoleContainers() {
            if (!_config.EnableSameRoleBalancing) {
                yield return YieldReason.ChunkBoundary;
                yield break;
            }
            foreach (var kv in _containersByCategory) {
                List<ContainerEntry> routes = kv.Value;
                if (routes == null || routes.Count < 2) continue;

                _roleBalanceCandidates.Clear();
                for (int r = 0; r < routes.Count; r++) {
                    ContainerEntry e = routes[r];
                    if (e == null) continue;
                    if (e.IsStock) continue;
                    if (e.ConsumerKind != ConsumerKind.None) continue;
                    if (!ValidateBlock(e.Block)) continue;
                    if (e.Inventory == null) continue;
                    _roleBalanceCandidates.Add(e);
                }
                if (!WillSameRoleBalancingRun(true, _roleBalanceCandidates.Count)) continue;

                IEnumerator<YieldReason> child = BalanceCategoryByTier(kv.Key, _roleBalanceCandidates);
                while (child.MoveNext()) yield return child.Current;

                yield return YieldReason.ChunkBoundary;
                if (BudgetExceeded()) yield return YieldReason.BudgetHit;
            }
        }

        /// <summary>
        /// Walks <paramref name="candidates"/> (already sorted ascending by Priority) one priority
        /// tier at a time. For each tier, first pulls items up from all lower tiers, then equalises
        /// holdings within the tier. Yields between tiers and on budget hits.
        /// </summary>
        IEnumerator<YieldReason> BalanceCategoryByTier(ItemCategory category, List<ContainerEntry> candidates) {
            int n = candidates.Count;
            int i = 0;
            while (i < n) {
                int tierStart = i;
                int tierPriority = candidates[i].Priority;
                while (i < n && candidates[i].Priority == tierPriority) i++;
                int tierEnd = i;

                _roleBalanceTier.Clear();
                for (int t = tierStart; t < tierEnd; t++) _roleBalanceTier.Add(candidates[t]);

                _roleBalanceLowerTiers.Clear();
                for (int t = tierEnd; t < n; t++) _roleBalanceLowerTiers.Add(candidates[t]);

                if (_roleBalanceLowerTiers.Count > 0) {
                    IEnumerator<YieldReason> pull = PullUpIntoTier(category, _roleBalanceTier, _roleBalanceLowerTiers);
                    while (pull.MoveNext()) yield return pull.Current;
                }

                if (_roleBalanceTier.Count >= 2) {
                    IEnumerator<YieldReason> split = BalanceWithinTier(category, _roleBalanceTier);
                    while (split.MoveNext()) yield return split.Current;
                }

                if (BudgetExceeded()) yield return YieldReason.BudgetHit;
            }
        }

        /// <summary>
        /// Pulls items up from <paramref name="lowerTiers"/> into <paramref name="upperTier"/>
        /// until upper containers cannot accept more of any type that classifies into
        /// <paramref name="category"/>. Off-category items (e.g. Ingots sitting in a multi-tag
        /// "Ores Ingots" container while we are processing the Ores category) are intentionally
        /// left in place so the Ingots-category pass can balance them, and so the generic sort
        /// step doesn't fight us by routing them straight back. Order is upper-by-position then
        /// lower-by-position; <see cref="MoveAllOfType"/>'s capacity clamping handles partial fits.
        /// </summary>
        IEnumerator<YieldReason> PullUpIntoTier(ItemCategory category, List<ContainerEntry> upperTier, List<ContainerEntry> lowerTiers) {
            // Collect distinct item types observed in any lower-tier container, filtered to
            // types that classify into the current category. This prevents a multi-tag container
            // (e.g. one tagged both "Ores" and "Ingots") from leaking its off-category contents
            // into containers tagged only for the current category — which the next sort pass
            // would just route back, wasting budget every cycle.
            // _roleBalanceHoldings is reused as a key-only set here; values are null and unused.
            _roleBalanceHoldings.Clear();
            for (int l = 0; l < lowerTiers.Count; l++) {
                ContainerEntry low = lowerTiers[l];
                if (low.Inventory == null) continue;
                _itemBuffer.Clear();
                low.Inventory.GetItems(_itemBuffer);
                for (int k = 0; k < _itemBuffer.Count; k++) {
                    MyItemType t = _itemBuffer[k].Type;
                    if (Classify(t) != category) continue;
                    if (!_roleBalanceHoldings.ContainsKey(t)) _roleBalanceHoldings[t] = null;
                }
            }

            foreach (var kv in _roleBalanceHoldings) {
                MyItemType type = kv.Key;
                for (int u = 0; u < upperTier.Count; u++) {
                    ContainerEntry hi = upperTier[u];
                    if (hi.Inventory == null) continue;
                    for (int l = 0; l < lowerTiers.Count; l++) {
                        ContainerEntry low = lowerTiers[l];
                        if (low.Inventory == null) continue;
                        long avail = GetCurrentAmount(low.Inventory, type);
                        if (avail <= 0) continue;
                        TryMove(low.Inventory, hi.Inventory, type, avail, "rolebal-pullup");
                        if (BudgetExceeded()) yield return YieldReason.BudgetHit;
                    }
                    if (BudgetExceeded()) yield return YieldReason.BudgetHit;
                }
            }
        }

        /// <summary>
        /// Equalises holdings of every item type observed in <paramref name="tier"/> that
        /// classifies into <paramref name="category"/>, water-filling around per-container
        /// capacity ceilings. Off-category items in the tier (rare; a transient state between
        /// the sorter and the connector hand-off) are skipped so we don't redistribute items
        /// that the sorter is about to relocate. Computes targets via
        /// <see cref="ComputeEqualSplitTargets"/> then applies them with a single pass of
        /// pair-wise transfers from over-target sources to under-target destinations.
        /// </summary>
        IEnumerator<YieldReason> BalanceWithinTier(ItemCategory category, List<ContainerEntry> tier) {
            BuildTierHoldings(category, tier, _roleBalanceHoldings);
            foreach (var kv in _roleBalanceHoldings) {
                MyItemType type = kv.Key;
                long[] holdings = kv.Value;
                int count = holdings.Length;
                long[] capacity = new long[count];
                long[] targets = new long[count];
                for (int i = 0; i < count; i++) {
                    capacity[i] = ProbeAdditionalCapacity(tier[i].Inventory, type);
                }
                ComputeEqualSplitTargets(holdings, capacity, targets);

                for (int s = 0; s < count; s++) {
                    long over = holdings[s] - targets[s];
                    if (over <= 0) continue;
                    for (int d = 0; d < count && over > 0; d++) {
                        if (d == s) continue;
                        long under = targets[d] - holdings[d];
                        if (under <= 0) continue;
                        long want = over < under ? over : under;
                        long moved = TryMove(tier[s].Inventory, tier[d].Inventory, type, want, "rolebal-split");
                        if (moved <= 0) {
                            // Destination refused this transfer (volume cap, partial-fit edge case);
                            // bail this src/dst pair so we don't loop forever on a stuck transfer.
                            break;
                        }
                        holdings[s] -= moved;
                        holdings[d] += moved;
                        over -= moved;
                        if (BudgetExceeded()) yield return YieldReason.BudgetHit;
                    }
                }
                if (BudgetExceeded()) yield return YieldReason.BudgetHit;
            }
        }

        /// <summary>
        /// Aggregates per-container holdings across <paramref name="tier"/> into
        /// <paramref name="outHoldings"/>, restricted to item types that classify into
        /// <paramref name="category"/>. The output dictionary is cleared on entry; each value
        /// is a fresh <c>long[]</c> sized to the tier's container count, indexed by tier position.
        /// </summary>
        void BuildTierHoldings(ItemCategory category, List<ContainerEntry> tier, Dictionary<MyItemType, long[]> outHoldings) {
            outHoldings.Clear();
            int n = tier.Count;
            for (int i = 0; i < n; i++) {
                IMyInventory inv = tier[i].Inventory;
                if (inv == null) continue;
                _itemBuffer.Clear();
                inv.GetItems(_itemBuffer);
                for (int k = 0; k < _itemBuffer.Count; k++) {
                    MyItemType type = _itemBuffer[k].Type;
                    // Defense in depth: skip items that don't belong to this category.
                    // The within-tier split must not redistribute foreign items between
                    // containers — those should be left for StepSortGenericCargo to relocate.
                    if (Classify(type) != category) continue;
                    long[] arr;
                    if (!outHoldings.TryGetValue(type, out arr)) {
                        arr = new long[n];
                        outHoldings[type] = arr;
                    }
                    arr[i] += (long)_itemBuffer[k].Amount;
                }
            }
        }

        /// <summary>
        /// Probes <paramref name="inv"/>'s additional capacity for <paramref name="type"/> via
        /// repeated <c>CanItemsBeAdded</c> calls on a coarse log scale. Returns
        /// <see cref="long.MaxValue"/> when even <see cref="RoleBalanceProbeMax"/> fits (treated
        /// as "unbounded"); returns 0 when even a single unit is rejected. At most six probe calls.
        /// </summary>
        long ProbeAdditionalCapacity(IMyInventory inv, MyItemType type) {
            if (inv == null) return 0;
            if (inv.CanItemsBeAdded((MyFixedPoint)(double)RoleBalanceProbeMax, type)) return long.MaxValue;
            if (inv.CanItemsBeAdded((MyFixedPoint)10000000.0, type)) return 10000000L;
            if (inv.CanItemsBeAdded((MyFixedPoint)100000.0, type)) return 100000L;
            if (inv.CanItemsBeAdded((MyFixedPoint)1000.0, type)) return 1000L;
            if (inv.CanItemsBeAdded((MyFixedPoint)10.0, type)) return 10L;
            if (inv.CanItemsBeAdded((MyFixedPoint)1.0, type)) return 1L;
            return 0;
        }

        /// <summary>
        /// Pure helper. Returns the unit count to move from a higher-holder to a lower-holder so
        /// they converge to the equal split, clamped by destination capacity. Returns 0 when no
        /// move is sensible (source already at-or-below destination, capacity zero/negative,
        /// or diff smaller than 2).
        /// </summary>
        /// <param name="srcCount">Current units of the type held by the source.</param>
        /// <param name="dstCount">Current units of the type held by the destination.</param>
        /// <param name="dstRemainingCapacityForType">Destination's additional capacity for this
        /// type in units. Caller passes <see cref="long.MaxValue"/> when no constraint is known.</param>
        internal static long ComputeBalanceTransfer(long srcCount, long dstCount, long dstRemainingCapacityForType) {
            if (srcCount <= dstCount) return 0;
            if (dstRemainingCapacityForType <= 0) return 0;
            long diff = srcCount - dstCount;
            long half = diff / 2;
            if (half <= 0) return 0;
            if (dstRemainingCapacityForType < half) return dstRemainingCapacityForType;
            return half;
        }

        /// <summary>
        /// Pure helper. Water-fills equal-split targets given current holdings and per-container
        /// additional-capacity ceilings. Containers that would receive more than they can hold
        /// are capped at their ceiling and the remainder is redistributed across the still-uncapped
        /// containers. When the entire tier is at ceiling, leftover units are left unallocated and
        /// the sum of <paramref name="outTargets"/> may be less than the sum of
        /// <paramref name="currentHoldings"/>; the caller is expected to leave unallocated units
        /// in lower tiers.
        /// </summary>
        /// <param name="currentHoldings">Per-container current units of one item type. Length N.</param>
        /// <param name="additionalCapacity">Per-container additional units that fit beyond current
        /// holdings; <see cref="long.MaxValue"/> means unbounded.</param>
        /// <param name="outTargets">Filled with per-container target holdings on exit. Length must
        /// match <paramref name="currentHoldings"/>.</param>
        internal static void ComputeEqualSplitTargets(long[] currentHoldings, long[] additionalCapacity, long[] outTargets) {
            int n = currentHoldings.Length;
            long total = 0;
            for (int i = 0; i < n; i++) total += currentHoldings[i];
            long[] ceilings = new long[n];
            for (int i = 0; i < n; i++) {
                long add = additionalCapacity[i];
                ceilings[i] = (add == long.MaxValue) ? long.MaxValue : currentHoldings[i] + add;
                outTargets[i] = 0;
            }
            bool[] capped = new bool[n];
            long remaining = total;
            int uncapped = n;
            while (uncapped > 0 && remaining > 0) {
                long share = remaining / uncapped;
                long extra = remaining - share * uncapped;
                bool changed = false;
                int seen = 0;
                for (int i = 0; i < n; i++) {
                    if (capped[i]) continue;
                    long want = share + (seen < extra ? 1 : 0);
                    seen++;
                    if (ceilings[i] < want) {
                        outTargets[i] = ceilings[i];
                        remaining -= ceilings[i];
                        capped[i] = true;
                        uncapped--;
                        changed = true;
                    }
                }
                if (changed) continue;
                seen = 0;
                for (int i = 0; i < n; i++) {
                    if (capped[i]) continue;
                    long want = share + (seen < extra ? 1 : 0);
                    seen++;
                    outTargets[i] = want;
                    remaining -= want;
                }
                break;
            }
        }

        /// <summary>
        /// Pure helper. Predicates whether a same-role balancing pass should execute for a given
        /// category given the current config and post-filter candidate count.
        /// </summary>
        internal static bool WillSameRoleBalancingRun(bool configEnabled, int candidateCount) {
            return configEnabled && candidateCount >= 2;
        }
    }
}
