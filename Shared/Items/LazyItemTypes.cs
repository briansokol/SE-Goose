using VRage.Game.ModAPI.Ingame;

namespace IngameScript {
    /// <summary>Lazy-initialized <see cref="MyItemType"/> instances for items the balancer/autocraft probes. Lazy because the <see cref="MyItemType"/> constructor performs a definition-registry lookup that fails outside the SE runtime — static-readonly fields explode at type-load in tests.</summary>
    public static class LazyItemTypes {
        static MyItemType? _ingotUraniumCache;

        /// <summary>Reactor fuel type used by reactor balancing.</summary>
        public static MyItemType IngotUranium {
            get {
                if (!_ingotUraniumCache.HasValue) _ingotUraniumCache = new MyItemType("MyObjectBuilder_Ingot", "Uranium");
                return _ingotUraniumCache.Value;
            }
        }

        static MyItemType? _oreIceCache;

        /// <summary>Gas-generator and irrigation feedstock used by gas balancing.</summary>
        public static MyItemType OreIce {
            get {
                if (!_oreIceCache.HasValue) _oreIceCache = new MyItemType("MyObjectBuilder_Ore", "Ice");
                return _oreIceCache.Value;
            }
        }

        static MyItemType? _componentSteelPlateCache;

        /// <summary>Control item used to reject generic cargo containers from consumer detection. A real cargo container accepts SteelPlate; a reactor or weapon does not.</summary>
        public static MyItemType ComponentSteelPlate {
            get {
                if (!_componentSteelPlateCache.HasValue) _componentSteelPlateCache = new MyItemType("MyObjectBuilder_Component", "SteelPlate");
                return _componentSteelPlateCache.Value;
            }
        }
    }
}
