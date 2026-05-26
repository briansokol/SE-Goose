using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        /// <summary>Class of consumer block recognised by the balancer.</summary>
        public enum ConsumerKind
        {
            /// <summary>Not a consumer; balancer ignores. Also the value used for blocks tagged <c>[NoBalance]</c>.</summary>
            None,
            /// <summary>Block implementing <see cref="IMyReactor"/>; balanced for <c>Ingot/Uranium</c>.</summary>
            Reactor,
            /// <summary>Block implementing <see cref="IMyGasGenerator"/> (O2/H2 generators, irrigation systems, etc.); balanced for <c>Ore/Ice</c>.</summary>
            Gas,
            /// <summary>Weapon (turret, fixed gun, or modded equivalent) that accepts an <c>AmmoMagazine</c> subtype.</summary>
            Weapon
        }

        // The three balancer item types are exposed as lazy properties rather than
        // static-readonly fields so the test harness can load Program without
        // triggering the SE definition-registry lookup that runs inside the
        // MyItemType ctor (and which is unavailable outside the game runtime).

        private static MyItemType? _ingotUraniumCache;

        /// <summary>Reactor fuel type used by the balancer's reactor probe and fill logic.</summary>
        private static MyItemType IngotUranium
        {
            get
            {
                if (!_ingotUraniumCache.HasValue)
                {
                    _ingotUraniumCache = new MyItemType("MyObjectBuilder_Ingot", "Uranium");
                }

                return _ingotUraniumCache.Value;
            }
        }

        private static MyItemType? _oreIceCache;

        /// <summary>Gas-generator and irrigation feedstock used by the balancer's gas probe and fill logic.</summary>
        private static MyItemType OreIce
        {
            get
            {
                if (!_oreIceCache.HasValue)
                {
                    _oreIceCache = new MyItemType("MyObjectBuilder_Ore", "Ice");
                }

                return _oreIceCache.Value;
            }
        }

        private static MyItemType? _componentSteelPlateCache;

        /// <summary>Control item used to reject generic cargo containers from consumer detection. A real cargo container accepts SteelPlate; a reactor or weapon does not.</summary>
        private static MyItemType ComponentSteelPlate
        {
            get
            {
                if (!_componentSteelPlateCache.HasValue)
                {
                    _componentSteelPlateCache = new MyItemType("MyObjectBuilder_Component", "SteelPlate");
                }

                return _componentSteelPlateCache.Value;
            }
        }

        /// <summary>Computes the absolute volume target (in m^3) for a weapon given its inventory capacity and the configured fill percent.</summary>
        /// <param name="maxVolume">Inventory's maximum volume, in m^3.</param>
        /// <param name="percent">Configured fill percent, 0-100. Caller is expected to clamp before calling.</param>
        /// <returns>Volume in m^3 that the weapon should be filled to.</returns>
        internal static float ComputeFillTargetVolume(float maxVolume, int percent)
        {
            return maxVolume * (percent / 100f);
        }


        /// <summary>
        /// Computes the natural per-reactor uranium-ingot fill target from the reactor's inventory volume
        /// and a "ingots per 1000L" ratio. Reactors with less than 1000L of volume are floored to 10 ingots
        /// (so a small reactor still has spark fuel). Larger reactors use round(volumeL / 1000) * ratio,
        /// rounding half-away-from-zero (1500L → 2 buckets, 2490L → 2 buckets).
        /// </summary>
        /// <param name="maxVolumeCubicMeters">Reactor inventory <c>MaxVolume</c> in m³ (1 m³ = 1000 L).</param>
        /// <param name="ingotsPer1000L">Class ratio. Caller is responsible for treating <c>&lt;= 0</c> as "feature off".</param>
        /// <returns>Target uranium-ingot count for this reactor (always non-negative).</returns>
        /// <summary>
        /// Computes the natural per-reactor uranium-ingot fill target from the reactor's inventory volume
        /// and a "ingots per 1000L" ratio. Each 1000L of volume adds another bucket of <paramref name="ingotsPer1000L"/>
        /// ingots, with the first bucket starting at 1L: 1–999L → 1 × ratio, 1000–1999L → 2 × ratio,
        /// 2000–2999L → 3 × ratio, etc.
        /// </summary>
        /// <param name="maxVolumeCubicMeters">Reactor inventory <c>MaxVolume</c> in m³ (1 m³ = 1000 L).</param>
        /// <param name="ingotsPer1000L">Class ratio. <c>&lt;= 0</c> disables this class (returns 0).</param>
        /// <returns>Target uranium-ingot count for this reactor (always non-negative).</returns>
        internal static long ComputeReactorIngotTarget(double maxVolumeCubicMeters, int ingotsPer1000L)
        {
            if (ingotsPer1000L <= 0)
            {
                return 0L;
            }

            double volumeLiters = maxVolumeCubicMeters * 1000.0;
            if (volumeLiters < 0.0)
            {
                volumeLiters = 0.0;
            }

            long buckets = (long)(volumeLiters / 1000.0) + 1L;
            return buckets * (long)ingotsPer1000L;
        }

        /// <summary>Resolves a <see cref="ConsumerKind"/> from four item-acceptance probe results. Pure helper exposed for unit testing; production code calls <c>IMyInventory.CanItemsBeAdded</c> on the live inventory and feeds the booleans here.</summary>
        /// <param name="canAddIngotUranium">True if the inventory accepts <c>Ingot/Uranium</c>.</param>
        /// <param name="canAddOreIce">True if the inventory accepts <c>Ore/Ice</c>.</param>
        /// <param name="canAddAnyAmmo">True if the inventory accepts at least one known <c>AmmoMagazine</c> subtype.</param>
        /// <param name="canAddSteelPlate">True if the inventory accepts <c>Component/SteelPlate</c>; identifies generic cargo containers.</param>
        /// <returns>The matched <see cref="ConsumerKind"/>, or <see cref="ConsumerKind.None"/> when the block is a generic container or accepts nothing recognised.</returns>


        /// <summary>Vanilla ammo magazine subtypes seeded into consumer probing so weapons are detected on a fresh world before any ammo has been observed by the catalog.</summary>
        private static readonly string[] VanillaAmmoMagazineSubtypes = new string[] {
            "NATO_25x184mm",
            "NATO_5p56x45mm",
            "Missile200mm",
            "AutocannonClip",
            "MediumCalibreAmmo",
            "LargeCalibreAmmo"
        };

        /// <summary>Reusable scratch list, refreshed each cycle, of ammo magazine types to probe weapon candidates with.</summary>
        private readonly List<MyItemType> _ammoCandidates = new List<MyItemType>();

        /// <summary>Reusable scratch set used to dedupe ammo candidate keys.</summary>
        private readonly HashSet<string> _ammoCandidateKeys = new HashSet<string>();


        /// <summary>Per-cycle cache of measured per-unit volume (m^3/unit) for items the balancer has transferred this cycle. Lets the bulk-transfer helpers compute exact unit counts from a remaining-volume headroom without iterating one unit at a time. Cleared at the start of every <see cref="StepBalanceConsumers"/> run.</summary>
        private readonly Dictionary<MyItemType, float> _balanceVolumeCache = new Dictionary<MyItemType, float>();


        /// <summary>Per-pass list of viable source containers (non-null entries with a non-null inventory). Populated at the top of <see cref="StepBalanceConsumers"/> so the three pull dispatches do not each rewalk <see cref="_entryByBlock"/>.</summary>
        private readonly List<ContainerEntry> _balanceSources = new List<ContainerEntry>();

        /// <summary>Total ammo-magazine volume across <see cref="_entryByBlock"/>, accumulated during <see cref="StepScanInventories"/>. Only items whose volume-per-unit has been measured into <see cref="_balanceVolumeCache"/> contribute.</summary>
        private float _gridAmmoVolume;

        /// <summary>Refreshes <see cref="_ammoCandidates"/> with the vanilla seed list plus every <c>AmmoMagazine/*</c> entry currently in the catalog.</summary>
        private void RebuildAmmoCandidateList()
        {
            _ammoCandidates.Clear();
            _ammoCandidateKeys.Clear();
            for (int i = 0; i < VanillaAmmoMagazineSubtypes.Length; i++)
            {
                string subtype = VanillaAmmoMagazineSubtypes[i];
                _ammoCandidates.Add(new MyItemType("MyObjectBuilder_AmmoMagazine", subtype));
                _ammoCandidateKeys.Add("AmmoMagazine/" + subtype);
            }
            foreach (KeyValuePair<string, MyItemType> kv in _knownItems)
            {
                if (kv.Key.StartsWith("AmmoMagazine/", StringComparison.Ordinal)
                    && !_ammoCandidateKeys.Contains(kv.Key))
                {
                    _ammoCandidates.Add(kv.Value);
                    _ammoCandidateKeys.Add(kv.Key);
                }
            }
        }

        /// <summary>Walks every managed block, applies the <c>[NoBalance]</c> exclusion gate, then probes acceptance of <see cref="IngotUranium"/>, <see cref="OreIce"/>, and known ammo magazines to assign a <see cref="ConsumerKind"/> to each entry. Caches accepted ammo magazines on weapon entries so the balance step does not re-probe.</summary>
        private IEnumerator<YieldReason> StepCategorizeConsumers()
        {
            RebuildAmmoCandidateList();

            int counter = 0;
            foreach (KeyValuePair<IMyTerminalBlock, ContainerEntry> kv in _entryByBlock)
            {
                ContainerEntry entry = kv.Value;
                if (entry == null)
                {
                    continue;
                }

                IMyTerminalBlock block = entry.Block;
                if (block == null)
                {
                    continue;
                }

                IMyInventory inv = entry.Inventory;
                entry.BalanceTagCount = ParseBalanceTagCount(block.CustomName);
                if (inv == null)
                {
                    entry.ConsumerKind = ConsumerKind.None;
                    entry.AcceptedAmmo = null;
                }
                else if (BlockNameTags.NameHasTag(block.CustomName, "[NoBalance]"))
                {
                    entry.ConsumerKind = ConsumerKind.None;
                    entry.AcceptedAmmo = null;
                }
                else
                {
                    ProbeConsumerKind(entry, inv);
                }

                if (WillBlockBeBalanced(entry.ConsumerKind, entry.BalanceTagCount, ClassActivatorFor(entry.ConsumerKind)))
                {
                    DisableUseConveyor(block);
                }

                counter++;
                if (counter % 25 == 0)
                {
                    yield return YieldReason.ChunkBoundary;
                }

                if (BudgetExceeded())
                {
                    yield return YieldReason.BudgetHit;
                }
            }
        }

        /// <summary>Returns true when the balancer will act on a block this cycle. Tagged blocks (<paramref name="balanceTagCount"/> &gt;= 0) always run; untagged blocks run only when their class percent is non-zero. Pure helper exposed for unit testing.</summary>
        /// <summary>
        /// Returns true when the balancer will act on a block of this consumer kind.
        /// <paramref name="classActivator"/> is the relevant config value: ingots-per-1000L for
        /// <see cref="ConsumerKind.Reactor"/>, percent (0-100) for Gas / Weapon. Any value &gt; 0
        /// activates the class.
        /// </summary>
        internal static bool WillBlockBeBalanced(ConsumerKind kind, long balanceTagCount, int classActivator)
        {
            if (kind == ConsumerKind.None)
            {
                return false;
            }

            if (balanceTagCount >= 0)
            {
                return true;
            }

            return classActivator > 0;
        }

        /// <summary>Returns the configured class fill percent for a given <paramref name="kind"/>.</summary>
        /// <summary>
        /// Returns the class-activating config value: ingots-per-1000L for <see cref="ConsumerKind.Reactor"/>,
        /// percent-of-volume for Gas and Weapon. Any value &gt; 0 activates the class.
        /// </summary>
        private int ClassActivatorFor(ConsumerKind kind)
        {
            switch (kind)
            {
                case ConsumerKind.Reactor:
                    return _config.ReactorUraniumIngotsPer1000L;
                case ConsumerKind.Gas:
                    return _config.GasIceFillPercent;
                case ConsumerKind.Weapon:
                    return _config.WeaponAmmoFillPercent;
                default:
                    return 0;
            }
        }

        /// <summary>Disables the block's built-in <c>UseConveyor</c> auto-pull so it stops fighting the balancer's pull/push decisions. The toggle is set via the standard terminal property exposed by reactors, gas generators, and weapons; modded blocks that omit the property are silently ignored. The change is one-way for v1 — there is no automatic restore when the balancer is later disabled. The user can re-enable conveyor pull manually via the block's terminal panel.</summary>
        private void DisableUseConveyor(IMyTerminalBlock block)
        {
            if (block == null)
            {
                return;
            }

            try
            {
                if (block.GetValueBool("UseConveyor"))
                {
                    block.SetValueBool("UseConveyor", false);
                    if (_config.DebugLogging)
                    {
                        LogAction("balance: disabled UseConveyor on " + block.CustomName);
                    }
                }
            }
            catch
            {
                // The block does not expose a UseConveyor terminal property; silently ignore.
            }
        }

        /// <summary>Probes a single inventory's item-acceptance and assigns the resulting <see cref="ConsumerKind"/> and (for weapons) <see cref="ContainerEntry.AcceptedAmmo"/> on <paramref name="entry"/>.</summary>
        private void ProbeConsumerKind(ContainerEntry entry, IMyInventory inv)
        {
            entry.ConsumerKind = ConsumerKind.None;
            entry.AcceptedAmmo = null;

            if (entry.Block is IMyReactor)
            {
                entry.ConsumerKind = ConsumerKind.Reactor;
                return;
            }
            if (entry.Block is IMyGasGenerator)
            {
                entry.ConsumerKind = ConsumerKind.Gas;
                return;
            }

            MyFixedPoint epsilon = MyFixedPoint.SmallestPossibleValue;
            if (inv.CanItemsBeAdded(epsilon, ComponentSteelPlate))
            {
                return;
            }

            List<MyItemType> accepted = null;
            for (int i = 0; i < _ammoCandidates.Count; i++)
            {
                MyItemType ammo = _ammoCandidates[i];
                if (inv.CanItemsBeAdded(epsilon, ammo))
                {
                    if (accepted == null)
                    {
                        accepted = new List<MyItemType>();
                    }

                    accepted.Add(ammo);
                }
            }
            if (accepted != null)
            {
                entry.ConsumerKind = ConsumerKind.Weapon;
                entry.AcceptedAmmo = accepted;
            }
        }


        /// <summary>Top-level balancer step. Runs reactors, then gas, then weapons (so critical-power demand wins under scarcity). Each class is a no-op when its PB target is 0.</summary>
        private IEnumerator<YieldReason> StepBalanceConsumers()
        {
            // _balanceVolumeCache persists across cycles. The proportional
            // factor needs volPerUnit *before* any transfer in this cycle, so
            // we cannot clear the cache here. It is invalidated only on script
            // recompile (via the field's natural lifecycle).

            _balanceSources.Clear();
            foreach (KeyValuePair<IMyTerminalBlock, ContainerEntry> kv in _entryByBlock)
            {
                ContainerEntry entry = kv.Value;
                if (entry == null)
                {
                    continue;
                }

                if (entry.Inventory == null)
                {
                    continue;
                }

                // Crane announces an assembler hold while it is actively crafting. Suppressing
                // the drain keeps Goose's balancer from yanking just-staged ingots back out
                // of the assembler input before the assembler can consume them.
                if (_bridge != null && entry.Block != null && _bridge.IsAssemblerHeld(entry.Block.EntityId))
                {
                    continue;
                }

                _balanceSources.Add(entry);
            }

            IEnumerator<YieldReason> child;

            child = BalanceConsumersOfKind(ConsumerKind.Reactor, _config.ReactorUraniumIngotsPer1000L);
            while (child.MoveNext())
            {
                yield return child.Current;
            }

            child = BalanceConsumersOfKind(ConsumerKind.Gas, _config.GasIceFillPercent);
            while (child.MoveNext())
            {
                yield return child.Current;
            }

            child = BalanceConsumersOfKind(ConsumerKind.Weapon, _config.WeaponAmmoFillPercent);
            while (child.MoveNext())
            {
                yield return child.Current;
            }
        }

        /// <summary>Pulls into <paramref name="dst"/> from <paramref name="srcInv"/> using a probe-then-bulk pattern. The first successful transfer for an item type measures the per-unit volume; subsequent calls (and the same call once the cache is populated) compute the remaining headroom in units and pull it in a single <see cref="TryMove"/>. Avoids the O(n) iteration cost of one-unit-at-a-time pulls when the per-unit volume is small (e.g. Ice at ~0.00037 m^3/unit, where 25%% of a 30 m^3 generator is ~20,000 units).</summary>
        private void BulkPullToTargetVolume(IMyInventory srcInv, IMyInventory dst, MyItemType item, float targetVolume)
        {
            if ((float)dst.CurrentVolume >= targetVolume)
            {
                return;
            }

            float volPerUnit;
            if (!_balanceVolumeCache.TryGetValue(item, out volPerUnit) || volPerUnit <= 0f)
            {
                float beforeVol = (float)dst.CurrentVolume;
                long moved = TryMove(srcInv, dst, item, 1, "balance");
                if (moved == 0)
                {
                    return;
                }

                float afterVol = (float)dst.CurrentVolume;
                volPerUnit = (afterVol - beforeVol) / moved;
                if (volPerUnit > 0f)
                {
                    _balanceVolumeCache[item] = volPerUnit;
                }

                if ((float)dst.CurrentVolume >= targetVolume)
                {
                    return;
                }
            }

            if (volPerUnit <= 0f)
            {
                return;
            }

            float headroom = targetVolume - (float)dst.CurrentVolume;
            long unitsToPull = (long)System.Math.Ceiling(headroom / volPerUnit);
            if (unitsToPull <= 0)
            {
                return;
            }

            TryMove(srcInv, dst, item, unitsToPull, "balance");
        }

        /// <summary>Mirror of <see cref="BulkPullToTargetVolume"/> for excess push: probes once if the cache is cold, then pushes the over-target volume in a single bulk <see cref="TryMove"/>.</summary>
        private void BulkPushFromExcessByVolume(IMyInventory dst, IMyInventory rinv, MyItemType item, float targetVolume)
        {
            if ((float)dst.CurrentVolume <= targetVolume)
            {
                return;
            }

            float volPerUnit;
            if (!_balanceVolumeCache.TryGetValue(item, out volPerUnit) || volPerUnit <= 0f)
            {
                float beforeVol = (float)dst.CurrentVolume;
                long moved = TryMove(dst, rinv, item, 1, "balance-excess");
                if (moved == 0)
                {
                    return;
                }

                float afterVol = (float)dst.CurrentVolume;
                volPerUnit = (beforeVol - afterVol) / moved;
                if (volPerUnit > 0f)
                {
                    _balanceVolumeCache[item] = volPerUnit;
                }

                if ((float)dst.CurrentVolume <= targetVolume)
                {
                    return;
                }
            }

            if (volPerUnit <= 0f)
            {
                return;
            }

            float overage = (float)dst.CurrentVolume - targetVolume;
            long unitsToPush = (long)System.Math.Ceiling(overage / volPerUnit);
            if (unitsToPush <= 0)
            {
                return;
            }

            TryMove(dst, rinv, item, unitsToPush, "balance-excess");
        }


        /// <summary>Pure helper: returns the proportional fill factor in [0, 1] for untagged blocks of a kind. Tagged blocks are reserved their full count from the supply before factor is computed; whatever's left is divided across untagged demand. Pure for unit testing.</summary>
        /// <param name="supplyVolume">Total supply of the relevant item across the grid, in m^3.</param>
        /// <param name="taggedReservedVolume">Volume reserved by tagged ([Balance=N]) blocks of the kind, in m^3.</param>
        /// <param name="untaggedDemandVolume">Total natural-target volume of untagged blocks of the kind, in m^3.</param>
        /// <returns><c>1.0</c> when there is no untagged demand or supply meets demand fully; <c>0.0</c> when tagged reservation already exhausts supply; otherwise <c>(supply - taggedReserved) / untaggedDemand</c>.</returns>
        internal static float ComputeProportionalFactor(float supplyVolume, float taggedReservedVolume, float untaggedDemandVolume)
        {
            if (untaggedDemandVolume <= 0f)
            {
                return 1f;
            }

            float availableForUntagged = supplyVolume - taggedReservedVolume;
            if (availableForUntagged <= 0f)
            {
                return 0f;
            }

            if (availableForUntagged >= untaggedDemandVolume)
            {
                return 1f;
            }

            return availableForUntagged / untaggedDemandVolume;
        }

        /// <summary>Computes the proportional factor for a given <paramref name="kind"/>. Returns <c>1.0</c> (no scaling) when the class is disabled, the cache is cold, or no untagged blocks are present. Otherwise computes the factor by walking the grid for supply + iterating <see cref="_entryByBlock"/> for tagged-reservation and untagged-demand sums.</summary>
        private float ComputeFactorForKind(ConsumerKind kind, int classActivator)
        {
            if (classActivator == 0)
            {
                return 1f;
            }

            if (kind == ConsumerKind.None)
            {
                return 1f;
            }

            float supplyVolume;
            float taggedReservedVolume;
            float untaggedDemandVolume;

            if (kind == ConsumerKind.Reactor)
            {
                MyItemType item = IngotUranium;
                float volPerUnit;
                if (!_balanceVolumeCache.TryGetValue(item, out volPerUnit) || volPerUnit <= 0f)
                {
                    return 1f;
                }

                long untaggedDemandUnits = 0;
                long taggedUnits = 0;
                foreach (KeyValuePair<IMyTerminalBlock, ContainerEntry> kv in _entryByBlock)
                {
                    ContainerEntry entry = kv.Value;
                    if (entry == null || entry.ConsumerKind != kind)
                    {
                        continue;
                    }

                    if (entry.Inventory == null)
                    {
                        continue;
                    }

                    if (entry.BalanceTagCount >= 0)
                    {
                        taggedUnits += entry.BalanceTagCount;
                    }
                    else
                    {
                        untaggedDemandUnits += ComputeReactorIngotTarget((double)entry.Inventory.MaxVolume, classActivator);
                    }
                }
                if (untaggedDemandUnits <= 0)
                {
                    return 1f;
                }

                long supplyUnits = SumGridItemAmount(item);
                supplyVolume = supplyUnits * volPerUnit;
                taggedReservedVolume = taggedUnits * volPerUnit;
                untaggedDemandVolume = untaggedDemandUnits * volPerUnit;
            }
            else
            {
                untaggedDemandVolume = 0f;
                foreach (KeyValuePair<IMyTerminalBlock, ContainerEntry> kv in _entryByBlock)
                {
                    ContainerEntry entry = kv.Value;
                    if (entry == null || entry.ConsumerKind != kind)
                    {
                        continue;
                    }

                    if (entry.BalanceTagCount >= 0)
                    {
                        continue;
                    }

                    if (entry.Inventory == null)
                    {
                        continue;
                    }

                    untaggedDemandVolume += (float)entry.Inventory.MaxVolume * (classActivator / 100f);
                }
                if (untaggedDemandVolume <= 0f)
                {
                    return 1f;
                }

                if (kind == ConsumerKind.Weapon)
                {
                    supplyVolume = SumGridAmmoVolume();
                    if (supplyVolume <= 0f)
                    {
                        return 1f;
                    }

                    float avgAmmoVol = AverageCachedAmmoVolPerUnit();
                    long taggedTotalCount = 0;
                    foreach (KeyValuePair<IMyTerminalBlock, ContainerEntry> kv in _entryByBlock)
                    {
                        ContainerEntry entry = kv.Value;
                        if (entry == null || entry.ConsumerKind != ConsumerKind.Weapon)
                        {
                            continue;
                        }

                        if (entry.BalanceTagCount < 0)
                        {
                            continue;
                        }

                        taggedTotalCount += entry.BalanceTagCount;
                    }
                    taggedReservedVolume = taggedTotalCount * avgAmmoVol;
                }
                else
                {
                    MyItemType item = OreIce;
                    float volPerUnit;
                    if (!_balanceVolumeCache.TryGetValue(item, out volPerUnit) || volPerUnit <= 0f)
                    {
                        return 1f;
                    }

                    long supplyUnits = SumGridItemAmount(item);
                    supplyVolume = supplyUnits * volPerUnit;
                    long taggedUnits = 0;
                    foreach (KeyValuePair<IMyTerminalBlock, ContainerEntry> kv in _entryByBlock)
                    {
                        ContainerEntry entry = kv.Value;
                        if (entry == null || entry.ConsumerKind != kind)
                        {
                            continue;
                        }

                        if (entry.BalanceTagCount < 0)
                        {
                            continue;
                        }

                        taggedUnits += entry.BalanceTagCount;
                    }
                    taggedReservedVolume = taggedUnits * volPerUnit;
                }
            }

            return ComputeProportionalFactor(supplyVolume, taggedReservedVolume, untaggedDemandVolume);
        }

        /// <summary>Sums the amount of <paramref name="item"/> across every managed inventory on the grid (containers, consumer blocks, etc.). Used by the proportional-fill factor computation.</summary>
        private long SumGridItemAmount(MyItemType item)
        {
            long total = 0;
            foreach (KeyValuePair<IMyTerminalBlock, ContainerEntry> kv in _entryByBlock)
            {
                ContainerEntry entry = kv.Value;
                if (entry == null || entry.Inventory == null)
                {
                    continue;
                }

                total += GetCurrentAmount(entry.Inventory, item);
            }
            return total;
        }

        /// <summary>Returns the cached total volume of every <c>AmmoMagazine/*</c> stack on the grid, accumulated by <see cref="StepScanInventories"/>. Magazines whose volPerUnit has not yet been measured are excluded (their contribution is unknown), which conservatively understates supply and biases factor downward.</summary>
        private float SumGridAmmoVolume()
        {
            return _gridAmmoVolume;
        }

        /// <summary>Returns the average cached per-unit volume across all <c>AmmoMagazine/*</c> entries in the cache. Used to convert tagged weapon counts to a volume estimate for proportional-fill reservation. Returns <c>0</c> when no ammo volumes have been cached yet.</summary>
        private float AverageCachedAmmoVolPerUnit()
        {
            int count = 0;
            float total = 0f;
            foreach (KeyValuePair<MyItemType, float> kv in _balanceVolumeCache)
            {
                if (kv.Key.TypeId != "MyObjectBuilder_AmmoMagazine")
                {
                    continue;
                }

                if (kv.Value <= 0f)
                {
                    continue;
                }

                total += kv.Value;
                count++;
            }
            return count > 0 ? total / count : 0f;
        }

        /// <summary>Iterates every consumer of the given <paramref name="kind"/>. Tagged blocks (<see cref="ContainerEntry.BalanceTagCount"/> &gt;= 0) use a unit-count target; untagged blocks use the class-wide <paramref name="classPercent"/> if non-zero, otherwise are skipped entirely.</summary>
        private IEnumerator<YieldReason> BalanceConsumersOfKind(ConsumerKind kind, int classActivator)
        {
            float factor = ComputeFactorForKind(kind, classActivator);

            int counter = 0;
            foreach (KeyValuePair<IMyTerminalBlock, ContainerEntry> kv in _entryByBlock)
            {
                ContainerEntry entry = kv.Value;
                if (entry == null || entry.ConsumerKind != kind)
                {
                    continue;
                }

                IMyInventory dst = entry.Inventory;
                if (dst == null)
                {
                    continue;
                }

                if (entry.BalanceTagCount >= 0)
                {
                    BalanceConsumerByCount(entry, kind, dst, entry.BalanceTagCount);
                }
                else if (kind == ConsumerKind.Reactor && classActivator > 0)
                {
                    long natural = ComputeReactorIngotTarget((double)dst.MaxVolume, classActivator);
                    long scaled = (long)Math.Round(natural * (double)factor, MidpointRounding.AwayFromZero);
                    if (scaled > 0)
                    {
                        BalanceConsumerByCount(entry, kind, dst, scaled);
                    }
                }
                else if (classActivator > 0)
                {
                    BalanceConsumerByPercent(entry, kind, dst, classActivator, factor);
                }

                counter++;
                if (counter % 5 == 0)
                {
                    yield return YieldReason.ChunkBoundary;
                }

                if (BudgetExceeded())
                {
                    yield return YieldReason.BudgetHit;
                }
            }
        }

        /// <summary>Fills (or drains) <paramref name="dst"/> to exactly <paramref name="target"/> units of the relevant item type. For weapons, <paramref name="target"/> is the total magazine count across all accepted ammo subtypes.</summary>
        private void BalanceConsumerByCount(ContainerEntry entry, ConsumerKind kind, IMyInventory dst, long target)
        {
            if (kind == ConsumerKind.Weapon)
            {
                BalanceWeaponByMagCount(entry, dst, target);
                return;
            }
            MyItemType item = (kind == ConsumerKind.Reactor) ? IngotUranium : OreIce;
            long current = GetCurrentAmount(dst, item);
            if (current < target)
            {
                PullCountFromAnySource(entry, dst, item, target - current);
            }
            else if (current > target)
            {
                PushCountExcessToCategory(entry, dst, item, current - target);
            }
        }

        /// <summary>Fills (or drains) <paramref name="dst"/> to <paramref name="percent"/>% of its inventory volume with the relevant item type. Pulls and pushes one unit / one magazine at a time, rechecking <see cref="IMyInventory.CurrentVolume"/> after each transfer so the loop is naturally correct for any unit volume (vanilla or modded).</summary>
        private void BalanceConsumerByPercent(ContainerEntry entry, ConsumerKind kind, IMyInventory dst, int percent, float factor)
        {
            float targetVolume = ComputeFillTargetVolume((float)dst.MaxVolume, percent) * factor;
            if (kind == ConsumerKind.Weapon)
            {
                List<MyItemType> ammoList = entry.AcceptedAmmo;
                if (ammoList == null || ammoList.Count == 0)
                {
                    return;
                }

                if ((float)dst.CurrentVolume < targetVolume)
                {
                    PullWeaponAmmoFromAnySource(entry, dst, ammoList, targetVolume);
                }
                if ((float)dst.CurrentVolume > targetVolume)
                {
                    PushWeaponExcessToAmmoCategory(entry, dst, targetVolume);
                }
                return;
            }
            MyItemType item = (kind == ConsumerKind.Reactor) ? IngotUranium : OreIce;
            if ((float)dst.CurrentVolume < targetVolume)
            {
                PullSingleItemByVolume(entry, dst, item, targetVolume);
            }
            if ((float)dst.CurrentVolume > targetVolume)
            {
                PushSingleItemExcessByVolume(entry, dst, item, targetVolume);
            }
        }

        /// <summary>Pulls one unit at a time of <paramref name="item"/> into <paramref name="dst"/> until <see cref="IMyInventory.CurrentVolume"/> reaches <paramref name="targetVolume"/> or no source has more of the item.</summary>
        private void PullSingleItemByVolume(ContainerEntry self, IMyInventory dst, MyItemType item, float targetVolume)
        {
            for (int s = 0; s < _balanceSources.Count; s++)
            {
                if ((float)dst.CurrentVolume >= targetVolume)
                {
                    return;
                }

                if (BudgetExceeded())
                {
                    return;
                }

                ContainerEntry srcEntry = _balanceSources[s];
                if (srcEntry == self)
                {
                    continue;
                }

                BulkPullToTargetVolume(srcEntry.Inventory, dst, item, targetVolume);
            }
        }

        /// <summary>Pushes one unit at a time of <paramref name="item"/> out of <paramref name="dst"/> to category-tagged routes until volume falls under <paramref name="targetVolume"/>. Warns once when no route exists.</summary>
        private void PushSingleItemExcessByVolume(ContainerEntry self, IMyInventory dst, MyItemType item, float targetVolume)
        {
            ItemCategory cat = Classify(item);
            List<ContainerEntry> routes;
            if (!_containersByCategory.TryGetValue(cat, out routes) || routes == null || routes.Count == 0)
            {
                LogWarningOnce("balancer:no-route:" + cat,
                    "[Goose] Balancer cannot push excess " + item.SubtypeId + ": no container tagged " + cat);
                return;
            }
            for (int r = 0; r < routes.Count; r++)
            {
                if ((float)dst.CurrentVolume <= targetVolume)
                {
                    return;
                }

                if (BudgetExceeded())
                {
                    return;
                }

                ContainerEntry route = routes[r];
                if (route == null || route.Block == self.Block)
                {
                    continue;
                }

                IMyInventory rinv = route.Inventory;
                if (rinv == null)
                {
                    continue;
                }

                BulkPushFromExcessByVolume(dst, rinv, item, targetVolume);
            }
        }

        /// <summary>Fills (or drains) a tagged weapon to exactly <paramref name="target"/> total magazines, summing across <see cref="ContainerEntry.AcceptedAmmo"/>. Uses bulk transfer (count-based) since the unit is integer magazines.</summary>
        private void BalanceWeaponByMagCount(ContainerEntry entry, IMyInventory dst, long target)
        {
            List<MyItemType> ammoList = entry.AcceptedAmmo;
            if (ammoList == null || ammoList.Count == 0)
            {
                return;
            }

            long currentMags = 0;
            for (int a = 0; a < ammoList.Count; a++)
            {
                currentMags += GetCurrentAmount(dst, ammoList[a]);
            }

            if (currentMags < target)
            {
                long needed = target - currentMags;
                for (int a = 0; a < ammoList.Count && needed > 0; a++)
                {
                    MyItemType ammo = ammoList[a];
                    foreach (KeyValuePair<IMyTerminalBlock, ContainerEntry> kv in _entryByBlock)
                    {
                        if (needed <= 0)
                        {
                            break;
                        }

                        ContainerEntry srcEntry = kv.Value;
                        if (srcEntry == null || srcEntry == entry)
                        {
                            continue;
                        }

                        IMyInventory srcInv = srcEntry.Inventory;
                        if (srcInv == null)
                        {
                            continue;
                        }

                        long moved = TryMove(srcInv, dst, ammo, needed, "balance");
                        needed -= moved;
                    }
                }
            }
            else if (currentMags > target)
            {
                long excess = currentMags - target;
                List<ContainerEntry> routes;
                if (!_containersByCategory.TryGetValue(ItemCategory.Ammo, out routes) || routes == null || routes.Count == 0)
                {
                    LogWarningOnce("balancer:no-route:Ammo",
                        "[Goose] Balancer cannot push excess ammo: no container tagged Ammo");
                    return;
                }
                _itemBuffer.Clear();
                dst.GetItems(_itemBuffer);
                for (int i = 0; i < _itemBuffer.Count && excess > 0; i++)
                {
                    MyItemType ammoType = _itemBuffer[i].Type;
                    long stackAmount = (long)_itemBuffer[i].Amount;
                    long take = stackAmount < excess ? stackAmount : excess;
                    for (int r = 0; r < routes.Count && take > 0; r++)
                    {
                        ContainerEntry route = routes[r];
                        if (route == null || route.Block == entry.Block)
                        {
                            continue;
                        }

                        IMyInventory rinv = route.Inventory;
                        if (rinv == null)
                        {
                            continue;
                        }

                        long moved = TryMove(dst, rinv, ammoType, take, "balance-excess");
                        take -= moved;
                        excess -= moved;
                    }
                }
            }
        }

        /// <summary>Fills (or drains) every consumer of the given <paramref name="kind"/> with <paramref name="item"/> toward <paramref name="target"/> units. Sources include any entry on the grid that holds the item; cross-consumer transfers are intentional so a hand-loaded reactor's excess feeds the next reactor.</summary>


        /// <summary>Pulls up to <paramref name="needed"/> units of <paramref name="item"/> into <paramref name="dst"/> from any other entry on the grid. Walks <see cref="_entryByBlock"/> in dictionary order; <see cref="TryMove"/> silently no-ops when a candidate source has nothing to give.</summary>
        private void PullCountFromAnySource(ContainerEntry self, IMyInventory dst, MyItemType item, long needed)
        {
            for (int s = 0; s < _balanceSources.Count; s++)
            {
                if (needed <= 0)
                {
                    return;
                }

                ContainerEntry srcEntry = _balanceSources[s];
                if (srcEntry == self)
                {
                    continue;
                }

                long moved = TryMove(srcEntry.Inventory, dst, item, needed, "balance");
                needed -= moved;
            }
        }

        /// <summary>Pushes up to <paramref name="excess"/> units of <paramref name="item"/> out of <paramref name="dst"/> into category-tagged containers for the item's classification. Warns once when no route exists and leaves the excess in place.</summary>
        private void PushCountExcessToCategory(ContainerEntry self, IMyInventory dst, MyItemType item, long excess)
        {
            ItemCategory cat = Classify(item);
            List<ContainerEntry> routes;
            if (!_containersByCategory.TryGetValue(cat, out routes) || routes == null || routes.Count == 0)
            {
                LogWarningOnce("balancer:no-route:" + cat,
                    "[Goose] Balancer cannot push excess " + item.SubtypeId + ": no container tagged " + cat);
                return;
            }
            for (int r = 0; r < routes.Count && excess > 0; r++)
            {
                ContainerEntry route = routes[r];
                if (route == null || route.Block == self.Block)
                {
                    continue;
                }

                IMyInventory rinv = route.Inventory;
                if (rinv == null)
                {
                    continue;
                }

                long moved = TryMove(dst, rinv, item, excess, "balance-excess");
                excess -= moved;
            }
        }

        /// <summary>Fills (or drains) every weapon consumer to within the configured volume percent of its inventory capacity. Pulls and pushes one magazine at a time so the loop is naturally correct for any magazine size, including modded ones, without needing to know per-magazine volume.</summary>


        /// <summary>Pulls one magazine at a time across the cached <paramref name="ammoList"/> until <paramref name="dst"/>'s current volume reaches <paramref name="targetVolume"/> or no source has more compatible ammo.</summary>
        private void PullWeaponAmmoFromAnySource(ContainerEntry self, IMyInventory dst, List<MyItemType> ammoList, float targetVolume)
        {
            for (int a = 0; a < ammoList.Count; a++)
            {
                if ((float)dst.CurrentVolume >= targetVolume)
                {
                    return;
                }

                MyItemType ammo = ammoList[a];
                for (int s = 0; s < _balanceSources.Count; s++)
                {
                    if ((float)dst.CurrentVolume >= targetVolume)
                    {
                        break;
                    }

                    if (BudgetExceeded())
                    {
                        return;
                    }

                    ContainerEntry srcEntry = _balanceSources[s];
                    if (srcEntry == self)
                    {
                        continue;
                    }

                    BulkPullToTargetVolume(srcEntry.Inventory, dst, ammo, targetVolume);
                }
            }
        }

        /// <summary>Pushes one magazine at a time out of <paramref name="dst"/> to any container tagged for the <see cref="ItemCategory.Ammo"/> category until volume falls under <paramref name="targetVolume"/>. Warns once when no route exists.</summary>
        private void PushWeaponExcessToAmmoCategory(ContainerEntry self, IMyInventory dst, float targetVolume)
        {
            List<ContainerEntry> routes;
            if (!_containersByCategory.TryGetValue(ItemCategory.Ammo, out routes) || routes == null || routes.Count == 0)
            {
                LogWarningOnce("balancer:no-route:Ammo",
                    "[Goose] Balancer cannot push excess ammo: no container tagged Ammo");
                return;
            }
            List<MyItemType> ammoList = self.AcceptedAmmo;
            if (ammoList == null || ammoList.Count == 0)
            {
                return;
            }

            for (int a = 0; a < ammoList.Count; a++)
            {
                if ((float)dst.CurrentVolume <= targetVolume)
                {
                    return;
                }

                if (BudgetExceeded())
                {
                    return;
                }

                MyItemType ammoType = ammoList[a];
                for (int r = 0; r < routes.Count; r++)
                {
                    if ((float)dst.CurrentVolume <= targetVolume)
                    {
                        return;
                    }

                    if (BudgetExceeded())
                    {
                        return;
                    }

                    ContainerEntry route = routes[r];
                    if (route == null || route.Block == self.Block)
                    {
                        continue;
                    }

                    IMyInventory rinv = route.Inventory;
                    if (rinv == null)
                    {
                        continue;
                    }

                    BulkPushFromExcessByVolume(dst, rinv, ammoType, targetVolume);
                }
            }
        }
    }
}
