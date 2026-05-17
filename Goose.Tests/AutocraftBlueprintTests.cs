using System;
using System.Collections.Generic;
using FluentAssertions;
using IngameScript;
using Xunit;

namespace Goose.Tests {
    public class AutocraftBlueprintTests {
        public class SubtypeFromKey_Tests {
            [Theory]
            [InlineData("Component/SteelPlate", "SteelPlate")]
            [InlineData("Ore/Stone", "Stone")]
            [InlineData("AmmoMagazine/NATO_25x184mm", "NATO_25x184mm")]
            public void Returns_subtype(string key, string expected) {
                Program.Autocraft_SubtypeFromKey(key).Should().Be(expected);
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("NoSlash")]
            [InlineData("Component/")]
            [InlineData("/Subtype")]
            public void Returns_empty_for_malformed(string key) {
                Program.Autocraft_SubtypeFromKey(key).Should().Be(string.Empty);
            }
        }

        public class Resolve_Tests {
            static readonly Dictionary<string, string> Curated = new Dictionary<string, string>(StringComparer.Ordinal) {
                { "Construction", "ConstructionComponent" },
                { "Computer",     "ComputerComponent" },
            };

            [Fact]
            public void UsesCacheFirst() {
                Dictionary<string, string> cache = new Dictionary<string, string>(StringComparer.Ordinal) {
                    { "Component/SteelPlate", "MyObjectBuilder_BlueprintDefinition/SteelPlate" }
                };
                int probes = 0;
                Func<string, bool> probe = id => { probes++; return false; };

                string result = Program.Autocraft_ResolveBlueprintIdString(
                    "Component/SteelPlate", cache, Curated, probe);

                result.Should().Be("MyObjectBuilder_BlueprintDefinition/SteelPlate");
                probes.Should().Be(0);
            }

            [Fact]
            public void FallsBackToCuratedMap() {
                List<string> tried = new List<string>();
                Func<string, bool> probe = id => {
                    tried.Add(id);
                    return id == "MyObjectBuilder_BlueprintDefinition/ConstructionComponent";
                };
                string result = Program.Autocraft_ResolveBlueprintIdString(
                    "Component/Construction", null, Curated, probe);

                result.Should().Be("MyObjectBuilder_BlueprintDefinition/ConstructionComponent");
                tried.Should().HaveCount(1);
            }

            [Fact]
            public void FallsBackToAutoProbe() {
                Func<string, bool> probe = id => id == "MyObjectBuilder_BlueprintDefinition/SteelPlate";
                string result = Program.Autocraft_ResolveBlueprintIdString(
                    "Component/SteelPlate", null, Curated, probe);
                result.Should().Be("MyObjectBuilder_BlueprintDefinition/SteelPlate");
            }

            [Fact]
            public void AutoProbe_UsesComponentSafetyNet() {
                Func<string, bool> probe = id => id == "MyObjectBuilder_BlueprintDefinition/LargeTubeComponent";
                string result = Program.Autocraft_ResolveBlueprintIdString(
                    "Component/LargeTube", null, Curated, probe);
                result.Should().Be("MyObjectBuilder_BlueprintDefinition/LargeTubeComponent");
            }

            [Fact]
            public void ReturnsNeedsLearn_WhenNothingMatches() {
                Func<string, bool> probe = id => false;
                string result = Program.Autocraft_ResolveBlueprintIdString(
                    "Component/ExoticModItem", null, Curated, probe);
                result.Should().BeNull();
            }

            [Fact]
            public void ReturnsNull_OnMalformedKey() {
                Func<string, bool> probe = id => true;
                Program.Autocraft_ResolveBlueprintIdString(null, null, Curated, probe).Should().BeNull();
                Program.Autocraft_ResolveBlueprintIdString("", null, Curated, probe).Should().BeNull();
                Program.Autocraft_ResolveBlueprintIdString("NoSlash", null, Curated, probe).Should().BeNull();
            }

            [Fact]
            public void Curated_FailsThrough_ToAutoProbe_WhenCuratedRejected() {
                Func<string, bool> probe = id => id == "MyObjectBuilder_BlueprintDefinition/Construction";
                string result = Program.Autocraft_ResolveBlueprintIdString(
                    "Component/Construction", null, Curated, probe);
                result.Should().Be("MyObjectBuilder_BlueprintDefinition/Construction");
            }
        }

