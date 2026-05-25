using System;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;

namespace Goose.Tests.Fakes
{
    /// <summary>
    /// Minimal <see cref="IMyInventory"/> stand-in for unit tests. Every member throws
    /// <see cref="NotImplementedException"/>. The fake is used purely as an identity token:
    /// tests compare returned inventories via reference equality.
    /// </summary>
    internal sealed class FakeInventory : IMyInventory
    {
        public bool CanPutItems { get { throw new NotImplementedException(); } }
        public MyFixedPoint CurrentMass { get { throw new NotImplementedException(); } }
        public MyFixedPoint CurrentVolume { get { throw new NotImplementedException(); } }
        public bool IsFull { get { throw new NotImplementedException(); } }
        public int ItemCount { get { throw new NotImplementedException(); } }
        public MyFixedPoint MaxVolume { get { throw new NotImplementedException(); } }
        public VRage.Game.ModAPI.Ingame.IMyEntity Owner { get { throw new NotImplementedException(); } }
        public float VolumeFillFactor { get { throw new NotImplementedException(); } }

        public bool CanItemsBeAdded(MyFixedPoint amount, MyItemType itemType) { throw new NotImplementedException(); }
        public bool CanTransferItemTo(IMyInventory targetInventory, MyItemType itemType) { throw new NotImplementedException(); }
        public bool ContainItems(MyFixedPoint amount, MyItemType itemType) { throw new NotImplementedException(); }
        public MyInventoryItem? FindItem(MyItemType itemType) { throw new NotImplementedException(); }
        public void GetAcceptedItems(List<MyItemType> items, Func<MyItemType, bool> filter = null) { throw new NotImplementedException(); }
        public MyFixedPoint GetItemAmount(MyItemType itemType) { throw new NotImplementedException(); }
        public MyInventoryItem? GetItemAt(int index) { throw new NotImplementedException(); }
        public MyInventoryItem? GetItemByID(uint id) { throw new NotImplementedException(); }
        public void GetItems(List<MyInventoryItem> items, Func<MyInventoryItem, bool> filter = null) { throw new NotImplementedException(); }
        public bool IsConnectedTo(IMyInventory other) { throw new NotImplementedException(); }
        public bool IsItemAt(int position) { throw new NotImplementedException(); }
        public bool TransferItemFrom(IMyInventory sourceInventory, MyInventoryItem item, MyFixedPoint? amount = null) { throw new NotImplementedException(); }
        public bool TransferItemFrom(IMyInventory sourceInventory, int sourceItemIndex, int? targetItemIndex = null, bool? stackIfPossible = null, MyFixedPoint? amount = null) { throw new NotImplementedException(); }
        public bool TransferItemTo(IMyInventory destination, MyInventoryItem item, MyFixedPoint? amount = null) { throw new NotImplementedException(); }
        public bool TransferItemTo(IMyInventory destination, int sourceItemIndex, int? targetItemIndex = null, bool? stackIfPossible = null, MyFixedPoint? amount = null) { throw new NotImplementedException(); }
    }
}
