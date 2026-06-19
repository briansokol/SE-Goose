using System.Collections.Generic;
using FluentAssertions;
using IngameScript;
using Xunit;

namespace Goose.Tests
{
    /// <summary>Tests for the pure refinery-config statics on <see cref="Program"/>.</summary>
    public class RefineryTests
    {
        public class ParseRefineryOrder_Tests
        {
            [Fact]
            public void Returns_empty_when_all_ores_commented()
            {
                string cd = "[Goose]\n; Stone\n; Iron\n; Gold\n";
                var result = new List<string>();
                Program.ParseRefineryOrder(cd, result);
                result.Should().BeEmpty();
            }

            [Fact]
            public void Returns_uncommented_ores_in_file_order()
            {
                string cd = "[Goose]\n; Stone\nIron\nGold\n; Silver\n";
                var result = new List<string>();
                Program.ParseRefineryOrder(cd, result);
                result.Should().Equal("Iron", "Gold");
            }

            [Fact]
            public void Ignores_unknown_tokens_and_other_sections()
            {
                string cd = "[Other]\nIron\n[Goose]\nBananium\nPlatinum\n";
                var result = new List<string>();
                Program.ParseRefineryOrder(cd, result);
                result.Should().Equal("Platinum");
            }

            [Fact]
            public void Deduplicates_keeping_first_occurrence()
            {
                string cd = "[Goose]\nIron\nIron\n";
                var result = new List<string>();
                Program.ParseRefineryOrder(cd, result);
                result.Should().Equal("Iron");
            }
        }

        public class NeedsRefineryTemplate_Tests
        {
            [Fact]
            public void True_when_no_goose_section()
            {
                Program.NeedsRefineryTemplate("").Should().BeTrue();
                Program.NeedsRefineryTemplate("[Crane]\norder=Iron\n").Should().BeTrue();
            }

            [Fact]
            public void False_when_goose_section_present()
            {
                Program.NeedsRefineryTemplate("[Goose]\n; Iron\n").Should().BeFalse();
            }
        }

        public class BuildRefineryTemplate_Tests
        {
            [Fact]
            public void Appends_all_default_ores_commented()
            {
                string result = Program.BuildRefineryTemplate("");
                result.Should().Contain("[Goose]");
                foreach (string ore in Program.DefaultRefineryOres)
                {
                    result.Should().Contain("; " + ore);
                }
                var parsed = new List<string>();
                Program.ParseRefineryOrder(result, parsed);
                parsed.Should().BeEmpty();
            }

            [Fact]
            public void Preserves_existing_content_and_separates_it()
            {
                string result = Program.BuildRefineryTemplate("[Foo]\nbar=1");
                result.Should().StartWith("[Foo]\nbar=1");
                result.Should().Contain("[Goose]");
            }
        }
    }
}
