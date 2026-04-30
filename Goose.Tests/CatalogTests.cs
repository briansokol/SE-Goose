using FluentAssertions;
using IngameScript;
using VRage.Game.ModAPI.Ingame;
using Xunit;

namespace Goose.Tests {
    /// <summary>Tests for the pure helpers in Program.Catalog.cs.</summary>
    public class CatalogTests {
        public class StripObjectBuilderPrefix_Tests {
            [Theory]
            [InlineData("MyObjectBuilder_Ore", "Ore")]
            [InlineData("MyObjectBuilder_Ingot", "Ingot")]
            [InlineData("MyObjectBuilder_Component", "Component")]
            [InlineData("MyObjectBuilder_AmmoMagazine", "AmmoMagazine")]
            public void Strips_prefix_when_present(string input, string expected) {
                Program.Catalog_StripObjectBuilderPrefix(input).Should().Be(expected);
            }

            [Theory]
            [InlineData("Ore", "Ore")]
            [InlineData("RandomString", "RandomString")]
            [InlineData("MyObject_NotAPrefix", "MyObject_NotAPrefix")]
            [InlineData("myobjectbuilder_lowercase", "myobjectbuilder_lowercase")]
            public void Passes_through_when_prefix_absent(string input, string expected) {
                Program.Catalog_StripObjectBuilderPrefix(input).Should().Be(expected);
            }

            [Theory]
            [InlineData(null, "")]
            [InlineData("", "")]
            public void Returns_empty_for_null_or_empty(string input, string expected) {
                Program.Catalog_StripObjectBuilderPrefix(input).Should().Be(expected);
            }

            [Fact]
            public void Strips_only_the_prefix_not_subsequent_occurrences() {
                Program.Catalog_StripObjectBuilderPrefix("MyObjectBuilder_MyObjectBuilder_Foo")
                    .Should().Be("MyObjectBuilder_Foo");
            }
        }

        public class BuildKey_Tests {
            [Fact]
            public void Builds_type_slash_subtype_for_ore() {
                var key = Program.Catalog_BuildKey(MyItemType.MakeOre("Iron"));
                key.Should().Be("Ore/Iron");
            }

            [Fact]
            public void Builds_type_slash_subtype_for_ingot() {
                var key = Program.Catalog_BuildKey(MyItemType.MakeIngot("Gold"));
                key.Should().Be("Ingot/Gold");
            }

            [Fact]
            public void Builds_type_slash_subtype_for_component() {
                var key = Program.Catalog_BuildKey(MyItemType.MakeComponent("SteelPlate"));
                key.Should().Be("Component/SteelPlate");
            }

            [Fact]
            public void Builds_type_slash_subtype_for_ammo() {
                var key = Program.Catalog_BuildKey(MyItemType.MakeAmmo("NATO_25x184mm"));
                key.Should().Be("AmmoMagazine/NATO_25x184mm");
            }
        }
    }
}
