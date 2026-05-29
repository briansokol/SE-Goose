using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;
using VRage.ObjectBuilders;
using VRageMath;

namespace Crane.Tests.Fakes
{
    /// <summary>
    /// Functional <see cref="IMyRefinery"/> stand-in. Wires <see cref="InputInventory"/>,
    /// <see cref="OutputInventory"/>, a settable <see cref="UseConveyorSystem"/>,
    /// <see cref="Enabled"/>, <see cref="IsProducing"/>, and <see cref="Closed"/>.
    /// Everything else throws.
    /// </summary>
    internal sealed class FakeRefinery : IMyRefinery
    {
        public IMyInventory InputInventory { get; set; }
        public IMyInventory OutputInventory { get; set; }
        public bool UseConveyorSystem { get; set; }
        public bool Enabled { get; set; }
        public bool IsProducing { get; set; }
        public bool Closed { get; set; }

        public bool IsQueueEmpty { get { throw new NotImplementedException(); } }
        public uint NextItemId { get { throw new NotImplementedException(); } }
        public float CurrentProgress { get { throw new NotImplementedException(); } }
        public bool Repeating { get { throw new NotImplementedException(); } set { throw new NotImplementedException(); } }
        public bool CooperativeMode { get { throw new NotImplementedException(); } set { throw new NotImplementedException(); } }

        public void RequestEnable(bool enable) { throw new NotImplementedException(); }

        public string CustomData { get { throw new NotImplementedException(); } set { throw new NotImplementedException(); } }
        public string CustomInfo { get { throw new NotImplementedException(); } }
        public string CustomName { get { throw new NotImplementedException(); } set { throw new NotImplementedException(); } }
        public string CustomNameWithFaction { get { throw new NotImplementedException(); } }
        public string DetailedInfo { get { throw new NotImplementedException(); } }
        public bool ShowInInventory { get { throw new NotImplementedException(); } set { throw new NotImplementedException(); } }
        public bool ShowInTerminal { get { throw new NotImplementedException(); } set { throw new NotImplementedException(); } }
        public bool ShowInToolbarConfig { get { throw new NotImplementedException(); } set { throw new NotImplementedException(); } }
        public bool ShowOnHUD { get { throw new NotImplementedException(); } set { throw new NotImplementedException(); } }

        public SerializableDefinitionId BlockDefinition { get { throw new NotImplementedException(); } }
        public IMyCubeGrid CubeGrid { get { throw new NotImplementedException(); } }
        public string DefinitionDisplayNameText { get { throw new NotImplementedException(); } }
        public float DisassembleRatio { get { throw new NotImplementedException(); } }
        public string DisplayNameText { get { throw new NotImplementedException(); } }
        public bool IsBeingHacked { get { throw new NotImplementedException(); } }
        public bool IsFunctional { get { throw new NotImplementedException(); } }
        public bool IsWorking { get { throw new NotImplementedException(); } }
        public float Mass { get { throw new NotImplementedException(); } }
        public Vector3I Max { get { throw new NotImplementedException(); } }
        public Vector3I Min { get { throw new NotImplementedException(); } }
        public int NumberInGrid { get { throw new NotImplementedException(); } }
        public MyBlockOrientation Orientation { get { throw new NotImplementedException(); } }
        public long OwnerId { get { throw new NotImplementedException(); } }
        public Vector3I Position { get { throw new NotImplementedException(); } }

        public VRage.Game.Components.Interfaces.IMyEntityComponentContainer Components { get { throw new NotImplementedException(); } }
        public string DisplayName { get { throw new NotImplementedException(); } }
        public long EntityId { get { throw new NotImplementedException(); } }
        public bool HasInventory { get { throw new NotImplementedException(); } }
        public int InventoryCount { get { throw new NotImplementedException(); } }
        public string Name { get { throw new NotImplementedException(); } }
        public BoundingBoxD WorldAABB { get { throw new NotImplementedException(); } }
        public BoundingBoxD WorldAABBHr { get { throw new NotImplementedException(); } }
        public MatrixD WorldMatrix { get { throw new NotImplementedException(); } }
        public BoundingSphereD WorldVolume { get { throw new NotImplementedException(); } }
        public BoundingSphereD WorldVolumeHr { get { throw new NotImplementedException(); } }

