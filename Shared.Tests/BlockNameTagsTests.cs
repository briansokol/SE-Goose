using FluentAssertions;
using IngameScript;
using Xunit;

namespace Shared.Tests
{
    /// <summary>Tests for <see cref="BlockNameTags"/>.</summary>
    public class BlockNameTagsTests
    {
        public class NameHasTag_Tests
        {
            [Theory]
            [InlineData("Container [Stock]", "[Stock]", true)]
            [InlineData("Foo", "[Stock]", false)]
            [InlineData("", "[Stock]", false)]
            [InlineData(null, "[Stock]", false)]
            public void Detects_substring(string name, string tag, bool expected)
            {
                BlockNameTags.NameHasTag(name, tag).Should().Be(expected);
            }
        }

        public class HasIgnoreTag_Tests
        {
            [Theory]
            [InlineData("Container [Ignore]", true)]
            [InlineData("Container [Manual]", true)]
            [InlineData("Container [Locked]", true)]
            [InlineData("Plain Container", false)]
            [InlineData("", false)]
            [InlineData(null, false)]
            public void Detects_any_ignore_variant(string name, bool expected)
            {
                BlockNameTags.HasIgnoreTag(name).Should().Be(expected);
            }
        }

        public class ParsePriorityFromName_Tests
        {
            [Theory]
            [InlineData("Cargo [P:10]", 10)]
            [InlineData("Cargo [P:0]", 0)]
            [InlineData("Cargo [P:200]", 200)]
            [InlineData("Cargo", 100)]
            [InlineData("Cargo [P:abc]", 100)]
            [InlineData("Cargo [P:", 100)]
            [InlineData(null, 100)]
            public void Parses_or_defaults_to_100(string name, int expected)
            {
                BlockNameTags.ParsePriorityFromName(name).Should().Be(expected);
            }
        }

        public class ParseBalanceTagCount_Tests
        {
            [Theory]
            [InlineData("Reactor [Balance=50]", 50L)]
            [InlineData("Reactor [Balance=0]", 0L)]
            [InlineData("Reactor", -1L)]
            [InlineData("Reactor [Balance=-5]", -1L)]
            [InlineData("Reactor [Balance=abc]", -1L)]
            [InlineData(null, -1L)]
            public void Parses_or_returns_neg_one(string name, long expected)
            {
                BlockNameTags.ParseBalanceTagCount(name).Should().Be(expected);
            }
        }

        public class LooksLikeNameTagQuota_Tests
        {
            [Theory]
            [InlineData("Component/SteelPlate:100", true)]
            [InlineData("Stock", false)]
            [InlineData("P:50", false)]
            [InlineData("Foo/Bar", false)]
            [InlineData("", false)]
            [InlineData(null, false)]
            public void Detects_quota_shape(string token, bool expected)
            {
                BlockNameTags.LooksLikeNameTagQuota(token).Should().Be(expected);
            }
        }
    }
}
