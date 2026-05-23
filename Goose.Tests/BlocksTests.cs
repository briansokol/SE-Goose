using System.Collections.Generic;
using FluentAssertions;
using IngameScript;
using VRage.Game.ModAPI.Ingame;
using Xunit;

namespace Goose.Tests
{
    /// <summary>Tests for the pure helpers in Program.Blocks.cs.</summary>
    public class BlocksTests
    {
        public class ParsePriorityFromName_Tests
        {
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
            public void Returns_parsed_priority_or_default(string name, int expected)
            {
                Program.ParsePriorityFromName(name).Should().Be(expected);
            }

            [Theory]
            [InlineData("Cargo [P:abc]")]
            [InlineData("Cargo [P:]")]
            [InlineData("Cargo [P:")]
            [InlineData("Cargo P:5")]
            [InlineData("Cargo [Q:5]")]
            public void Returns_default_for_malformed_tags(string name)
            {
                Program.ParsePriorityFromName(name).Should().Be(100);
            }

            [Fact]
            public void First_well_formed_tag_wins_when_multiple_present()
            {
                Program.ParsePriorityFromName("Cargo [P:3] [P:9]").Should().Be(3);
            }
        }


        public class ParseBalanceTagCount_Tests
        {
            [Theory]
            [InlineData("Reactor [Balance=100]", 100L)]
            [InlineData("Reactor [Balance=0]", 0L)]
            [InlineData("Reactor [Balance=99999]", 99999L)]
            [InlineData("Turret [P:01] [Balance=50]", 50L)]
            [InlineData("[Balance=10]", 10L)]
            public void Parses_well_formed_tags(string name, long expected)
            {
                Program.ParseBalanceTagCount(name).Should().Be(expected);
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("Reactor")]
            [InlineData("Reactor [NoBalance]")]
            [InlineData("Reactor [P:01]")]
            [InlineData("Reactor [Balance=]")]
            [InlineData("Reactor [Balance=abc]")]
            [InlineData("Reactor [Balance=-5]")]
            [InlineData("Reactor [Balance=100")] // unmatched bracket
            [InlineData("Reactor [Balance=1.5]")]
            public void Returns_minus_one_for_absent_or_malformed_tags(string name)
            {
                Program.ParseBalanceTagCount(name).Should().Be(-1L);
            }

            [Fact]
            public void First_well_formed_tag_wins_when_multiple_present()
            {
                Program.ParseBalanceTagCount("Reactor [Balance=10] [Balance=20]").Should().Be(10L);
            }
        }

        public class IsIdentifier_Tests
        {
            [Theory]
            [InlineData("Foo")]
            [InlineData("foo")]
            [InlineData("_Foo")]
            [InlineData("Foo_Bar")]
            [InlineData("Foo123")]
            [InlineData("F")]
            [InlineData("_")]
            [InlineData("MyObjectBuilder_Ore")]
            public void Accepts_valid_identifiers(string s)
            {
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
            public void Rejects_invalid_identifiers(string s)
            {
                Program.IsIdentifier(s).Should().BeFalse();
            }
        }

