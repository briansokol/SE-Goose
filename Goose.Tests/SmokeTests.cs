using FluentAssertions;
using IngameScript;
using VRage.Game.ModAPI.Ingame;
using Xunit;

namespace Goose.Tests {
    /// <summary>One-shot environmental checks: confirm the SE reference assemblies are usable in tests.</summary>
    public class SmokeTests {
        /// <summary>Verifies <see cref="MyItemType"/> can be produced via a static factory at test runtime.</summary>
        [Fact]
        public void MyItemType_make_ore_works() {
            var type = MyItemType.MakeOre("Iron");
            type.TypeId.Should().Be("MyObjectBuilder_Ore");
            type.SubtypeId.Should().Be("Iron");
        }

        /// <summary>Verifies the static helpers in <see cref="Program"/> are callable without a Program instance.</summary>
        [Fact]
        public void Program_static_helpers_are_callable() {
            Program.ParsePriorityFromName("Cargo [P:5]").Should().Be(5);
            Program.IsIdentifier("MyType_1").Should().BeTrue();
            Program.Catalog_StripObjectBuilderPrefix("MyObjectBuilder_Ore").Should().Be("Ore");
            Program.Catalog_BuildKey(MyItemType.MakeOre("Iron")).Should().Be("Ore/Iron");
            Program.ClassifyByTypeId("MyObjectBuilder_Ore", "Iron").Should().Be(Program.ItemCategory.Ores);
        }
    }
}
