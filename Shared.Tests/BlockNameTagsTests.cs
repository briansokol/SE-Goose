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

        public class HasFederateTag_Tests
        {
            [Theory]
            [InlineData("Connector [Federate]", true)]
            [InlineData("Connector [Federate P:0]", true)]
            [InlineData("Connector [Federate P:3]", true)]
            [InlineData("Connector", false)]
            [InlineData("Connector [Federated]", false)]
            [InlineData("", false)]
            [InlineData(null, false)]
            public void Detects_federate_with_or_without_priority(string name, bool expected)
            {
                BlockNameTags.HasFederateTag(name).Should().Be(expected);
            }
        }

        public class ParseFederatePriority_Tests
        {
            [Theory]
            [InlineData("Connector [Federate]", 0)]
            [InlineData("Connector [Federate P:0]", 0)]
            [InlineData("Connector [Federate P:3]", 3)]
            [InlineData("Connector [Federate P:42]", 42)]
            public void Returns_priority_for_federate_tag(string name, int expected)
            {
                BlockNameTags.ParseFederatePriority(name).Should().Be(expected);
            }

            [Theory]
            [InlineData("Connector")]
            [InlineData("Connector [Federated]")]
            [InlineData("")]
            [InlineData(null)]
            public void Returns_negative_one_when_no_federate_tag(string name)
            {
                BlockNameTags.ParseFederatePriority(name).Should().Be(-1);
            }

            [Fact]
            public void Bare_federate_defaults_to_highest_priority_zero()
            {
                BlockNameTags.ParseFederatePriority("[Federate]").Should().Be(0);
            }
        }
    }
}