        public void GetQueue(List<MyProductionItem> items) { throw new NotImplementedException(); }
        public void AddQueueItem(MyDefinitionId blueprint, MyFixedPoint amount) { throw new NotImplementedException(); }
        public void AddQueueItem(MyDefinitionId blueprint, decimal amount) { throw new NotImplementedException(); }
        public void AddQueueItem(MyDefinitionId blueprint, double amount) { throw new NotImplementedException(); }
        public bool CanUseBlueprint(MyDefinitionId blueprint) { throw new NotImplementedException(); }
        public void ClearQueue() { throw new NotImplementedException(); }
        public void InsertQueueItem(int idx, MyDefinitionId blueprint, MyFixedPoint amount) { throw new NotImplementedException(); }
        public void InsertQueueItem(int idx, MyDefinitionId blueprint, decimal amount) { throw new NotImplementedException(); }
        public void InsertQueueItem(int idx, MyDefinitionId blueprint, double amount) { throw new NotImplementedException(); }
        public void MoveQueueItemRequest(uint queueItemId, int targetIdx) { throw new NotImplementedException(); }
        public void RemoveQueueItem(int idx, MyFixedPoint amount) { throw new NotImplementedException(); }
        public void RemoveQueueItem(int idx, decimal amount) { throw new NotImplementedException(); }
        public void RemoveQueueItem(int idx, double amount) { throw new NotImplementedException(); }

        public void GetActions(List<ITerminalAction> resultList, Func<ITerminalAction, bool> collect = null) { throw new NotImplementedException(); }
        public ITerminalAction GetActionWithName(string name) { throw new NotImplementedException(); }
        public void GetProperties(List<ITerminalProperty> resultList, Func<ITerminalProperty, bool> collect = null) { throw new NotImplementedException(); }
        public ITerminalProperty GetProperty(string id) { throw new NotImplementedException(); }
        public bool HasLocalPlayerAccess() { throw new NotImplementedException(); }
        public bool HasNobodyPlayerAccessToBlock() { throw new NotImplementedException(); }
        public bool HasPlayerAccess(long playerId, MyRelationsBetweenPlayerAndBlock defaultNoUser = MyRelationsBetweenPlayerAndBlock.NoOwnership) { throw new NotImplementedException(); }
        public bool HasPlayerAccessWithNobodyCheck(long playerId, bool defaultIfNobody = false) { throw new NotImplementedException(); }
        public bool IsSameConstructAs(IMyTerminalBlock other) { throw new NotImplementedException(); }
        public void SearchActionsOfName(string name, List<ITerminalAction> resultList, Func<ITerminalAction, bool> collect = null) { throw new NotImplementedException(); }
        public void SetCustomName(string text) { throw new NotImplementedException(); }
        public void SetCustomName(StringBuilder text) { throw new NotImplementedException(); }

        public string GetOwnerFactionTag() { throw new NotImplementedException(); }
        public MyRelationsBetweenPlayerAndBlock GetUserRelationToOwner(long identityId, MyRelationsBetweenPlayerAndBlock defaultRelations = MyRelationsBetweenPlayerAndBlock.NoOwnership) { throw new NotImplementedException(); }
        public MyRelationsBetweenPlayerAndBlock GetPlayerRelationToOwner() { throw new NotImplementedException(); }
        public void UpdateIsWorking() { throw new NotImplementedException(); }
        public void UpdateVisual() { throw new NotImplementedException(); }

        public IMyInventory GetInventory() { throw new NotImplementedException(); }
        public IMyInventory GetInventory(int index) { return index == 0 ? InputInventory : OutputInventory; }
        public Vector3D GetPosition() { throw new NotImplementedException(); }
    }
}
