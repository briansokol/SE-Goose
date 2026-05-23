using FluentAssertions;
using IngameScript;
using Xunit;

namespace Shared.Tests
{
    /// <summary>Tests for <see cref="AutocraftQuotaParsing"/>.</summary>
    public class AutocraftQuotaParsingTests
    {
        [Theory]
        [InlineData("x")]
        [InlineData("X")]
        public void X_sentinel_marks_ignore(string raw)
        {
            long t;
            AutocraftMode m;
            bool ignore;
            AutocraftQuotaParsing.TryParseAutocraftQuotaValue(raw, out t, out m, out ignore).Should().BeTrue();
            ignore.Should().BeTrue();
            t.Should().Be(0);
        }

        [Theory]
        [InlineData("100", 100L, AutocraftMode.Minimum)]
        [InlineData("0", 0L, AutocraftMode.Minimum)]
        [InlineData("500", 500L, AutocraftMode.Minimum)]
        public void Bare_number_is_minimum(string raw, long amount, AutocraftMode mode)
        {
            long t;
            AutocraftMode m;
            bool ignore;
            AutocraftQuotaParsing.TryParseAutocraftQuotaValue(raw, out t, out m, out ignore).Should().BeTrue();
            ignore.Should().BeFalse();
            t.Should().Be(amount);
            m.Should().Be(mode);
        }

        [Theory]
        [InlineData("200E", 200L)]
        [InlineData("200e", 200L)]
        public void E_suffix_is_exact(string raw, long amount)
        {
            long t;
            AutocraftMode m;
            bool ignore;
            AutocraftQuotaParsing.TryParseAutocraftQuotaValue(raw, out t, out m, out ignore).Should().BeTrue();
            ignore.Should().BeFalse();
            t.Should().Be(amount);
            m.Should().Be(AutocraftMode.Exact);
        }

        [Theory]
        [InlineData("150L")]
        [InlineData("150l")]
        public void L_silently_aliases_to_exact(string raw)
        {
            long t;
            AutocraftMode m;
            bool ignore;
            AutocraftQuotaParsing.TryParseAutocraftQuotaValue(raw, out t, out m, out ignore).Should().BeTrue();
            ignore.Should().BeFalse();
            t.Should().Be(150);
            m.Should().Be(AutocraftMode.Exact);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("100M")]
        [InlineData("abc")]
        public void Returns_false_on_malformed(string raw)
        {
            long t;
            AutocraftMode m;
            bool ignore;
            AutocraftQuotaParsing.TryParseAutocraftQuotaValue(raw, out t, out m, out ignore).Should().BeFalse();
        }
    }
}
