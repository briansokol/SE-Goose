using FluentAssertions;
using IngameScript;
using Xunit;

namespace Shared.Tests {
    /// <summary>Tests for <see cref="CatalogKeyHelpers"/>.</summary>
    public class CatalogKeyHelpersTests {
        public class StripObjectBuilderPrefix_Tests {
            [Theory]
            [InlineData("MyObjectBuilder_Component", "Component")]
            [InlineData("MyObjectBuilder_Ingot", "Ingot")]
            [InlineData("MyObjectBuilder_Ore", "Ore")]
            [InlineData("Component", "Component")]
            [InlineData("", "")]
            [InlineData(null, "")]
            public void Strips_or_passes_through(string input, string expected) {
                CatalogKeyHelpers.StripObjectBuilderPrefix(input).Should().Be(expected);
            }
        }
    }
}
