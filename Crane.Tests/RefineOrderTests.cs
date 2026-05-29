using System.Collections.Generic;
using FluentAssertions;
using IngameScript;
using Xunit;

namespace Crane.Tests
{
    /// <summary>Tests for the pure dynamic-order builder in <c>Crane/Program.Refinery.cs</c>.</summary>
    public class RefineOrderTests
    {
        private static List<string> Order(params string[] subtypes)
        {
            return new List<string>(subtypes);
        }

        [Fact]
        public void No_thresholds_returns_base_order_unchanged()
        {
            List<string> result = Program.BuildDynamicRefineOrder(
                Order("Platinum", "Gold", "Iron"),
                new Dictionary<string, RefineThreshold>(),
                new Dictionary<string, long>(),
                new Dictionary<string, long>());

            result.Should().Equal("Platinum", "Gold", "Iron");
        }

        [Fact]
        public void Ingot_below_min_with_ore_available_is_bumped_to_front()
        {
            var thresholds = new Dictionary<string, RefineThreshold>
            {
                { "Iron", new RefineThreshold { Min = 5000, Max = 0 } }
            };
            var ingots = new Dictionary<string, long> { { "Iron", 100 } };
            var ores = new Dictionary<string, long> { { "Iron", 9000 } };

            List<string> result = Program.BuildDynamicRefineOrder(
                Order("Platinum", "Gold", "Iron"), thresholds, ingots, ores);

            result.Should().Equal("Iron", "Platinum", "Gold");
        }

        [Fact]
        public void Below_min_but_no_ore_available_is_not_bumped()
        {
            var thresholds = new Dictionary<string, RefineThreshold>
            {
                { "Iron", new RefineThreshold { Min = 5000, Max = 0 } }
            };
            var ingots = new Dictionary<string, long> { { "Iron", 100 } };
            var ores = new Dictionary<string, long>();

            List<string> result = Program.BuildDynamicRefineOrder(
                Order("Platinum", "Gold", "Iron"), thresholds, ingots, ores);

            result.Should().Equal("Platinum", "Gold", "Iron");
        }

        [Fact]
        public void Ingot_at_or_above_max_is_dropped()
        {
            var thresholds = new Dictionary<string, RefineThreshold>
            {
                { "Gold", new RefineThreshold { Min = 0, Max = 2000 } }
            };
            var ingots = new Dictionary<string, long> { { "Gold", 2000 } };
            var ores = new Dictionary<string, long> { { "Gold", 500 } };

            List<string> result = Program.BuildDynamicRefineOrder(
                Order("Platinum", "Gold", "Iron"), thresholds, ingots, ores);

            result.Should().Equal("Platinum", "Iron");
        }

        [Fact]
        public void Multiple_bumped_ores_keep_relative_base_order()
        {
            var thresholds = new Dictionary<string, RefineThreshold>
            {
                { "Gold", new RefineThreshold { Min = 1000, Max = 0 } },
                { "Iron", new RefineThreshold { Min = 5000, Max = 0 } }
            };
            var ingots = new Dictionary<string, long> { { "Gold", 0 }, { "Iron", 0 } };
            var ores = new Dictionary<string, long> { { "Gold", 100 }, { "Iron", 100 } };

            List<string> result = Program.BuildDynamicRefineOrder(
                Order("Platinum", "Gold", "Iron"), thresholds, ingots, ores);

            result.Should().Equal("Gold", "Iron", "Platinum");
        }

        [Fact]
        public void Zero_min_and_zero_max_are_inert()
        {
            var thresholds = new Dictionary<string, RefineThreshold>
            {
                { "Iron", new RefineThreshold { Min = 0, Max = 0 } }
            };
            var ingots = new Dictionary<string, long> { { "Iron", 0 } };
            var ores = new Dictionary<string, long> { { "Iron", 9000 } };

            List<string> result = Program.BuildDynamicRefineOrder(
                Order("Platinum", "Iron"), thresholds, ingots, ores);

            result.Should().Equal("Platinum", "Iron");
        }
    }
}
