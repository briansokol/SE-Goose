using FluentAssertions;
using IngameScript;
using Xunit;

namespace Shared.Tests {
    /// <summary>Tests for <see cref="QuotaParsing"/>.</summary>
    public class QuotaParsingTests {
        public class TryParseQuotaKeyShape_Tests {
            [Theory]
            [InlineData("Ingot/Iron", "MyObjectBuilder_Ingot", "Iron")]
            [InlineData("Component/SteelPlate", "MyObjectBuilder_Component", "SteelPlate")]
            [InlineData("Ore/Stone", "MyObjectBuilder_Ore", "Stone")]
            [InlineData("MyObjectBuilder_Ingot/Iron", "MyObjectBuilder_Ingot", "Iron")]
            public void Parses_valid_keys(string key, string expectedType, string expectedSubtype) {
                string t, s;
                QuotaParsing.TryParseQuotaKeyShape(key, out t, out s).Should().BeTrue();
                t.Should().Be(expectedType);
                s.Should().Be(expectedSubtype);
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("Ingot")]
            [InlineData("/Iron")]
            [InlineData("Ingot/")]
            [InlineData("/")]
            public void Returns_false_for_malformed_keys(string key) {
                string t, s;
                QuotaParsing.TryParseQuotaKeyShape(key, out t, out s).Should().BeFalse();
            }
        }

        public class TryParseQuotaValue_Tests {
            [Theory]
            [InlineData("100", 100L, QuotaMode.Exact)]
            [InlineData("0", 0L, QuotaMode.Exact)]
            [InlineData("500M", 500L, QuotaMode.Minimum)]
            [InlineData("500m", 500L, QuotaMode.Minimum)]
            [InlineData("250L", 250L, QuotaMode.Limiter)]
            [InlineData("250l", 250L, QuotaMode.Limiter)]
            public void Parses_typed_values(string raw, long amount, QuotaMode mode) {
                long a;
                QuotaMode m;
                QuotaParsing.TryParseQuotaValue(raw, out a, out m).Should().BeTrue();
                a.Should().Be(amount);
                m.Should().Be(mode);
            }

            [Theory]
            [InlineData("All")]
            [InlineData("all")]
            [InlineData("ALL")]
            public void Parses_all_mode(string raw) {
                long a;
                QuotaMode m;
                QuotaParsing.TryParseQuotaValue(raw, out a, out m).Should().BeTrue();
                m.Should().Be(QuotaMode.All);
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("abc")]
            [InlineData("100X")]
            public void Returns_false_on_malformed(string raw) {
                long a;
                QuotaMode m;
                QuotaParsing.TryParseQuotaValue(raw, out a, out m).Should().BeFalse();
            }
        }

        public class ClampPercent_Tests {
            [Theory]
            [InlineData(-5, 0)]
            [InlineData(0, 0)]
            [InlineData(50, 50)]
            [InlineData(100, 100)]
            [InlineData(150, 100)]
            public void Clamps_to_0_to_100(int input, int expected) {
                QuotaParsing.ClampPercent(input).Should().Be(expected);
            }
        }

        public class IsIdentifier_Tests {
            [Theory]
            [InlineData("Foo", true)]
            [InlineData("_bar", true)]
            [InlineData("Item123", true)]
            [InlineData("NATO_25x184mm", true)]
            [InlineData("", false)]
            [InlineData(null, false)]
            [InlineData("1Foo", false)]
            [InlineData("Foo-Bar", false)]
            [InlineData("Foo Bar", false)]
            public void Validates_identifier_syntax(string s, bool expected) {
                QuotaParsing.IsIdentifier(s).Should().Be(expected);
            }
        }

        public class IsValidQuotaKey_Tests {
            [Theory]
            [InlineData("Component/SteelPlate", true)]
            [InlineData("Ingot/Iron", true)]
            [InlineData("AmmoMagazine/NATO_25x184mm", true)]
            [InlineData("", false)]
            [InlineData(null, false)]
            [InlineData("Component", false)]
            [InlineData("/SteelPlate", false)]
            [InlineData("Component/", false)]
            [InlineData("Comp-onent/SteelPlate", false)]
            public void Validates_key_shape(string key, bool expected) {
                QuotaParsing.IsValidQuotaKey(key).Should().Be(expected);
            }
        }
    }
}