        public class ClassifyByTypeId_Tests
        {
            [Theory]
            [InlineData("MyObjectBuilder_Ore", "Iron", Program.ItemCategory.Ores)]
            [InlineData("MyObjectBuilder_Ore", "Stone", Program.ItemCategory.Ores)]
            [InlineData("MyObjectBuilder_Ingot", "Iron", Program.ItemCategory.Ingots)]
            [InlineData("MyObjectBuilder_Ingot", "Gold", Program.ItemCategory.Ingots)]
            [InlineData("MyObjectBuilder_AmmoMagazine", "NATO_25x184mm", Program.ItemCategory.Ammo)]
            [InlineData("MyObjectBuilder_AmmoMagazine", "Missile200mm", Program.ItemCategory.Ammo)]
            [InlineData("MyObjectBuilder_Datapad", "Datapad", Program.ItemCategory.Misc)]
            public void Classifies_simple_types(string typeId, string subId, Program.ItemCategory expected)
            {
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
            public void Classifies_components_and_prototech(string subId, Program.ItemCategory expected)
            {
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
            public void Classifies_physical_gun_objects(string subId, Program.ItemCategory expected)
            {
                Program.ClassifyByTypeId("MyObjectBuilder_PhysicalGunObject", subId).Should().Be(expected);
            }

            [Theory]
            [InlineData("MyObjectBuilder_OxygenContainerObject", "OxygenBottle", Program.ItemCategory.Tools)]
            [InlineData("MyObjectBuilder_GasContainerObject", "HydrogenBottle", Program.ItemCategory.Tools)]
            public void Classifies_gas_and_oxygen_containers(string typeId, string subId, Program.ItemCategory expected)
            {
                Program.ClassifyByTypeId(typeId, subId).Should().Be(expected);
            }

            [Theory]
            [InlineData("Ingredient_Wheat", Program.ItemCategory.Ingredients)]
            [InlineData("WheatIngredient", Program.ItemCategory.Ingredients)]
            [InlineData("Meal_Stew", Program.ItemCategory.Meals)]
            [InlineData("BeefMeal", Program.ItemCategory.Meals)]
            [InlineData("MedicalDose", Program.ItemCategory.Consumables)]
            [InlineData("RandomConsumable", Program.ItemCategory.Consumables)]
            public void Classifies_consumable_items(string subId, Program.ItemCategory expected)
            {
                Program.ClassifyByTypeId("MyObjectBuilder_ConsumableItem", subId).Should().Be(expected);
            }

            [Theory]
            [InlineData("MyObjectBuilder_SeedItem", "Seed", Program.ItemCategory.Seeds)]
            [InlineData("MyObjectBuilder_SeedItem", "", Program.ItemCategory.Seeds)]
            [InlineData("MyObjectBuilder_SeedItem", "WheatSeed", Program.ItemCategory.Seeds)]
            public void Classifies_seed_items(string typeId, string subId, Program.ItemCategory expected)
            {
                Program.ClassifyByTypeId(typeId, subId).Should().Be(expected);
            }

            [Theory]
            [InlineData("MyObjectBuilder_PhysicalObject", "ScrapPart")]
            [InlineData("MyObjectBuilder_TotallyMadeUp", "Whatever")]
            [InlineData("", "")]
            public void Falls_back_to_misc_for_unknown_types(string typeId, string subId)
            {
                Program.ClassifyByTypeId(typeId, subId).Should().Be(Program.ItemCategory.Misc);
            }
        }

        public class LooksLikeNameTagQuota_Tests
        {
            [Theory]
            [InlineData("Ingot/Iron:100")]
            [InlineData("Component/SteelPlate:50M")]
            [InlineData("MyObjectBuilder_Ingot/Iron:100")]
            [InlineData(" Ingot/Iron : 100 ")]
            [InlineData("a/b:c")]
            public void Returns_true_when_token_contains_slash_and_colon(string token)
            {
                Program.LooksLikeNameTagQuota(token).Should().BeTrue();
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("Stock")]
            [InlineData("P:01")]
            [InlineData("Ignore")]
            [InlineData("Ingots")]
            [InlineData("Ingot/Iron")]
            [InlineData("Stuff:more")]
            public void Returns_false_when_token_lacks_slash_or_colon(string token)
            {
                Program.LooksLikeNameTagQuota(token).Should().BeFalse();
            }
        }

        public class TryParseNameTagQuotaShape_Tests
        {
            [Fact]
            public void Parses_exact_quota()
            {
                string typeId, subtypeId;
                long amount;
                Program.QuotaMode mode;
                Program.TryParseNameTagQuotaShape("Ingot/Iron:100", out typeId, out subtypeId, out amount, out mode).Should().BeTrue();
                typeId.Should().Be("MyObjectBuilder_Ingot");
                subtypeId.Should().Be("Iron");
                amount.Should().Be(100);
                mode.Should().Be(Program.QuotaMode.Exact);
            }

            [Fact]
            public void Parses_minimum_suffix()
            {
                string typeId, subtypeId;
                long amount;
                Program.QuotaMode mode;
                Program.TryParseNameTagQuotaShape("Ingot/Iron:500M", out typeId, out subtypeId, out amount, out mode).Should().BeTrue();
                amount.Should().Be(500);
                mode.Should().Be(Program.QuotaMode.Minimum);
            }

            [Fact]
            public void Parses_limiter_suffix()
            {
                string typeId, subtypeId;
                long amount;
                Program.QuotaMode mode;
                Program.TryParseNameTagQuotaShape("Ore/Stone:1000L", out typeId, out subtypeId, out amount, out mode).Should().BeTrue();
                typeId.Should().Be("MyObjectBuilder_Ore");
                subtypeId.Should().Be("Stone");
                amount.Should().Be(1000);
                mode.Should().Be(Program.QuotaMode.Limiter);
            }

            [Theory]
            [InlineData("Component/Construction:All")]
            [InlineData("Component/Construction:all")]
            public void Parses_All_mode_case_insensitive(string token)
            {
                string typeId, subtypeId;
                long amount;
                Program.QuotaMode mode;
                Program.TryParseNameTagQuotaShape(token, out typeId, out subtypeId, out amount, out mode).Should().BeTrue();
                typeId.Should().Be("MyObjectBuilder_Component");
                subtypeId.Should().Be("Construction");
                mode.Should().Be(Program.QuotaMode.All);
            }

            [Fact]
            public void Tolerates_whitespace_around_colon()
            {
                string typeId, subtypeId;
                long amount;
                Program.QuotaMode mode;
                Program.TryParseNameTagQuotaShape(" Ingot/Iron : 100M ", out typeId, out subtypeId, out amount, out mode).Should().BeTrue();
                typeId.Should().Be("MyObjectBuilder_Ingot");
                subtypeId.Should().Be("Iron");
                amount.Should().Be(100);
                mode.Should().Be(Program.QuotaMode.Minimum);
            }

            [Fact]
            public void Accepts_long_form_object_builder_prefix()
            {
                string typeId, subtypeId;
                long amount;
                Program.QuotaMode mode;
                Program.TryParseNameTagQuotaShape("MyObjectBuilder_Ingot/Iron:100", out typeId, out subtypeId, out amount, out mode).Should().BeTrue();
                typeId.Should().Be("MyObjectBuilder_Ingot");
                subtypeId.Should().Be("Iron");
            }

            [Theory]
            [InlineData("")]
            [InlineData(":100")]
            [InlineData("Ingot/Iron:")]
            [InlineData("Ingot/Iron:abc")]
            [InlineData("Ingot/Iron::100")]
            [InlineData("Ingot/Iron:1.5")]
            [InlineData("/Iron:100")]
            [InlineData("Ingot/:100")]
            public void Returns_false_for_malformed_tokens(string token)
            {
                string typeId, subtypeId;
                long amount;
                Program.QuotaMode mode;
                Program.TryParseNameTagQuotaShape(token, out typeId, out subtypeId, out amount, out mode).Should().BeFalse();
            }
        }

        public class ExtractNameTagQuotas_Tests
        {
            /// <summary>Test resolver covering the SE item types accessible via <c>Make*</c>
            /// factories. Returns <c>null</c> for unrecognized type ids so we can also exercise
            /// the unresolvable-type path.</summary>
            private static MyItemType? MakeBasedResolver(string fullyQualified)
            {
                int slash = fullyQualified.IndexOf('/');
                if (slash <= 0 || slash >= fullyQualified.Length - 1)
                {
                    return null;
                }

                string typeId = fullyQualified.Substring(0, slash);
                string subId = fullyQualified.Substring(slash + 1);
                switch (typeId)
                {
                    case "MyObjectBuilder_Ingot":
                        return MyItemType.MakeIngot(subId);
                    case "MyObjectBuilder_Ore":
                        return MyItemType.MakeOre(subId);
                    case "MyObjectBuilder_Component":
                        return MyItemType.MakeComponent(subId);
                    case "MyObjectBuilder_AmmoMagazine":
                        return MyItemType.MakeAmmo(subId);
                    default:
                        return null;
                }
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("Cargo")]
            [InlineData("Cargo Stock [Stock]")]
            [InlineData("Cargo [Stock] [P:01]")]
            [InlineData("Cargo [Ingots]")]
            [InlineData("Cargo [Ingot/Iron]")]
            public void Extracts_no_quotas_from_non_quota_names(string name)
            {
                var dict = new Dictionary<MyItemType, Program.StockQuota>();
                List<string> malformed = Program.ExtractNameTagQuotas(name, dict, MakeBasedResolver);
                dict.Should().BeEmpty();
                malformed.Should().BeNull();
            }

            [Fact]
            public void Extracts_single_quota()
            {
                var dict = new Dictionary<MyItemType, Program.StockQuota>();
                Program.ExtractNameTagQuotas("Cargo [Stock] [Ingot/Iron:100]", dict, MakeBasedResolver);
                dict.Should().HaveCount(1);
                dict[MyItemType.MakeIngot("Iron")].Amount.Should().Be(100);
                dict[MyItemType.MakeIngot("Iron")].Mode.Should().Be(Program.QuotaMode.Exact);
            }

            [Fact]
            public void Extracts_multiple_quotas()
            {
                var dict = new Dictionary<MyItemType, Program.StockQuota>();
                Program.ExtractNameTagQuotas("Cargo [Stock] [Ingot/Iron:100M] [Ingot/Cobalt:50L]", dict, MakeBasedResolver);
                dict.Should().HaveCount(2);
                dict[MyItemType.MakeIngot("Iron")].Mode.Should().Be(Program.QuotaMode.Minimum);
                dict[MyItemType.MakeIngot("Cobalt")].Mode.Should().Be(Program.QuotaMode.Limiter);
            }

            [Fact]
            public void Last_duplicate_key_wins()
            {
                var dict = new Dictionary<MyItemType, Program.StockQuota>();
                Program.ExtractNameTagQuotas("[Stock] [Ingot/Iron:100] [Ingot/Iron:50]", dict, MakeBasedResolver);
                dict.Should().HaveCount(1);
                dict[MyItemType.MakeIngot("Iron")].Amount.Should().Be(50);
            }

            [Fact]
            public void Name_tag_overrides_existing_dictionary_entry()
            {
                var dict = new Dictionary<MyItemType, Program.StockQuota>();
                dict[MyItemType.MakeIngot("Iron")] =
                    new Program.StockQuota { Amount = 500, Mode = Program.QuotaMode.Minimum };
                Program.ExtractNameTagQuotas("Cargo [Stock] [Ingot/Iron:100]", dict, MakeBasedResolver);
                dict.Should().HaveCount(1);
                dict[MyItemType.MakeIngot("Iron")].Amount.Should().Be(100);
                dict[MyItemType.MakeIngot("Iron")].Mode.Should().Be(Program.QuotaMode.Exact);
            }

            [Fact]
            public void Tolerates_unmatched_open_bracket()
            {
                var dict = new Dictionary<MyItemType, Program.StockQuota>();
                List<string> malformed = Program.ExtractNameTagQuotas("Cargo [Stock] [Ingot/Iron:100", dict, MakeBasedResolver);
                dict.Should().BeEmpty();
                malformed.Should().BeNull();
            }

            [Fact]
            public void Reports_malformed_quota_tokens()
            {
                var dict = new Dictionary<MyItemType, Program.StockQuota>();
                List<string> malformed = Program.ExtractNameTagQuotas("Cargo [Stock] [Ingot/Iron:abc]", dict, MakeBasedResolver);
                dict.Should().BeEmpty();
                malformed.Should().NotBeNull();
                malformed.Should().ContainSingle().Which.Should().Be("Ingot/Iron:abc");
            }

            [Fact]
            public void Mixes_valid_and_malformed_tokens()
            {
                var dict = new Dictionary<MyItemType, Program.StockQuota>();
                List<string> malformed = Program.ExtractNameTagQuotas(
                    "[Stock] [Ingot/Iron:100] [Ingot/Cobalt:abc] [P:01]", dict, MakeBasedResolver);
                dict.Should().HaveCount(1);
                dict[MyItemType.MakeIngot("Iron")].Amount.Should().Be(100);
                malformed.Should().ContainSingle().Which.Should().Be("Ingot/Cobalt:abc");
            }

            [Fact]
            public void Reports_unresolvable_types_as_malformed()
            {
                var dict = new Dictionary<MyItemType, Program.StockQuota>();
                List<string> malformed = Program.ExtractNameTagQuotas(
                    "[Stock] [UnknownType/Foo:100]", dict, MakeBasedResolver);
                dict.Should().BeEmpty();
                malformed.Should().ContainSingle().Which.Should().Be("UnknownType/Foo:100");
            }
        }



    }
}
