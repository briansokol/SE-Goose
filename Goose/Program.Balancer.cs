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
        internal static float ComputeWeaponTargetVolume(float maxVolume, int percent) {
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
    }
}
