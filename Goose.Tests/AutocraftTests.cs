using System.Text;
using FluentAssertions;
using IngameScript;
using Xunit;

namespace Goose.Tests {
    public class AutocraftTests {
        public class TryParseQuotaValue_Tests {
            [Theory]
            [InlineData("5000", 5000L, Program.AutocraftMode.Minimum)]
            [InlineData("5000M", 5000L, Program.AutocraftMode.Minimum)]
            [InlineData("5000m", 5000L, Program.AutocraftMode.Minimum)]
            [InlineData("10000L", 10000L, Program.AutocraftMode.Limiter)]
            [InlineData("10000l", 10000L, Program.AutocraftMode.Limiter)]
            [InlineData("0", 0L, Program.AutocraftMode.Minimum)]
            public void Parses_well_formed_values(string raw, long expectedAmount, Program.AutocraftMode expectedMode) {
                long amount;
                Program.AutocraftMode mode;
                bool ignore;
                Program.Autocraft_TryParseQuotaValue(raw, out amount, out mode, out ignore).Should().BeTrue();
                amount.Should().Be(expectedAmount);
                mode.Should().Be(expectedMode);
                ignore.Should().BeFalse();
            }

            [Theory]
            [InlineData("x")]
            [InlineData("X")]
            public void Recognizes_ignore_sentinel(string raw) {
                long amount;
                Program.AutocraftMode mode;
                bool ignore;
                Program.Autocraft_TryParseQuotaValue(raw, out amount, out mode, out ignore).Should().BeTrue();
                ignore.Should().BeTrue();
                amount.Should().Be(0);
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("abc")]
            [InlineData("-5")]
            [InlineData("L")]
            [InlineData("5LL")]
            [InlineData("xx")]
            [InlineData("5x")]
            public void Rejects_malformed_values(string raw) {
                long amount;
                Program.AutocraftMode mode;
                bool ignore;
                Program.Autocraft_TryParseQuotaValue(raw, out amount, out mode, out ignore).Should().BeFalse();
                ignore.Should().BeFalse();
            }
        }

        public class ComputePoolSplit_Tests {
            [Fact]
            public void Empty_pool_yields_zero_zero() {
                int a, d;
                Program.Autocraft_ComputePoolSplit(0, 5, 3, out a, out d);
                a.Should().Be(0);
                d.Should().Be(0);
            }

            [Fact]
            public void No_work_yields_zero_zero() {
                int a, d;
                Program.Autocraft_ComputePoolSplit(6, 0, 0, out a, out d);
                a.Should().Be(0);
                d.Should().Be(0);
            }

            [Fact]
            public void Single_assembler_assemble_wins_on_tie() {
                int a, d;
                Program.Autocraft_ComputePoolSplit(1, 2, 2, out a, out d);
                a.Should().Be(1);
                d.Should().Be(0);
            }

            [Fact]
            public void Single_assembler_more_disassemble_wins() {
                int a, d;
                Program.Autocraft_ComputePoolSplit(1, 1, 5, out a, out d);
                a.Should().Be(0);
                d.Should().Be(1);
            }

            [Fact]
            public void Only_assemble_work_takes_whole_pool() {
                int a, d;
                Program.Autocraft_ComputePoolSplit(6, 5, 0, out a, out d);
                a.Should().Be(6);
                d.Should().Be(0);
            }

            [Fact]
            public void Only_disassemble_work_takes_whole_pool() {
                int a, d;
                Program.Autocraft_ComputePoolSplit(6, 0, 4, out a, out d);
                a.Should().Be(0);
                d.Should().Be(6);
            }

            [Fact]
            public void Balances_by_pending_count() {
                int a, d;
                Program.Autocraft_ComputePoolSplit(6, 4, 2, out a, out d);
                a.Should().Be(4);
                d.Should().Be(2);
            }

