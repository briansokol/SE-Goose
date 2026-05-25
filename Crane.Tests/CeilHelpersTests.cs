using FluentAssertions;
using IngameScript;
using VRage;
using Xunit;

namespace Shared.Tests
{
    /// <summary>Tests for <see cref="CeilHelpers"/>.</summary>
    public class CeilHelpersTests
    {
        [Fact]
        public void Whole_values_pass_through()
        {
            CeilHelpers.CeilToLong((MyFixedPoint)0).Should().Be(0);
            CeilHelpers.CeilToLong((MyFixedPoint)5).Should().Be(5);
            CeilHelpers.CeilToLong((MyFixedPoint)100).Should().Be(100);
        }

        [Fact]
        public void Fractional_rounds_up()
        {
            CeilHelpers.CeilToLong((MyFixedPoint)0.1).Should().Be(1);
            CeilHelpers.CeilToLong((MyFixedPoint)0.9).Should().Be(1);
            CeilHelpers.CeilToLong((MyFixedPoint)1.5).Should().Be(2);
            CeilHelpers.CeilToLong((MyFixedPoint)4.001).Should().Be(5);
        }
    }
}
