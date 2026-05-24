using System;
using System.Collections.Generic;
using Crane.Tests.Fakes;
using FluentAssertions;
using IngameScript;
using Sandbox.ModAPI.Ingame;
using VRage;
using VRage.Game.ModAPI.Ingame;
using Xunit;

namespace Crane.Tests
{
    /// <summary>Tests for the per-assembler feeder helpers in
    /// <c>Crane/Program.AutocraftFeeder.cs</c>.</summary>
    public class AutocraftFeederTests
    {
        private static readonly MyItemType Iron = MyItemType.MakeIngot("Iron");
        private static readonly MyItemType Nickel = MyItemType.MakeIngot("Nickel");
        private static readonly MyItemType SteelPlate = MyItemType.MakeComponent("SteelPlate");
        private static readonly MyItemType InteriorPlate = MyItemType.MakeComponent("InteriorPlate");

        private static List<IMyCargoContainer> CargoListWith(params FakeCargoContainer[] cargos)
        {
            var list = new List<IMyCargoContainer>(cargos.Length);
            for (int i = 0; i < cargos.Length; i++)
            {
                list.Add(cargos[i]);
            }
            return list;
        }

        /// <summary>Assembly-mode Input drain keeps ingots and pushes everything else to cargo.</summary>
        [Fact]
        public void Assembly_drain_input_keeps_ingots_and_drains_components()
        {
            var input = new FakeInventory();
            input.Add(Iron, 100);
            input.Add(SteelPlate, 25);
            var cargo = new FakeCargoContainer();

            Program.DrainInventory(
                input, keepIngots: true, keepSubtypes: null,
                cargoContainers: CargoListWith(cargo),
                itemBuffer: new List<MyInventoryItem>(),
                budgetExceeded: null, debugLog: null);

            input.AmountOf(Iron).Should().Be(100);
            input.AmountOf(SteelPlate).Should().Be(0);
            cargo.Inventory.AmountOf(SteelPlate).Should().Be(25);
            cargo.Inventory.AmountOf(Iron).Should().Be(0);
        }

        /// <summary>Disassembly-mode Output drain keeps the queued subtype, drains everything else.</summary>
        [Fact]
        public void Disassembly_drain_output_keeps_queued_subtypes_and_drains_rest()
        {
            var output = new FakeInventory();
            output.Add(SteelPlate, 10);
            output.Add(InteriorPlate, 20);
            var cargo = new FakeCargoContainer();
            var keep = new HashSet<string>(StringComparer.Ordinal) { "SteelPlate" };

            Program.DrainInventory(
                output, keepIngots: false, keepSubtypes: keep,
                cargoContainers: CargoListWith(cargo),
                itemBuffer: new List<MyInventoryItem>(),
                budgetExceeded: null, debugLog: null);

            output.AmountOf(SteelPlate).Should().Be(10);
            output.AmountOf(InteriorPlate).Should().Be(0);
            cargo.Inventory.AmountOf(InteriorPlate).Should().Be(20);
            cargo.Inventory.AmountOf(SteelPlate).Should().Be(0);
        }

        /// <summary>With an empty queue the feeder drains both Input and Output completely.</summary>
        [Fact]
        public void Empty_queue_drain_clears_both_sides()
        {
            var input = new FakeInventory();
            input.Add(Iron, 50);
            input.Add(Nickel, 30);
            var output = new FakeInventory();
            output.Add(SteelPlate, 15);
            var cargo = new FakeCargoContainer();
            List<IMyCargoContainer> cargos = CargoListWith(cargo);
            var buffer = new List<MyInventoryItem>();

            Program.DrainInventory(input, false, null, cargos, buffer, null, null);
            Program.DrainInventory(output, false, null, cargos, buffer, null, null);

            input.StackCount.Should().Be(0);
            output.StackCount.Should().Be(0);
            cargo.Inventory.AmountOf(Iron).Should().Be(50);
            cargo.Inventory.AmountOf(Nickel).Should().Be(30);
            cargo.Inventory.AmountOf(SteelPlate).Should().Be(15);
        }

        /// <summary>Assembly mode tops the Input up to <c>AssemblerIngotKeep</c> using cargo as source.</summary>
        [Fact]
        public void Assembly_pull_ingot_tops_up_to_target()
        {
            var input = new FakeInventory();
            input.Add(Iron, 10);
            var cargo = new FakeCargoContainer();
            cargo.Inventory.Add(Iron, 200);

            Program.PullItemIntoInventory(
                input, Iron, need: 40,
                cargoContainers: CargoListWith(cargo),
                itemBuffer: new List<MyInventoryItem>(),
                budgetExceeded: null, debugLog: null);

            input.AmountOf(Iron).Should().Be(50);
            cargo.Inventory.AmountOf(Iron).Should().Be(160);
        }

        /// <summary>Disassembly mode pulls the queued component into Output up to the queued amount.</summary>
        [Fact]
        public void Disassembly_pull_queued_component_stages_into_output()
        {
            var output = new FakeInventory();
            var cargo = new FakeCargoContainer();
            cargo.Inventory.Add(InteriorPlate, 50);

            Program.PullItemIntoInventory(
                output, InteriorPlate, need: 10,
                cargoContainers: CargoListWith(cargo),
                itemBuffer: new List<MyInventoryItem>(),
                budgetExceeded: null, debugLog: null);

            output.AmountOf(InteriorPlate).Should().Be(10);
            cargo.Inventory.AmountOf(InteriorPlate).Should().Be(40);
        }
    }
}
