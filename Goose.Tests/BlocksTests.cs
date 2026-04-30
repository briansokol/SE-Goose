using FluentAssertions;
using IngameScript;
using Xunit;

namespace Goose.Tests {
    /// <summary>Tests for the pure helpers in Program.Blocks.cs.</summary>
    public class BlocksTests {
        public class ParsePriorityFromName_Tests {
            [Theory]
            [InlineData(null, 100)]
            [InlineData("", 100)]
            [InlineData("Cargo", 100)]
            [InlineData("Cargo [P:1]", 1)]
            [InlineData("Cargo [P:5]", 5)]
            [InlineData("Cargo [P:50]", 50)]
            [InlineData("Cargo [P:0]", 0)]
            [InlineData("Cargo [P:-3]", -3)]
            [InlineData("Cargo [P:9999]", 9999)]
            [InlineData("[P:7] Cargo", 7)]
            [InlineData("Cargo [P:7] [Stock]", 7)]
            public void Returns_parsed_priority_or_default(string name, int expected) {
                Program.ParsePriorityFromName(name).Should().Be(expected);
            }

            [Theory]
            [InlineData("Cargo [P:abc]")]
            [InlineData("Cargo [P:]")]
            [InlineData("Cargo [P:")]
            [InlineData("Cargo P:5")]
            [InlineData("Cargo [Q:5]")]
            public void Returns_default_for_malformed_tags(string name) {
                Program.ParsePriorityFromName(name).Should().Be(100);
            }

            [Fact]
            public void First_well_formed_tag_wins_when_multiple_present() {
                Program.ParsePriorityFromName("Cargo [P:3] [P:9]").Should().Be(3);
            }
        }

        public class IsIdentifier_Tests {
            [Theory]
            [InlineData("Foo")]
            [InlineData("foo")]
            [InlineData("_Foo")]
            [InlineData("Foo_Bar")]
            [InlineData("Foo123")]
            [InlineData("F")]
            [InlineData("_")]
            [InlineData("MyObjectBuilder_Ore")]
            public void Accepts_valid_identifiers(string s) {
                Program.IsIdentifier(s).Should().BeTrue();
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("1Foo")]
            [InlineData("9")]
            [InlineData("Foo Bar")]
            [InlineData("Foo-Bar")]
            [InlineData("Foo.Bar")]
            [InlineData("Foo/Bar")]
            [InlineData(" Foo")]
            [InlineData("Foo!")]
            public void Rejects_invalid_identifiers(string s) {
                Program.IsIdentifier(s).Should().BeFalse();
            }
        }

        public class ClassifyByTypeId_Tests {
            [Theory]
            [InlineData("MyObjectBuilder_Ore", "Iron", Program.ItemCategory.Ores)]
            [InlineData("MyObjectBuilder_Ore", "Stone", Program.ItemCategory.Ores)]
            [InlineData("MyObjectBuilder_Ingot", "Iron", Program.ItemCategory.Ingots)]
            [InlineData("MyObjectBuilder_Ingot", "Gold", Program.ItemCategory.Ingots)]
            [InlineData("MyObjectBuilder_AmmoMagazine", "NATO_25x184mm", Program.ItemCategory.Ammo)]
            [InlineData("MyObjectBuilder_AmmoMagazine", "Missile200mm", Program.ItemCategory.Ammo)]
            [InlineData("MyObjectBuilder_Datapad", "Datapad", Program.ItemCategory.Misc)]
            public void Classifies_simple_types(string typeId, string subId, Program.ItemCategory expected) {
                Program.ClassifyByTypeId(typeId, subId).Should().Be(expected);
            }

            [Theory]
            [InlineData("SteelPlate", Program.ItemCategory.Components)]
            [InlineData("Construction", Program.ItemCategory.Components)]
            [InlineData("Computer", Program.ItemCategory.Components)]
            [InlineData("PrototechCapacitor", Program.ItemCategory.Prototech)]
            [InlineData("PrototechFrame", Program.ItemCategory.Prototech)]
            [InlineData("PrototechCircuitry", Program.ItemCategory.Prototech)]
            [InlineData("PrototechSomethingNew", Program.ItemCategory.Prototech)]
            public void Classifies_components_and_prototech(string subId, Program.ItemCategory expected) {
                Program.ClassifyByTypeId("MyObjectBuilder_Component", subId).Should().Be(expected);
            }

            [Theory]
            [InlineData("Welder", Program.ItemCategory.Tools)]
            [InlineData("Welder2Item", Program.ItemCategory.Tools)]
            [InlineData("AngleGrinderItem", Program.ItemCategory.Tools)]
            [InlineData("HandDrillItem", Program.ItemCategory.Tools)]
            [InlineData("AutomaticRifleItem", Program.ItemCategory.Weapons)]
            [InlineData("SemiAutoPistolItem", Program.ItemCategory.Weapons)]
            [InlineData("BasicHandHeldLauncherItem", Program.ItemCategory.Weapons)]
            [InlineData("UnknownGunSubtype", Program.ItemCategory.Weapons)]
            public void Classifies_physical_gun_objects(string subId, Program.ItemCategory expected) {
                Program.ClassifyByTypeId("MyObjectBuilder_PhysicalGunObject", subId).Should().Be(expected);
            }

            [Theory]
            [InlineData("MyObjectBuilder_OxygenContainerObject", "OxygenBottle", Program.ItemCategory.Tools)]
            [InlineData("MyObjectBuilder_GasContainerObject", "HydrogenBottle", Program.ItemCategory.Tools)]
            public void Classifies_gas_and_oxygen_containers(string typeId, string subId, Program.ItemCategory expected) {
                Program.ClassifyByTypeId(typeId, subId).Should().Be(expected);
            }

            [Theory]
            [InlineData("Ingredient_Wheat", Program.ItemCategory.Ingredients)]
            [InlineData("WheatIngredient", Program.ItemCategory.Ingredients)]
            [InlineData("Meal_Stew", Program.ItemCategory.Meals)]
            [InlineData("BeefMeal", Program.ItemCategory.Meals)]
            [InlineData("MedicalDose", Program.ItemCategory.Consumables)]
            [InlineData("RandomConsumable", Program.ItemCategory.Consumables)]
            public void Classifies_consumable_items(string subId, Program.ItemCategory expected) {
                Program.ClassifyByTypeId("MyObjectBuilder_ConsumableItem", subId).Should().Be(expected);
            }

            [Theory]
            [InlineData("MyObjectBuilder_PhysicalObject", "ScrapPart")]
            [InlineData("MyObjectBuilder_TotallyMadeUp", "Whatever")]
            [InlineData("", "")]
            public void Falls_back_to_misc_for_unknown_types(string typeId, string subId) {
                Program.ClassifyByTypeId(typeId, subId).Should().Be(Program.ItemCategory.Misc);
            }
        }
    }
}
