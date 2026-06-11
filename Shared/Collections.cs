using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    // Named subclasses of frequently used generic collection shapes. The MDK2
    // minifier cannot rename BCL generic spellings at declaration sites, but it
    // renames these classes to 1-2 characters, so each use site shrinks in the
    // packed script.

    /// <summary>List of strings.</summary>
    public class StringList : List<string>
    {
        /// <summary>Creates an empty list.</summary>
        public StringList() { }

        /// <summary>Creates a list with the given initial capacity.</summary>
        public StringList(int capacity) : base(capacity) { }
    }

    /// <summary>Set of strings.</summary>
    public class StringSet : HashSet<string>
    {
        /// <summary>Creates an empty set with the default comparer.</summary>
        public StringSet() { }

        /// <summary>Creates an empty set with the given comparer.</summary>
        public StringSet(IEqualityComparer<string> comparer) : base(comparer) { }
    }

    /// <summary>Set of entity/grid ids.</summary>
    public class LongSet : HashSet<long>
    {
        /// <summary>Creates an empty set.</summary>
        public LongSet() { }

        /// <summary>Creates a set seeded with <paramref name="items"/>.</summary>
        public LongSet(IEnumerable<long> items) : base(items) { }
    }

    /// <summary>String-to-string map.</summary>
    public class StringMap : Dictionary<string, string>
    {
        /// <summary>Creates an empty map with the default comparer.</summary>
        public StringMap() { }

        /// <summary>Creates an empty map with the given key comparer.</summary>
        public StringMap(IEqualityComparer<string> comparer) : base(comparer) { }
    }

    /// <summary>Amount keyed by string (e.g. subtype).</summary>
    public class LongByString : Dictionary<string, long>
    {
        /// <summary>Creates an empty map with the default comparer.</summary>
        public LongByString() { }

        /// <summary>Creates an empty map with the given key comparer.</summary>
        public LongByString(IEqualityComparer<string> comparer) : base(comparer) { }
    }

    /// <summary>Count keyed by string.</summary>
    public class IntByString : Dictionary<string, int>
    {
        /// <summary>Creates an empty map with the default comparer.</summary>
        public IntByString() { }

        /// <summary>Creates an empty map with the given key comparer.</summary>
        public IntByString(IEqualityComparer<string> comparer) : base(comparer) { }
    }

    /// <summary>Item type keyed by string.</summary>
    public class ItemTypeByString : Dictionary<string, MyItemType>
    {
        /// <summary>Creates an empty map with the default comparer.</summary>
        public ItemTypeByString() { }

        /// <summary>Creates an empty map with the given key comparer.</summary>
        public ItemTypeByString(IEqualityComparer<string> comparer) : base(comparer) { }
    }

    /// <summary>Amount keyed by item type.</summary>
    public class LongByItemType : Dictionary<MyItemType, long> { }

    /// <summary>List of item types.</summary>
    public class ItemTypeList : List<MyItemType> { }

    /// <summary>List of inventory item stacks.</summary>
    public class InvItemList : List<MyInventoryItem> { }

    /// <summary>List of terminal blocks.</summary>
    public class BlockList : List<IMyTerminalBlock> { }

    /// <summary>List of ship connectors.</summary>
    public class ConnectorList : List<IMyShipConnector> { }

    /// <summary>List of cargo containers.</summary>
    public class CargoList : List<IMyCargoContainer>
    {
        /// <summary>Creates an empty list.</summary>
        public CargoList() { }

        /// <summary>Creates a list with the given initial capacity.</summary>
        public CargoList(int capacity) : base(capacity) { }
    }

    /// <summary>List of text surfaces.</summary>
    public class SurfaceList : List<IMyTextSurface> { }

    /// <summary>List of mechanical connection blocks.</summary>
    public class MechBlockList : List<IMyMechanicalConnectionBlock> { }

    /// <summary>List of assemblers.</summary>
    public class AssemblerList : List<IMyAssembler> { }

    /// <summary>List of connector scope edges.</summary>
    public class ConnectorEdgeList : List<ConnectorEdge> { }

    /// <summary>List of mechanical scope edges.</summary>
    public class MechanicalEdgeList : List<MechanicalEdge> { }

    /// <summary>List of federation peers.</summary>
    public class PeerList : List<FederationPeer> { }
}
