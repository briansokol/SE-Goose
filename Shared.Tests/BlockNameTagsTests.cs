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

    }
}
