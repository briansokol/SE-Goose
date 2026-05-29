using System.Collections.Generic;
using FluentAssertions;
using IngameScript;
using Xunit;

namespace Crane.Tests
{
    /// <summary>Tests for the pure dynamic-order builder in <c>Crane/Program.Refinery.cs</c>.</summary>
    public class RefineOrderTests
    {
        private const long DefaultMin = 500;
        private const long DefaultMax = 1000;

        private static List<string> Order(params string[] subtypes)
        {
            return new List<string>(subtypes);
        }

        private static List<string> Build(
            List<string> baseOrder,
            Dictionary<string, RefineThreshold> thresholds,
            Dictionary<string, long> ingots,
            Dictionary<string, long> ores,
            HashSet<string> highPriority)
        {
            return Program.BuildDynamicRefineOrder(
                baseOrder, thresholds, ingots, ores, DefaultMin, DefaultMax, highPriority);
        }

        [Fact]
        public void Without_ore_available_nothing_is_bumped_so_base_order_holds()
        {
            List<string> result = Build(
                Order("Platinum", "Gold", "Iron"),
                new Dictionary<string, RefineThreshold>(),
                new Dictionary<string, long>(),
                new Dictionary<string, long>(),
                new HashSet<string>());

            result.Should().Equal("Platinum", "Gold", "Iron");
        }

        [Fact]
        public void Default_threshold_bumps_ore_below_default_min()
        {
            var ingots = new Dictionary<string, long> { { "Gold", 100 } };
            var ores = new Dictionary<string, long> { { "Gold", 5000 } };

            List<string> result = Build(
                Order("Platinum", "Gold", "Iron"),
                new Dictionary<string, RefineThreshold>(),
                ingots, ores, new HashSet<string>());

            result.Should().Equal("Gold", "Platinum", "Iron");
        }

        [Fact]
        public void Explicit_threshold_overrides_default()
        {
            var thresholds = new Dictionary<string, RefineThreshold>
            {
                { "Iron", new RefineThreshold { Min = 5000, Max = 8000 } }
            };
            var ingots = new Dictionary<string, long> { { "Iron", 3000 } };
            var ores = new Dictionary<string, long> { { "Iron", 9000 } };

            List<string> result = Build(
                Order("Platinum", "Gold", "Iron"),
                thresholds, ingots, ores, new HashSet<string>());

            result.Should().Equal("Iron", "Platinum", "Gold");
        }

        [Fact]
        public void Below_min_with_no_ore_is_not_bumped()
        {
            var ingots = new Dictionary<string, long> { { "Iron", 100 } };

            List<string> result = Build(
                Order("Platinum", "Gold", "Iron"),
                new Dictionary<string, RefineThreshold>(),
                ingots, new Dictionary<string, long>(), new HashSet<string>());

            result.Should().Equal("Platinum", "Gold", "Iron");
        }

        [Fact]
        public void Dropping_below_min_marks_high_priority()
        {
            var ingots = new Dictionary<string, long> { { "Iron", 100 } };
            var ores = new Dictionary<string, long> { { "Iron", 5000 } };
            var state = new HashSet<string>();

            Build(Order("Platinum", "Iron"), new Dictionary<string, RefineThreshold>(), ingots, ores, state);

            state.Should().Contain("Iron");
        }

        [Fact]
        public void Middle_band_keeps_previous_high_state_bumped()
        {
            var ingots = new Dictionary<string, long> { { "Iron", 700 } };
            var ores = new Dictionary<string, long> { { "Iron", 5000 } };
            var state = new HashSet<string> { "Iron" };

            List<string> result = Build(
                Order("Platinum", "Iron"),
                new Dictionary<string, RefineThreshold>(),
                ingots, ores, state);

            result.Should().Equal("Iron", "Platinum");
            state.Should().Contain("Iron");
        }

        [Fact]
        public void Middle_band_keeps_previous_normal_state_unbumped()
        {
            var ingots = new Dictionary<string, long> { { "Iron", 700 } };
            var ores = new Dictionary<string, long> { { "Iron", 5000 } };
            var state = new HashSet<string>();

            List<string> result = Build(
                Order("Platinum", "Iron"),
                new Dictionary<string, RefineThreshold>(),
                ingots, ores, state);

            result.Should().Equal("Platinum", "Iron");
            state.Should().NotContain("Iron");
        }

        [Fact]
        public void Reaching_max_reverts_to_normal_priority()
        {
            var ingots = new Dictionary<string, long> { { "Iron", 1000 } };
            var ores = new Dictionary<string, long> { { "Iron", 5000 } };
            var state = new HashSet<string> { "Iron" };

            List<string> result = Build(
                Order("Platinum", "Iron"),
                new Dictionary<string, RefineThreshold>(),
                ingots, ores, state);

            result.Should().Equal("Platinum", "Iron");
            state.Should().NotContain("Iron");
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

            List<string> result = Build(
                Order("Platinum", "Iron"),
                thresholds, ingots, ores, new HashSet<string>());

            result.Should().Equal("Platinum", "Iron");
        }

        [Fact]
        public void Stone_is_exempt_from_threshold_bump()
        {
            var ingots = new Dictionary<string, long>();
            var ores = new Dictionary<string, long> { { "Stone", 9000 }, { "Iron", 9000 } };
            var state = new HashSet<string> { "Stone" };

            List<string> result = Build(
                Order("Stone", "Iron"),
                new Dictionary<string, RefineThreshold>(),
                ingots, ores, state);

            result.Should().Equal("Iron", "Stone");
            state.Should().NotContain("Stone");
        }

        [Fact]
        public void Multiple_bumped_ores_keep_relative_base_order()
        {
            var ingots = new Dictionary<string, long> { { "Gold", 0 }, { "Iron", 0 } };
            var ores = new Dictionary<string, long> { { "Gold", 100 }, { "Iron", 100 } };

            List<string> result = Build(
                Order("Platinum", "Gold", "Iron"),
                new Dictionary<string, RefineThreshold>(),
                ingots, ores, new HashSet<string>());

            result.Should().Equal("Gold", "Iron", "Platinum");
        }
    }
}
