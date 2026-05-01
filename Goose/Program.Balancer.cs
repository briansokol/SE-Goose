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
        /// <summary>Class of consumer block recognised by the balancer.</summary>
        public enum ConsumerKind {
            /// <summary>Not a consumer; balancer ignores. Also the value used for blocks tagged <c>[NoBalance]</c>.</summary>
            None,
            /// <summary>Reactor (or modded equivalent) that accepts <c>Ingot/Uranium</c>.</summary>
            Reactor,
            /// <summary>O2/H2 generator, irrigation system, or other block that accepts <c>Ore/Ice</c>.</summary>
            Gas,
            /// <summary>Weapon (turret, fixed gun, or modded equivalent) that accepts an <c>AmmoMagazine</c> subtype.</summary>
            Weapon
        }

        // The three balancer item types are exposed as lazy properties rather than
        // static-readonly fields so the test harness can load Program without
        // triggering the SE definition-registry lookup that runs inside the
        // MyItemType ctor (and which is unavailable outside the game runtime).

        static MyItemType? _ingotUraniumCache;

        /// <summary>Reactor fuel type used by the balancer's reactor probe and fill logic.</summary>
        static MyItemType IngotUranium {
            get {
                if (!_ingotUraniumCache.HasValue) _ingotUraniumCache = new MyItemType("MyObjectBuilder_Ingot", "Uranium");
                return _ingotUraniumCache.Value;
            }
        }

        static MyItemType? _oreIceCache;

        /// <summary>Gas-generator and irrigation feedstock used by the balancer's gas probe and fill logic.</summary>
        static MyItemType OreIce {
            get {
                if (!_oreIceCache.HasValue) _oreIceCache = new MyItemType("MyObjectBuilder_Ore", "Ice");
                return _oreIceCache.Value;
            }
        }

        static MyItemType? _componentSteelPlateCache;

        /// <summary>Control item used to reject generic cargo containers from consumer detection. A real cargo container accepts SteelPlate; a reactor or weapon does not.</summary>
        static MyItemType ComponentSteelPlate {
            get {
                if (!_componentSteelPlateCache.HasValue) _componentSteelPlateCache = new MyItemType("MyObjectBuilder_Component", "SteelPlate");
                return _componentSteelPlateCache.Value;
            }
        }

        /// <summary>Computes the absolute volume target (in m^3) for a weapon given its inventory capacity and the configured fill percent.</summary>
        /// <param name="maxVolume">Inventory's maximum volume, in m^3.</param>
        /// <param name="percent">Configured fill percent, 0-100. Caller is expected to clamp before calling.</param>
        /// <returns>Volume in m^3 that the weapon should be filled to.</returns>
        internal static float ComputeFillTargetVolume(float maxVolume, int percent) {
            return maxVolume * (percent / 100f);
        }

        /// <summary>Resolves a <see cref="ConsumerKind"/> from four item-acceptance probe results. Pure helper exposed for unit testing; production code calls <c>IMyInventory.CanItemsBeAdded</c> on the live inventory and feeds the booleans here.</summary>
        /// <param name="canAddIngotUranium">True if the inventory accepts <c>Ingot/Uranium</c>.</param>
        /// <param name="canAddOreIce">True if the inventory accepts <c>Ore/Ice</c>.</param>
        /// <param name="canAddAnyAmmo">True if the inventory accepts at least one known <c>AmmoMagazine</c> subtype.</param>
        /// <param name="canAddSteelPlate">True if the inventory accepts <c>Component/SteelPlate</c>; identifies generic cargo containers.</param>
        /// <returns>The matched <see cref="ConsumerKind"/>, or <see cref="ConsumerKind.None"/> when the block is a generic container or accepts nothing recognised.</returns>
        internal static ConsumerKind IsConsumerKindFromProbes(bool canAddIngotUranium, bool canAddOreIce, bool canAddAnyAmmo, bool canAddSteelPlate) {
            if (canAddSteelPlate) return ConsumerKind.None;
            if (canAddIngotUranium) return ConsumerKind.Reactor;
            if (canAddOreIce) return ConsumerKind.Gas;
            if (canAddAnyAmmo) return ConsumerKind.Weapon;
            return ConsumerKind.None;
        }


        /// <summary>Vanilla ammo magazine subtypes seeded into consumer probing so weapons are detected on a fresh world before any ammo has been observed by the catalog.</summary>
        static readonly string[] VanillaAmmoMagazineSubtypes = new string[] {
            "NATO_25x184mm",
            "NATO_5p56x45mm",
            "Missile200mm",
            "AutocannonClip",
            "MediumCalibreAmmo",
            "LargeCalibreAmmo"
        };

        /// <summary>Reusable scratch list, refreshed each cycle, of ammo magazine types to probe weapon candidates with.</summary>
        readonly List<MyItemType> _ammoCandidates = new List<MyItemType>();

        /// <summary>Reusable scratch set used to dedupe ammo candidate keys.</summary>
        readonly HashSet<string> _ammoCandidateKeys = new HashSet<string>();


        /// <summary>Per-cycle cache of measured per-unit volume (m^3/unit) for items the balancer has transferred this cycle. Lets the bulk-transfer helpers compute exact unit counts from a remaining-volume headroom without iterating one unit at a time. Cleared at the start of every <see cref="StepBalanceConsumers"/> run.</summary>
        readonly Dictionary<MyItemType, float> _balanceVolumeCache = new Dictionary<MyItemType, float>();

        /// <summary>Refreshes <see cref="_ammoCandidates"/> with the vanilla seed list plus every <c>AmmoMagazine/*</c> entry currently in the catalog.</summary>
        void RebuildAmmoCandidateList() {
            _ammoCandidates.Clear();
            _ammoCandidateKeys.Clear();
            for (int i = 0; i < VanillaAmmoMagazineSubtypes.Length; i++) {
                string subtype = VanillaAmmoMagazineSubtypes[i];
                _ammoCandidates.Add(new MyItemType("MyObjectBuilder_AmmoMagazine", subtype));
                _ammoCandidateKeys.Add("AmmoMagazine/" + subtype);
            }
            foreach (var kv in _knownItems) {
                if (kv.Key.StartsWith("AmmoMagazine/", StringComparison.Ordinal)
                    && !_ammoCandidateKeys.Contains(kv.Key)) {
                    _ammoCandidates.Add(kv.Value);
                    _ammoCandidateKeys.Add(kv.Key);
                }
            }
        }

        /// <summary>Walks every managed block, applies the <c>[NoBalance]</c> exclusion gate, then probes acceptance of <see cref="IngotUranium"/>, <see cref="OreIce"/>, and known ammo magazines to assign a <see cref="ConsumerKind"/> to each entry. Caches accepted ammo magazines on weapon entries so the balance step does not re-probe.</summary>
        IEnumerator<YieldReason> StepCategorizeConsumers() {
            RebuildAmmoCandidateList();

            int counter = 0;
            foreach (var kv in _entryByBlock) {
                ContainerEntry entry = kv.Value;
                if (entry == null) continue;
                IMyTerminalBlock block = entry.Block;
                if (block == null) continue;
                IMyInventory inv = entry.Inventory;
                entry.BalanceTagCount = ParseBalanceTagCount(block.CustomName);
                if (inv == null) {
                    entry.ConsumerKind = ConsumerKind.None;
                    entry.AcceptedAmmo = null;
                } else if (NameHasTag(block.CustomName, "[NoBalance]")) {
                    entry.ConsumerKind = ConsumerKind.None;
                    entry.AcceptedAmmo = null;
                } else {
                    ProbeConsumerKind(entry, inv);
                }

                counter++;
                if (counter % 25 == 0) yield return YieldReason.ChunkBoundary;
                if (BudgetExceeded()) yield return YieldReason.BudgetHit;
            }
        }

        /// <summary>Probes a single inventory's item-acceptance and assigns the resulting <see cref="ConsumerKind"/> and (for weapons) <see cref="ContainerEntry.AcceptedAmmo"/> on <paramref name="entry"/>.</summary>
        void ProbeConsumerKind(ContainerEntry entry, IMyInventory inv) {
            entry.ConsumerKind = ConsumerKind.None;
            entry.AcceptedAmmo = null;

            MyFixedPoint epsilon = MyFixedPoint.SmallestPossibleValue;
            bool canSteelPlate = inv.CanItemsBeAdded(epsilon, ComponentSteelPlate);
            if (canSteelPlate) return;

            bool canIngotUranium = inv.CanItemsBeAdded(epsilon, IngotUranium);
            if (canIngotUranium) {
                entry.ConsumerKind = ConsumerKind.Reactor;
                return;
            }

            bool canOreIce = inv.CanItemsBeAdded(epsilon, OreIce);
            if (canOreIce) {
                entry.ConsumerKind = ConsumerKind.Gas;
                return;
            }

            List<MyItemType> accepted = null;
            for (int i = 0; i < _ammoCandidates.Count; i++) {
                MyItemType ammo = _ammoCandidates[i];
                if (inv.CanItemsBeAdded(epsilon, ammo)) {
                    if (accepted == null) accepted = new List<MyItemType>();
                    accepted.Add(ammo);
                }
            }
            if (accepted != null) {
                entry.ConsumerKind = ConsumerKind.Weapon;
                entry.AcceptedAmmo = accepted;
            }
        }


        /// <summary>Top-level balancer step. Runs reactors, then gas, then weapons (so critical-power demand wins under scarcity). Each class is a no-op when its PB target is 0.</summary>
        IEnumerator<YieldReason> StepBalanceConsumers() {
            // Per-block tags ([Balance=N]) bypass the class enable check, so a
            // tagged block runs even when its class percent is 0. The class
            // pass for an untagged block runs only when the class percent is
            // non-zero. Reactors first, then gas, then weapons so critical-
            // power demand wins under scarcity.

            _balanceVolumeCache.Clear();

            IEnumerator<YieldReason> child;

            child = BalanceConsumersOfKind(ConsumerKind.Reactor, _config.ReactorUraniumFillPercent);
            while (child.MoveNext()) yield return child.Current;

            child = BalanceConsumersOfKind(ConsumerKind.Gas, _config.GasIceFillPercent);
            while (child.MoveNext()) yield return child.Current;

            child = BalanceConsumersOfKind(ConsumerKind.Weapon, _config.WeaponAmmoFillPercent);
            while (child.MoveNext()) yield return child.Current;
        }

        /// <summary>Pulls into <paramref name="dst"/> from <paramref name="srcInv"/> using a probe-then-bulk pattern. The first successful transfer for an item type measures the per-unit volume; subsequent calls (and the same call once the cache is populated) compute the remaining headroom in units and pull it in a single <see cref="TryMove"/>. Avoids the O(n) iteration cost of one-unit-at-a-time pulls when the per-unit volume is small (e.g. Ice at ~0.00037 m^3/unit, where 25%% of a 30 m^3 generator is ~20,000 units).</summary>
        void BulkPullToTargetVolume(IMyInventory srcInv, IMyInventory dst, MyItemType item, float targetVolume) {
            if ((float)dst.CurrentVolume >= targetVolume) return;

            float volPerUnit;
            if (!_balanceVolumeCache.TryGetValue(item, out volPerUnit) || volPerUnit <= 0f) {
                float beforeVol = (float)dst.CurrentVolume;
                long moved = TryMove(srcInv, dst, item, 1, "balance");
                if (moved == 0) return;
                float afterVol = (float)dst.CurrentVolume;
                volPerUnit = (afterVol - beforeVol) / moved;
                if (volPerUnit > 0f) _balanceVolumeCache[item] = volPerUnit;
                if ((float)dst.CurrentVolume >= targetVolume) return;
            }

            if (volPerUnit <= 0f) return;

            float headroom = targetVolume - (float)dst.CurrentVolume;
            long unitsToPull = (long)System.Math.Ceiling(headroom / volPerUnit);
            if (unitsToPull <= 0) return;
            TryMove(srcInv, dst, item, unitsToPull, "balance");
        }

        /// <summary>Mirror of <see cref="BulkPullToTargetVolume"/> for excess push: probes once if the cache is cold, then pushes the over-target volume in a single bulk <see cref="TryMove"/>.</summary>
        void BulkPushFromExcessByVolume(IMyInventory dst, IMyInventory rinv, MyItemType item, float targetVolume) {
            if ((float)dst.CurrentVolume <= targetVolume) return;

            float volPerUnit;
            if (!_balanceVolumeCache.TryGetValue(item, out volPerUnit) || volPerUnit <= 0f) {
                float beforeVol = (float)dst.CurrentVolume;
                long moved = TryMove(dst, rinv, item, 1, "balance-excess");
                if (moved == 0) return;
                float afterVol = (float)dst.CurrentVolume;
                volPerUnit = (beforeVol - afterVol) / moved;
                if (volPerUnit > 0f) _balanceVolumeCache[item] = volPerUnit;
                if ((float)dst.CurrentVolume <= targetVolume) return;
            }

            if (volPerUnit <= 0f) return;

            float overage = (float)dst.CurrentVolume - targetVolume;
            long unitsToPush = (long)System.Math.Ceiling(overage / volPerUnit);
            if (unitsToPush <= 0) return;
            TryMove(dst, rinv, item, unitsToPush, "balance-excess");
        }

        /// <summary>Iterates every consumer of the given <paramref name="kind"/>. Tagged blocks (<see cref="ContainerEntry.BalanceTagCount"/> &gt;= 0) use a unit-count target; untagged blocks use the class-wide <paramref name="classPercent"/> if non-zero, otherwise are skipped entirely.</summary>
        IEnumerator<YieldReason> BalanceConsumersOfKind(ConsumerKind kind, int classPercent) {
            int counter = 0;
            foreach (var kv in _entryByBlock) {
                ContainerEntry entry = kv.Value;
                if (entry == null || entry.ConsumerKind != kind) continue;
                IMyInventory dst = entry.Inventory;
                if (dst == null) continue;

                if (entry.BalanceTagCount >= 0) {
                    BalanceConsumerByCount(entry, kind, dst, entry.BalanceTagCount);
                } else if (classPercent > 0) {
                    BalanceConsumerByPercent(entry, kind, dst, classPercent);
                }

                counter++;
                if (counter % 5 == 0) yield return YieldReason.ChunkBoundary;
                if (BudgetExceeded()) yield return YieldReason.BudgetHit;
            }
        }

        /// <summary>Fills (or drains) <paramref name="dst"/> to exactly <paramref name="target"/> units of the relevant item type. For weapons, <paramref name="target"/> is the total magazine count across all accepted ammo subtypes.</summary>
        void BalanceConsumerByCount(ContainerEntry entry, ConsumerKind kind, IMyInventory dst, long target) {
            if (kind == ConsumerKind.Weapon) {
                BalanceWeaponByMagCount(entry, dst, target);
                return;
            }
            MyItemType item = (kind == ConsumerKind.Reactor) ? IngotUranium : OreIce;
            long current = GetCurrentAmount(dst, item);
            if (current < target) {
                PullCountFromAnySource(entry, dst, item, target - current);
            } else if (current > target) {
                PushCountExcessToCategory(entry, dst, item, current - target);
            }
        }

        /// <summary>Fills (or drains) <paramref name="dst"/> to <paramref name="percent"/>% of its inventory volume with the relevant item type. Pulls and pushes one unit / one magazine at a time, rechecking <see cref="IMyInventory.CurrentVolume"/> after each transfer so the loop is naturally correct for any unit volume (vanilla or modded).</summary>
        void BalanceConsumerByPercent(ContainerEntry entry, ConsumerKind kind, IMyInventory dst, int percent) {
            float targetVolume = ComputeFillTargetVolume((float)dst.MaxVolume, percent);
            if (kind == ConsumerKind.Weapon) {
                List<MyItemType> ammoList = entry.AcceptedAmmo;
                if (ammoList == null || ammoList.Count == 0) return;
                if ((float)dst.CurrentVolume < targetVolume) {
                    PullWeaponAmmoFromAnySource(entry, dst, ammoList, targetVolume);
                }
                if ((float)dst.CurrentVolume > targetVolume) {
                    PushWeaponExcessToAmmoCategory(entry, dst, targetVolume);
                }
                return;
            }
            MyItemType item = (kind == ConsumerKind.Reactor) ? IngotUranium : OreIce;
            if ((float)dst.CurrentVolume < targetVolume) {
                PullSingleItemByVolume(entry, dst, item, targetVolume);
            }
            if ((float)dst.CurrentVolume > targetVolume) {
                PushSingleItemExcessByVolume(entry, dst, item, targetVolume);
            }
        }

        /// <summary>Pulls one unit at a time of <paramref name="item"/> into <paramref name="dst"/> until <see cref="IMyInventory.CurrentVolume"/> reaches <paramref name="targetVolume"/> or no source has more of the item.</summary>
        void PullSingleItemByVolume(ContainerEntry self, IMyInventory dst, MyItemType item, float targetVolume) {
            foreach (var kv in _entryByBlock) {
                if ((float)dst.CurrentVolume >= targetVolume) return;
                if (BudgetExceeded()) return;
                ContainerEntry srcEntry = kv.Value;
                if (srcEntry == null || srcEntry == self) continue;
                IMyInventory srcInv = srcEntry.Inventory;
                if (srcInv == null) continue;
                BulkPullToTargetVolume(srcInv, dst, item, targetVolume);
            }
        }

        /// <summary>Pushes one unit at a time of <paramref name="item"/> out of <paramref name="dst"/> to category-tagged routes until volume falls under <paramref name="targetVolume"/>. Warns once when no route exists.</summary>
        void PushSingleItemExcessByVolume(ContainerEntry self, IMyInventory dst, MyItemType item, float targetVolume) {
            ItemCategory cat = Classify(item);
            List<ContainerEntry> routes;
            if (!_containersByCategory.TryGetValue(cat, out routes) || routes == null || routes.Count == 0) {
                LogWarningOnce("balancer:no-route:" + cat,
                    "[Goose] Balancer cannot push excess " + item.SubtypeId + ": no container tagged " + cat);
                return;
            }
            for (int r = 0; r < routes.Count; r++) {
                if ((float)dst.CurrentVolume <= targetVolume) return;
                if (BudgetExceeded()) return;
                ContainerEntry route = routes[r];
                if (route == null || route.Block == self.Block) continue;
                IMyInventory rinv = route.Inventory;
                if (rinv == null) continue;
                BulkPushFromExcessByVolume(dst, rinv, item, targetVolume);
            }
        }

        /// <summary>Fills (or drains) a tagged weapon to exactly <paramref name="target"/> total magazines, summing across <see cref="ContainerEntry.AcceptedAmmo"/>. Uses bulk transfer (count-based) since the unit is integer magazines.</summary>
        void BalanceWeaponByMagCount(ContainerEntry entry, IMyInventory dst, long target) {
            List<MyItemType> ammoList = entry.AcceptedAmmo;
            if (ammoList == null || ammoList.Count == 0) return;

            long currentMags = 0;
            for (int a = 0; a < ammoList.Count; a++) {
                currentMags += GetCurrentAmount(dst, ammoList[a]);
            }

            if (currentMags < target) {
                long needed = target - currentMags;
                for (int a = 0; a < ammoList.Count && needed > 0; a++) {
                    MyItemType ammo = ammoList[a];
                    foreach (var kv in _entryByBlock) {
                        if (needed <= 0) break;
                        ContainerEntry srcEntry = kv.Value;
                        if (srcEntry == null || srcEntry == entry) continue;
                        IMyInventory srcInv = srcEntry.Inventory;
                        if (srcInv == null) continue;
                        long moved = TryMove(srcInv, dst, ammo, needed, "balance");
                        needed -= moved;
                    }
                }
            } else if (currentMags > target) {
                long excess = currentMags - target;
                List<ContainerEntry> routes;
                if (!_containersByCategory.TryGetValue(ItemCategory.Ammo, out routes) || routes == null || routes.Count == 0) {
                    LogWarningOnce("balancer:no-route:Ammo",
                        "[Goose] Balancer cannot push excess ammo: no container tagged Ammo");
                    return;
                }
                _itemBuffer.Clear();
                dst.GetItems(_itemBuffer);
                for (int i = 0; i < _itemBuffer.Count && excess > 0; i++) {
                    MyItemType ammoType = _itemBuffer[i].Type;
                    long stackAmount = (long)_itemBuffer[i].Amount;
                    long take = stackAmount < excess ? stackAmount : excess;
                    for (int r = 0; r < routes.Count && take > 0; r++) {
                        ContainerEntry route = routes[r];
                        if (route == null || route.Block == entry.Block) continue;
                        IMyInventory rinv = route.Inventory;
                        if (rinv == null) continue;
                        long moved = TryMove(dst, rinv, ammoType, take, "balance-excess");
                        take -= moved;
                        excess -= moved;
                    }
                }
            }
        }

        /// <summary>Fills (or drains) every consumer of the given <paramref name="kind"/> with <paramref name="item"/> toward <paramref name="target"/> units. Sources include any entry on the grid that holds the item; cross-consumer transfers are intentional so a hand-loaded reactor's excess feeds the next reactor.</summary>
        

        /// <summary>Pulls up to <paramref name="needed"/> units of <paramref name="item"/> into <paramref name="dst"/> from any other entry on the grid. Walks <see cref="_entryByBlock"/> in dictionary order; <see cref="TryMove"/> silently no-ops when a candidate source has nothing to give.</summary>
        void PullCountFromAnySource(ContainerEntry self, IMyInventory dst, MyItemType item, long needed) {
            foreach (var kv in _entryByBlock) {
                if (needed <= 0) return;
                ContainerEntry srcEntry = kv.Value;
                if (srcEntry == null || srcEntry == self) continue;
                IMyInventory srcInv = srcEntry.Inventory;
                if (srcInv == null) continue;
                long moved = TryMove(srcInv, dst, item, needed, "balance");
                needed -= moved;
            }
        }

        /// <summary>Pushes up to <paramref name="excess"/> units of <paramref name="item"/> out of <paramref name="dst"/> into category-tagged containers for the item's classification. Warns once when no route exists and leaves the excess in place.</summary>
        void PushCountExcessToCategory(ContainerEntry self, IMyInventory dst, MyItemType item, long excess) {
            ItemCategory cat = Classify(item);
            List<ContainerEntry> routes;
            if (!_containersByCategory.TryGetValue(cat, out routes) || routes == null || routes.Count == 0) {
                LogWarningOnce("balancer:no-route:" + cat,
                    "[Goose] Balancer cannot push excess " + item.SubtypeId + ": no container tagged " + cat);
                return;
            }
            for (int r = 0; r < routes.Count && excess > 0; r++) {
                ContainerEntry route = routes[r];
                if (route == null || route.Block == self.Block) continue;
                IMyInventory rinv = route.Inventory;
                if (rinv == null) continue;
                long moved = TryMove(dst, rinv, item, excess, "balance-excess");
                excess -= moved;
            }
        }

        /// <summary>Fills (or drains) every weapon consumer to within the configured volume percent of its inventory capacity. Pulls and pushes one magazine at a time so the loop is naturally correct for any magazine size, including modded ones, without needing to know per-magazine volume.</summary>
        

        /// <summary>Pulls one magazine at a time across the cached <paramref name="ammoList"/> until <paramref name="dst"/>'s current volume reaches <paramref name="targetVolume"/> or no source has more compatible ammo.</summary>
        void PullWeaponAmmoFromAnySource(ContainerEntry self, IMyInventory dst, List<MyItemType> ammoList, float targetVolume) {
            for (int a = 0; a < ammoList.Count; a++) {
                if ((float)dst.CurrentVolume >= targetVolume) return;
                if (BudgetExceeded()) return;
                MyItemType ammo = ammoList[a];
                foreach (var kv in _entryByBlock) {
                    if ((float)dst.CurrentVolume >= targetVolume) break;
                    if (BudgetExceeded()) return;
                    ContainerEntry srcEntry = kv.Value;
                    if (srcEntry == null || srcEntry == self) continue;
                    IMyInventory srcInv = srcEntry.Inventory;
                    if (srcInv == null) continue;
                    BulkPullToTargetVolume(srcInv, dst, ammo, targetVolume);
                }
            }
        }

        /// <summary>Pushes one magazine at a time out of <paramref name="dst"/> to any container tagged for the <see cref="ItemCategory.Ammo"/> category until volume falls under <paramref name="targetVolume"/>. Warns once when no route exists.</summary>
        void PushWeaponExcessToAmmoCategory(ContainerEntry self, IMyInventory dst, float targetVolume) {
            List<ContainerEntry> routes;
            if (!_containersByCategory.TryGetValue(ItemCategory.Ammo, out routes) || routes == null || routes.Count == 0) {
                LogWarningOnce("balancer:no-route:Ammo",
                    "[Goose] Balancer cannot push excess ammo: no container tagged Ammo");
                return;
            }
            List<MyItemType> ammoList = self.AcceptedAmmo;
            if (ammoList == null || ammoList.Count == 0) return;

            for (int a = 0; a < ammoList.Count; a++) {
                if ((float)dst.CurrentVolume <= targetVolume) return;
                if (BudgetExceeded()) return;
                MyItemType ammoType = ammoList[a];
                for (int r = 0; r < routes.Count; r++) {
                    if ((float)dst.CurrentVolume <= targetVolume) return;
                    if (BudgetExceeded()) return;
                    ContainerEntry route = routes[r];
                    if (route == null || route.Block == self.Block) continue;
                    IMyInventory rinv = route.Inventory;
                    if (rinv == null) continue;
                    BulkPushFromExcessByVolume(dst, rinv, ammoType, targetVolume);
                }
            }
        }
    }
}
