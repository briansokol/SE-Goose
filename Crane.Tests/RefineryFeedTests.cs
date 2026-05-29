using System.Collections.Generic;
using Crane.Tests.Fakes;
using FluentAssertions;
using IngameScript;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;
using Xunit;

namespace Crane.Tests
{
    /// <summary>Tests for the static refinery claim/feed helpers in <c>Crane/Program.Refinery.cs</c>.</summary>
    public class RefineryFeedTests
    {
        private static readonly MyItemType GoldOre = MyItemType.MakeOre("Gold");
        private static readonly MyItemType IronOre = MyItemType.MakeOre("Iron");

        private static List<IMyCargoContainer> CargoListWith(params FakeCargoContainer[] cargos)
        {
            var list = new List<IMyCargoContainer>(cargos.Length);
            for (int i = 0; i < cargos.Length; i++)
            {
                list.Add(cargos[i]);
            }
            return list;
        }

        [Fact]
        public void Claim_turns_off_conveyor_on_all_managed_refineries()
        {
            var r1 = new FakeRefinery { UseConveyorSystem = true };
            var r2 = new FakeRefinery { UseConveyorSystem = false };

            Program.ClaimRefineryInputs(new List<IMyRefinery> { r1, r2 });

            r1.UseConveyorSystem.Should().BeFalse();
            r2.UseConveyorSystem.Should().BeFalse();
        }

        [Fact]
        public void Feeds_highest_priority_available_ore_first_and_stops_at_target()
        {
            var input = new FakeInventory { UnitVolume = 1.0, MaxVolumeValue = 1000.0 };
            var refinery = new FakeRefinery { InputInventory = input, OutputInventory = new FakeInventory(), Enabled = true };
            var cargo = new FakeCargoContainer();
            cargo.Inventory.Add(GoldOre, 10000);
            cargo.Inventory.Add(IronOre, 10000);
            var oreTotals = new Dictionary<string, long> { { "Gold", 10000 }, { "Iron", 10000 } };

            Program.TopUpRefinery(
                refinery, new List<string> { "Gold", "Iron" }, 0.5, oreTotals,
                CargoListWith(cargo), new List<MyInventoryItem>(), null, null);

            input.AmountOf(GoldOre).Should().BeGreaterThan(0);
            input.AmountOf(IronOre).Should().Be(0);
            input.VolumeFillFactor.Should().BeGreaterThanOrEqualTo(0.5f);
        }

        [Fact]
        public void Does_not_feed_when_input_already_at_target()
        {
            var input = new FakeInventory { UnitVolume = 1.0, MaxVolumeValue = 1000.0 };
            input.Add(IronOre, 600);
            var refinery = new FakeRefinery { InputInventory = input, OutputInventory = new FakeInventory(), Enabled = true };
            var cargo = new FakeCargoContainer();
            cargo.Inventory.Add(IronOre, 5000);
            var oreTotals = new Dictionary<string, long> { { "Iron", 5000 } };

            Program.TopUpRefinery(
                refinery, new List<string> { "Iron" }, 0.5, oreTotals,
                CargoListWith(cargo), new List<MyInventoryItem>(), null, null);

            input.AmountOf(IronOre).Should().Be(600);
        }

        [Fact]
        public void Skips_ore_with_no_grid_supply()
        {
            var input = new FakeInventory { UnitVolume = 1.0, MaxVolumeValue = 1000.0 };
            var refinery = new FakeRefinery { InputInventory = input, OutputInventory = new FakeInventory(), Enabled = true };
            var cargo = new FakeCargoContainer();
            cargo.Inventory.Add(IronOre, 5000);
            var oreTotals = new Dictionary<string, long> { { "Iron", 5000 } };

            Program.TopUpRefinery(
                refinery, new List<string> { "Gold", "Iron" }, 0.5, oreTotals,
                CargoListWith(cargo), new List<MyInventoryItem>(), null, null);

            input.AmountOf(GoldOre).Should().Be(0);
            input.AmountOf(IronOre).Should().BeGreaterThan(0);
        }
    }
}
