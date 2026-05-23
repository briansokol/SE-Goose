using FluentAssertions;
using IngameScript;
using Xunit;

namespace Goose.Tests
{
    /// <summary>Tests for the pure helpers in Program.Config.cs.</summary>
    public class ConfigTests
    {
        public class TryParseQuotaKeyShape_Tests
        {
            [Theory]
            [InlineData("Ingot/Iron", "MyObjectBuilder_Ingot", "Iron")]
            [InlineData("Component/SteelPlate", "MyObjectBuilder_Component", "SteelPlate")]
            [InlineData("Ore/Stone", "MyObjectBuilder_Ore", "Stone")]
            [InlineData("MyObjectBuilder_Ingot/Iron", "MyObjectBuilder_Ingot", "Iron")]
            [InlineData("MyObjectBuilder_AmmoMagazine/NATO_25x184mm", "MyObjectBuilder_AmmoMagazine", "NATO_25x184mm")]
            public void Parses_valid_keys(string key, string expectedType, string expectedSubtype)
            {
                string typeIdWithPrefix, subtypeId;
                Program.TryParseQuotaKeyShape(key, out typeIdWithPrefix, out subtypeId).Should().BeTrue();
                typeIdWithPrefix.Should().Be(expectedType);
                subtypeId.Should().Be(expectedSubtype);
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("Ingot")]
            [InlineData("/Iron")]
            [InlineData("Ingot/")]
            [InlineData("/")]
            public void Returns_false_for_malformed_keys(string key)
            {
                string typeIdWithPrefix, subtypeId;
                Program.TryParseQuotaKeyShape(key, out typeIdWithPrefix, out subtypeId).Should().BeFalse();
            }
        }

        public class TryParseQuotaValue_Tests
        {
            [Theory]
            [InlineData("0", 0L, Program.QuotaMode.Exact)]
            [InlineData("1", 1L, Program.QuotaMode.Exact)]
            [InlineData("100", 100L, Program.QuotaMode.Exact)]
            [InlineData("-5", -5L, Program.QuotaMode.Exact)]
            [InlineData("100M", 100L, Program.QuotaMode.Minimum)]
            [InlineData("100m", 100L, Program.QuotaMode.Minimum)]
            [InlineData("500M", 500L, Program.QuotaMode.Minimum)]
            [InlineData("100L", 100L, Program.QuotaMode.Limiter)]
            [InlineData("100l", 100L, Program.QuotaMode.Limiter)]
            [InlineData("All", 0L, Program.QuotaMode.All)]
            [InlineData("all", 0L, Program.QuotaMode.All)]
            [InlineData("ALL", 0L, Program.QuotaMode.All)]
            public void Parses_valid_values(string raw, long expectedAmount, Program.QuotaMode expectedMode)
            {
                long amount;
                Program.QuotaMode mode;
                Program.TryParseQuotaValue(raw, out amount, out mode).Should().BeTrue();
                amount.Should().Be(expectedAmount);
                mode.Should().Be(expectedMode);
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("abc")]
            [InlineData("M")]
            [InlineData("L")]
            [InlineData("100x")]
            [InlineData("1.5")]
            public void Returns_false_for_malformed_values(string raw)
            {
                long amount;
                Program.QuotaMode mode;
                Program.TryParseQuotaValue(raw, out amount, out mode).Should().BeFalse();
            }
        }



        public class ClampPercent_Tests
        {
            [Theory]
            [InlineData(0, 0)]
            [InlineData(50, 50)]
            [InlineData(100, 100)]
            [InlineData(-1, 0)]
            [InlineData(-1000, 0)]
            [InlineData(101, 100)]
            [InlineData(200, 100)]
            [InlineData(int.MaxValue, 100)]
            [InlineData(int.MinValue, 0)]
            public void Clamps_to_zero_through_one_hundred(int raw, int expected)
            {
                Program.ClampPercent(raw).Should().Be(expected);
            }
        }
    }
}
