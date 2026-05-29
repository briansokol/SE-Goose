using System.Collections.Generic;
using FluentAssertions;
using IngameScript;
using Xunit;

namespace Shared.Tests
{
    /// <summary>Tests for <see cref="RefineParsing"/>.</summary>
    public class RefineParsingTests
    {
        public class ParseOrderLine_Tests
        {
            [Fact]
            public void Splits_comma_separated_subtypes_in_order()
            {
                List<string> order = RefineParsing.ParseOrderLine("Platinum,Uranium,Gold");
                order.Should().Equal("Platinum", "Uranium", "Gold");
            }

            [Fact]
            public void Trims_whitespace_and_drops_empty_entries()
            {
                List<string> order = RefineParsing.ParseOrderLine(" Iron , , Nickel ,");
                order.Should().Equal("Iron", "Nickel");
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("   ")]
            public void Returns_empty_list_for_blank_input(string raw)
            {
                RefineParsing.ParseOrderLine(raw).Should().BeEmpty();
            }
        }

        public class TryParseThreshold_Tests
        {
            [Theory]
            [InlineData("5000,50000", 5000L, 50000L)]
            [InlineData(" 2000 , 20000 ", 2000L, 20000L)]
            [InlineData("0,0", 0L, 0L)]
            public void Parses_min_max_pairs(string raw, long min, long max)
            {
                RefineThreshold threshold;
                RefineParsing.TryParseThreshold(raw, out threshold).Should().BeTrue();
                threshold.Min.Should().Be(min);
                threshold.Max.Should().Be(max);
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("5000")]
            [InlineData("5000,50000,1")]
            [InlineData("abc,def")]
            [InlineData("-5,10")]
            public void Returns_false_for_malformed_pairs(string raw)
            {
                RefineThreshold threshold;
                RefineParsing.TryParseThreshold(raw, out threshold).Should().BeFalse();
            }
        }
    }
}