        public class CuratedMap_Tests {
            [Fact]
            public void ContainsKnownVanillaMismatches() {
                Program.AutocraftCuratedBlueprintSubtype.Should().ContainKey("Construction");
                Program.AutocraftCuratedBlueprintSubtype["Construction"].Should().Be("ConstructionComponent");
                Program.AutocraftCuratedBlueprintSubtype.Should().ContainKey("Computer");
                Program.AutocraftCuratedBlueprintSubtype.Should().ContainKey("Detector");
                Program.AutocraftCuratedBlueprintSubtype.Should().ContainKey("Explosives");
                Program.AutocraftCuratedBlueprintSubtype.Should().ContainKey("Girder");
                Program.AutocraftCuratedBlueprintSubtype.Should().ContainKey("Medical");
                Program.AutocraftCuratedBlueprintSubtype.Should().ContainKey("Motor");
                Program.AutocraftCuratedBlueprintSubtype.Should().ContainKey("RadioCommunication");
                Program.AutocraftCuratedBlueprintSubtype.Should().ContainKey("Reactor");
                Program.AutocraftCuratedBlueprintSubtype.Should().ContainKey("Thrust");
            }
        }

        public class ParseGCraftBlueprints_Tests {
            [Fact]
            public void Parses_uncommented_entries_inside_section() {
                string customData =
                    "[Goose]\n" +
                    "Component/SteelPlate=5000\n" +
                    "\n" +
                    "[GCraftBlueprints]\n" +
                    "Component/Construction=MyObjectBuilder_BlueprintDefinition/ConstructionComponent\n" +
                    "; Component/Commented=ignored\n" +
                    "Component/MyMod=MyObjectBuilder_BlueprintDefinition/MyModBP\n";

                Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
                Program.Autocraft_ParseGCraftBlueprintsSection(customData, result);

                result.Should().HaveCount(2);
                result["Component/Construction"].Should().Be("MyObjectBuilder_BlueprintDefinition/ConstructionComponent");
                result["Component/MyMod"].Should().Be("MyObjectBuilder_BlueprintDefinition/MyModBP");
            }

            [Fact]
            public void Ignores_entries_in_other_sections() {
                string customData =
                    "[Goose]\n" +
                    "Component/SteelPlate=5000\n" +
                    "Component/IgnoredHere=NotABlueprint\n" +
                    "[GCraftBlueprints]\n" +
                    "Component/Construction=MyObjectBuilder_BlueprintDefinition/ConstructionComponent\n";

                Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
                Program.Autocraft_ParseGCraftBlueprintsSection(customData, result);

                result.Should().HaveCount(1);
                result.Should().NotContainKey("Component/SteelPlate");
                result.Should().NotContainKey("Component/IgnoredHere");
            }

            [Fact]
            public void Handles_empty_input() {
                Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
                Program.Autocraft_ParseGCraftBlueprintsSection(null, result);
                result.Should().BeEmpty();
                Program.Autocraft_ParseGCraftBlueprintsSection("", result);
                result.Should().BeEmpty();
                Program.Autocraft_ParseGCraftBlueprintsSection("[GCraftBlueprints]\n", result);
                result.Should().BeEmpty();
            }

            [Fact]
            public void Ignores_malformed_lines() {
                string customData =
                    "[GCraftBlueprints]\n" +
                    "=Value\n" +
                    "Key=\n" +
                    "JustText\n" +
                    "Good/Key=GoodValue\n";

                Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
                Program.Autocraft_ParseGCraftBlueprintsSection(customData, result);

                result.Should().HaveCount(1);
                result["Good/Key"].Should().Be("GoodValue");
            }
        }

        public class ParseGooseQuotaSection_Tests {
            [Fact]
            public void Captures_active_quota_lines_verbatim() {
                string customData =
                    "[Goose]\n" +
                    "Component/SteelPlate=5000\n" +
                    "Component/Construction=10000L\n" +
                    "; Component/MotorComponent=500\n";

                Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
                Program.Autocraft_ParseGooseQuotaSection(customData, result);

                result.Should().HaveCount(2);
                result["Component/SteelPlate"].Should().Be("Component/SteelPlate=5000");
                result["Component/Construction"].Should().Be("Component/Construction=10000L");
                result.Should().NotContainKey("Component/MotorComponent");
            }
        }

