using FluentAssertions;
using IngameScript;
using Xunit;

namespace Goose.Tests
{
    /// <summary>Tests for the pure helpers in Program.Status.cs.</summary>
    public class StatusTests
    {
        public class ComputeFillPercent_Tests
        {
            [Theory]
            [InlineData(6000.0, 10000.0, 60)]
            [InlineData(0.0, 10000.0, 0)]
            [InlineData(10000.0, 10000.0, 100)]
            [InlineData(1.0, 3.0, 33)]
            [InlineData(2.0, 3.0, 67)]
            public void Returns_rounded_used_over_max_percent(double used, double max, int expected)
            {
                Program.ComputeFillPercent(used, max).Should().Be(expected);
            }

            [Theory]
            [InlineData(5.0, 0.0)]
            [InlineData(5.0, -10.0)]
            public void Returns_zero_when_capacity_is_not_positive(double used, double max)
            {
                Program.ComputeFillPercent(used, max).Should().Be(0);
            }

            [Fact]
            public void Clamps_overfull_to_one_hundred()
            {
                Program.ComputeFillPercent(15000.0, 10000.0).Should().Be(100);
            }
        }

        public class RenderFillBar_Tests
        {
            [Theory]
            [InlineData(60, 10, "[######----]")]
            [InlineData(0, 10, "[----------]")]
            [InlineData(100, 10, "[##########]")]
            [InlineData(32, 10, "[###-------]")]
            public void Renders_proportional_bar(int percent, int width, string expected)
            {
                Program.RenderFillBar(percent, width).Should().Be(expected);
            }
        }

        public class AbbreviateCategory_Tests
        {
            [Theory]
            [InlineData(Program.ItemCategory.Components, "Comps")]
            [InlineData(Program.ItemCategory.Ores, "Ores")]
            [InlineData(Program.ItemCategory.Ingots, "Ingots")]
            [InlineData(Program.ItemCategory.Misc, "Misc")]
            public void Returns_short_label(Program.ItemCategory category, string expected)
            {
                Program.AbbreviateCategory(category).Should().Be(expected);
            }
        }
    }
}