            [Fact]
            public void Both_sides_get_at_least_one() {
                int a, d;
                Program.Autocraft_ComputePoolSplit(6, 100, 1, out a, out d);
                a.Should().BeGreaterOrEqualTo(1);
                d.Should().BeGreaterOrEqualTo(1);
                (a + d).Should().Be(6);
            }

            [Fact]
            public void Both_sides_get_at_least_one_when_assemble_swamps_disassemble() {
                int a, d;
                Program.Autocraft_ComputePoolSplit(2, 100, 1, out a, out d);
                a.Should().Be(1);
                d.Should().Be(1);
            }
        }

        public class FormatTargetColumn_Tests {
            [Fact]
            public void Minimum_quota_renders_plain_count() {
                Program.AutocraftQuota q = new Program.AutocraftQuota { Amount = 5000, Mode = Program.AutocraftMode.Minimum };
                Program.Autocraft_FormatTargetColumn(q).Should().Be("5000");
            }

            [Fact]
            public void Limiter_quota_renders_count_with_L_suffix() {
                Program.AutocraftQuota q = new Program.AutocraftQuota { Amount = 10000, Mode = Program.AutocraftMode.Limiter };
                Program.Autocraft_FormatTargetColumn(q).Should().Be("10000L");
            }

            [Fact]
            public void Null_quota_renders_dash() {
                Program.Autocraft_FormatTargetColumn(null).Should().Be("-");
            }
        }

        public class FormatStatusColumn_Tests {
            [Theory]
            [InlineData(Program.AutocraftStatus.OK, 0L, "OK")]
            [InlineData(Program.AutocraftStatus.OK, 100L, "OK")]
            [InlineData(Program.AutocraftStatus.Crafting, 0L, "Crafting")]
            [InlineData(Program.AutocraftStatus.Crafting, 1240L, "Crafting (1240 queued)")]
            [InlineData(Program.AutocraftStatus.Disassembling, 2300L, "Disassembling (2300 queued)")]
            [InlineData(Program.AutocraftStatus.BlockedNeedsLearn, 0L, "Blocked: NeedsLearn")]
            [InlineData(Program.AutocraftStatus.BlockedNoAssembler, 0L, "Blocked: NoAssembler")]
            [InlineData(Program.AutocraftStatus.Disabled, 0L, "Disabled")]
            public void Renders_expected_text(Program.AutocraftStatus status, long queued, string expected) {
                Program.Autocraft_FormatStatusColumn(status, queued).Should().Be(expected);
            }
        }

        public class AppendStatusRow_Tests {
            [Fact]
            public void Formats_aligned_columns() {
                StringBuilder sb = new StringBuilder();
                Program.Autocraft_AppendStatusRow(sb, "SteelPlate", "5000", 3210L, "Crafting (1240 queued)");
                string line = sb.ToString();
                line.Should().StartWith("SteelPlate");
                line.Should().Contain("5000");
                line.Should().Contain("3210");
                line.Should().Contain("Crafting (1240 queued)");
                line.Should().EndWith("\n");
                int posSteel = line.IndexOf("SteelPlate", System.StringComparison.Ordinal);
                int posTarget = line.IndexOf("5000", System.StringComparison.Ordinal);
                int posActual = line.IndexOf("3210", System.StringComparison.Ordinal);
                int posStatus = line.IndexOf("Crafting", System.StringComparison.Ordinal);
                posSteel.Should().Be(0);
                posTarget.Should().BeGreaterOrEqualTo(20);
                posActual.Should().BeGreaterOrEqualTo(29);
                posStatus.Should().BeGreaterOrEqualTo(38);
            }

            [Fact]
            public void Long_item_name_still_separates_from_target() {
                StringBuilder sb = new StringBuilder();
                Program.Autocraft_AppendStatusRow(sb, "ReallyLongItemSubtypeName", "100", 5L, "OK");
                string line = sb.ToString();
                line.Should().Contain("ReallyLongItemSubtypeName 100");
            }
        }
    }
}
