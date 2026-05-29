using System;
using System.Collections.Generic;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;

namespace Crane.Tests.Fakes
{
    /// <summary>
    /// Functional <see cref="IMyInventory"/> stand-in. Backs items in a list, supports
    /// <see cref="GetItems"/>, <see cref="CanTransferItemTo"/>, <see cref="CanItemsBeAdded"/>,
    /// and the index-style <see cref="TransferItemTo"/>. Everything else throws.
    /// Tests construct it via <see cref="Add"/> and inspect with <see cref="AmountOf"/>.
    /// </summary>
    internal sealed class FakeInventory : IMyInventory
    {
        private readonly List<MyInventoryItem> _items = new List<MyInventoryItem>();
        private uint _nextId = 1;

        /// <summary>True when the inventory should refuse all incoming items (capacity-full simulation).</summary>
        public bool Full = false;

        /// <summary>Volume occupied per item unit, used to derive <see cref="CurrentVolume"/> and <see cref="VolumeFillFactor"/>. 0 disables volume modeling.</summary>
        public double UnitVolume = 0.0;

        /// <summary>Total volume capacity backing <see cref="MaxVolume"/>.</summary>
        public double MaxVolumeValue = 1_000_000.0;

        /// <summary>Adds <paramref name="amount"/> units of <paramref name="type"/> as a single stack.</summary>
        public void Add(MyItemType type, long amount)
        {
            _items.Add(new MyInventoryItem(type, _nextId++, (MyFixedPoint)(double)amount));
        }

        /// <summary>Sums every stack of <paramref name="type"/> currently present.</summary>
        public long AmountOf(MyItemType type)
        {
            long sum = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Type == type)
                {
                    sum += (long)_items[i].Amount;
                }
            }
            return sum;
        }

        /// <summary>Count of distinct stacks (handy for asserting "everything drained").</summary>
        public int StackCount { get { return _items.Count; } }

        public void GetItems(List<MyInventoryItem> items, Func<MyInventoryItem, bool> filter = null)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                items.Add(_items[i]);
            }
        }

        public bool CanTransferItemTo(IMyInventory targetInventory, MyItemType itemType)
        {
            return true;
        }

        public bool CanItemsBeAdded(MyFixedPoint amount, MyItemType itemType)
        {
            return !Full;
        }

        public bool TransferItemTo(IMyInventory destination, int sourceItemIndex, int? targetItemIndex = null, bool? stackIfPossible = null, MyFixedPoint? amount = null)
        {
            if (sourceItemIndex < 0 || sourceItemIndex >= _items.Count)
            {
                return false;
            }
            var dst = destination as FakeInventory;
            if (dst == null || dst == this)
            {
                return false;
            }
            MyInventoryItem src = _items[sourceItemIndex];
            MyFixedPoint moveAmount = amount.HasValue ? amount.Value : src.Amount;
            if (moveAmount > src.Amount)
            {
                moveAmount = src.Amount;
            }
            if (dst.Full)
            {
                return false;
            }
            if (moveAmount >= src.Amount)
            {
                _items.RemoveAt(sourceItemIndex);
            }
            else
            {
                _items[sourceItemIndex] = new MyInventoryItem(src.Type, src.ItemId, src.Amount - moveAmount);
            }
            dst._items.Add(new MyInventoryItem(src.Type, dst._nextId++, moveAmount));
            return true;
        }

        /// <summary>Sums every stack's amount across all item types.</summary>
        private long TotalItemAmount()
        {
            long sum = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                sum += (long)_items[i].Amount;
            }
            return sum;
        }

        public bool CanPutItems { get { throw new NotImplementedException(); } }
        public MyFixedPoint CurrentMass { get { throw new NotImplementedException(); } }
        public MyFixedPoint CurrentVolume { get { return (MyFixedPoint)(TotalItemAmount() * UnitVolume); } }
        public bool IsFull { get { throw new NotImplementedException(); } }
        public int ItemCount { get { throw new NotImplementedException(); } }
        public MyFixedPoint MaxVolume { get { return (MyFixedPoint)MaxVolumeValue; } }
        public VRage.Game.ModAPI.Ingame.IMyEntity Owner { get { throw new NotImplementedException(); } }
        public float VolumeFillFactor { get { return MaxVolumeValue <= 0 ? 1f : (float)(TotalItemAmount() * UnitVolume / MaxVolumeValue); } }

        public bool ContainItems(MyFixedPoint amount, MyItemType itemType) { throw new NotImplementedException(); }
        public MyInventoryItem? FindItem(MyItemType itemType) { throw new NotImplementedException(); }
        public void GetAcceptedItems(List<MyItemType> items, Func<MyItemType, bool> filter = null) { throw new NotImplementedException(); }
        public MyFixedPoint GetItemAmount(MyItemType itemType) { throw new NotImplementedException(); }
        public MyInventoryItem? GetItemAt(int index) { throw new NotImplementedException(); }
        public MyInventoryItem? GetItemByID(uint id) { throw new NotImplementedException(); }
        public bool IsConnectedTo(IMyInventory other) { throw new NotImplementedException(); }
        public bool IsItemAt(int position) { throw new NotImplementedException(); }
        public bool TransferItemFrom(IMyInventory sourceInventory, MyInventoryItem item, MyFixedPoint? amount = null) { throw new NotImplementedException(); }
        public bool TransferItemFrom(IMyInventory sourceInventory, int sourceItemIndex, int? targetItemIndex = null, bool? stackIfPossible = null, MyFixedPoint? amount = null) { throw new NotImplementedException(); }
        public bool TransferItemTo(IMyInventory destination, MyInventoryItem item, MyFixedPoint? amount = null) { throw new NotImplementedException(); }
    }
}