        public class BuildGCraftCustomData_Tests {
            [Fact]
            public void Preserves_user_active_quota_lines() {
                Dictionary<string, string> quotas = new Dictionary<string, string>(StringComparer.Ordinal) {
                    { "Component/SteelPlate", "Component/SteelPlate=5000" },
                };
                List<string> catalog = new List<string> { "Component/SteelPlate", "Component/InteriorPlate" };

                string output = Program.Autocraft_BuildGCraftCustomData(
                    autoBlueprintEntries: null,
                    userBlueprintEntries: null,
                    activeQuotaLines: quotas,
                    catalogKeys: catalog,
                    reusableSB: null);

                output.Should().Contain("Component/SteelPlate=5000");
                output.Should().Contain("Component/InteriorPlate=x");
                output.Should().NotContain("; Component/InteriorPlate=");
            }

            [Fact]
            public void Appends_new_catalog_items_as_ignore_defaults() {
                Dictionary<string, string> quotas = new Dictionary<string, string>(StringComparer.Ordinal);
                List<string> catalog = new List<string> {
                    "Component/Computer", "Component/SteelPlate", "Component/Construction"
                };

                string output = Program.Autocraft_BuildGCraftCustomData(
                    null, null, quotas, catalog, null);

                int posComputer     = output.IndexOf("Component/Computer=x", StringComparison.Ordinal);
                int posConstruction = output.IndexOf("Component/Construction=x", StringComparison.Ordinal);
                int posSteel        = output.IndexOf("Component/SteelPlate=x", StringComparison.Ordinal);
                posComputer.Should().BeGreaterThan(0);
                posConstruction.Should().BeGreaterThan(posComputer);
                posSteel.Should().BeGreaterThan(posConstruction);
                output.Should().NotContain("; Component/Computer=");
            }

            [Fact]
            public void Preserves_user_override_blueprint_mapping() {
                Dictionary<string, string> autoMap = new Dictionary<string, string>(StringComparer.Ordinal) {
                    { "Component/Construction", "MyObjectBuilder_BlueprintDefinition/ConstructionComponent" },
                };
                Dictionary<string, string> userMap = new Dictionary<string, string>(StringComparer.Ordinal) {
                    { "Component/Construction", "MyObjectBuilder_BlueprintDefinition/UserOverride" },
                };

                string output = Program.Autocraft_BuildGCraftCustomData(
                    autoMap, userMap,
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    new List<string>(), null);

                output.Should().Contain("Component/Construction=MyObjectBuilder_BlueprintDefinition/UserOverride");
                output.Should().NotContain("Component/Construction=MyObjectBuilder_BlueprintDefinition/ConstructionComponent");
            }

            [Fact]
            public void Emits_both_section_headers() {
                string output = Program.Autocraft_BuildGCraftCustomData(
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    new List<string>(), null);

                output.Should().StartWith("[Goose]");
                output.Should().Contain("\n[GCraftBlueprints]\n");
            }

            [Fact]
            public void Merges_auto_and_user_blueprints_alphabetically() {
                Dictionary<string, string> autoMap = new Dictionary<string, string>(StringComparer.Ordinal) {
                    { "Component/Zeta",  "MyObjectBuilder_BlueprintDefinition/Zeta" },
                    { "Component/Alpha", "MyObjectBuilder_BlueprintDefinition/Alpha" },
                };
                Dictionary<string, string> userMap = new Dictionary<string, string>(StringComparer.Ordinal) {
                    { "Component/Middle", "MyObjectBuilder_BlueprintDefinition/Middle" },
                };

                string output = Program.Autocraft_BuildGCraftCustomData(
                    autoMap, userMap,
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    new List<string>(), null);

                int posAlpha  = output.IndexOf("Component/Alpha=", StringComparison.Ordinal);
                int posMiddle = output.IndexOf("Component/Middle=", StringComparison.Ordinal);
                int posZeta   = output.IndexOf("Component/Zeta=", StringComparison.Ordinal);
                posAlpha.Should().BeGreaterThan(0);
                posMiddle.Should().BeGreaterThan(posAlpha);
                posZeta.Should().BeGreaterThan(posMiddle);
            }

            [Fact]
            public void Active_x_line_is_preserved_verbatim() {
                Dictionary<string, string> quotas = new Dictionary<string, string>(StringComparer.Ordinal) {
                    { "Component/SteelPlate", "Component/SteelPlate=x" },
                    { "Component/Construction", "Component/Construction=10000L" },
                };
                List<string> catalog = new List<string> { "Component/SteelPlate", "Component/Construction", "Component/Motor" };

                string output = Program.Autocraft_BuildGCraftCustomData(
                    null, null, quotas, catalog, null);

                output.Should().Contain("Component/SteelPlate=x");
                output.Should().Contain("Component/Construction=10000L");
                output.Should().Contain("Component/Motor=x");
            }
        }
    }
}
