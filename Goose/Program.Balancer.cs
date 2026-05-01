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

        /// <summary>Reactor fuel type used by the balancer's reactor probe and fill logic.</summary>
        static readonly MyItemType IngotUranium = new MyItemType("MyObjectBuilder_Ingot", "Uranium");

        /// <summary>Gas-generator and irrigation feedstock used by the balancer's gas probe and fill logic.</summary>
        static readonly MyItemType OreIce = new MyItemType("MyObjectBuilder_Ore", "Ice");

        /// <summary>Control item used to reject generic cargo containers from consumer detection. A real cargo container accepts SteelPlate; a reactor or weapon does not.</summary>
        static readonly MyItemType ComponentSteelPlate = new MyItemType("MyObjectBuilder_Component", "SteelPlate");

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
    }
}
