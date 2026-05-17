using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using IngameScript;
using Sandbox.ModAPI.Ingame;
using Xunit;

namespace Goose.Tests {
    public class AutocraftTests {
        public class TryParseQuotaValue_Tests {
            [Theory]
            [InlineData("5000", 5000L, Program.AutocraftMode.Minimum)]
            [InlineData("0", 0L, Program.AutocraftMode.Minimum)]
            [InlineData("10000E", 10000L, Program.AutocraftMode.Exact)]
            [InlineData("10000e", 10000L, Program.AutocraftMode.Exact)]
            [InlineData("10000L", 10000L, Program.AutocraftMode.Exact)]
            [InlineData("10000l", 10000L, Program.AutocraftMode.Exact)]
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
            [InlineData("E")]
            [InlineData("e")]
            [InlineData("L")]
            [InlineData("5LL")]
            [InlineData("5EE")]
            [InlineData("5000M")]
            [InlineData("5000m")]
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


        public class CeilFixedPointToLong_Tests {
            [Theory]
            [InlineData(0L, 0L)]
            [InlineData(1L, 1L)]
            [InlineData(5L, 5L)]
            [InlineData(100L, 100L)]
            public void Whole_values_pass_through(long input, long expected) {
                VRage.MyFixedPoint v = VRage.MyFixedPoint.DeserializeStringSafe(input.ToString());
                Program.Autocraft_CeilFixedPointToLong(v).Should().Be(expected);
            }

            [Theory]
            [InlineData("0.0001", 1L)]
            [InlineData("0.5",    1L)]
            [InlineData("4.5",    5L)]
            [InlineData("4.9",    5L)]
            [InlineData("99.999", 100L)]
            public void Fractional_remainder_rounds_up(string serialized, long expected) {
                VRage.MyFixedPoint v = VRage.MyFixedPoint.DeserializeStringSafe(serialized);
                Program.Autocraft_CeilFixedPointToLong(v).Should().Be(expected);
            }

            [Fact]
            public void Zero_returns_zero() {
                Program.Autocraft_CeilFixedPointToLong(VRage.MyFixedPoint.Zero).Should().Be(0);
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
            public void Exact_quota_renders_count_with_E_suffix() {
                Program.AutocraftQuota q = new Program.AutocraftQuota { Amount = 10000, Mode = Program.AutocraftMode.Exact };
                Program.Autocraft_FormatTargetColumn(q).Should().Be("10000E");
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


        public class SplitDeficit_Tests {
            [Fact]
            public void Even_split_no_remainder() {
                int[] shares = Program.Autocraft_SplitDeficit(12, 4, 5);
                shares.Should().Equal(new[] { 3, 3, 3, 3 });
            }

            [Fact]
            public void Split_with_remainder_lands_on_last_slot() {
                int[] shares = Program.Autocraft_SplitDeficit(13, 4, 5);
                shares.Should().Equal(new[] { 4, 4, 4, 1 });
            }

            [Fact]
            public void Below_min_batch_goes_to_single_slot() {
                int[] shares = Program.Autocraft_SplitDeficit(3, 5, 5);
                shares.Should().Equal(new[] { 3, 0, 0, 0, 0 });
            }

            [Fact]
            public void Single_capable_takes_full_deficit() {
                int[] shares = Program.Autocraft_SplitDeficit(5, 1, 5);
                shares.Should().Equal(new[] { 5 });
            }

            [Fact]
            public void Three_capable_with_remainder() {
                int[] shares = Program.Autocraft_SplitDeficit(10, 3, 5);
                shares.Should().Equal(new[] { 4, 4, 2 });
            }

            [Fact]
            public void Zero_deficit_yields_zero_array() {
                int[] shares = Program.Autocraft_SplitDeficit(0, 4, 5);
                shares.Should().Equal(new[] { 0, 0, 0, 0 });
            }

            [Fact]
            public void Zero_capable_yields_empty() {
                int[] shares = Program.Autocraft_SplitDeficit(100, 0, 5);
                shares.Should().BeEmpty();
            }

            [Fact]
            public void Two_capable_with_remainder() {
                int[] shares = Program.Autocraft_SplitDeficit(7, 2, 5);
                shares.Should().Equal(new[] { 4, 3 });
            }
        }

        public class PickReservedAssemblerIndex_Tests {
            [Fact]
            public void Picks_assembler_in_most_capable_sets() {
                var ids = new List<long> { 100, 200, 300 };
                var sets = new List<HashSet<long>> {
                    new HashSet<long> { 100, 200 },
                    new HashSet<long> { 200, 300 }
                };
                Program.Autocraft_PickReservedAssemblerIndex(ids, sets).Should().Be(1);
            }

            [Fact]
            public void All_equally_capable_picks_lowest_entity() {
                var ids = new List<long> { 100, 200, 300 };
                var sets = new List<HashSet<long>> {
                    new HashSet<long> { 100, 200, 300 },
                    new HashSet<long> { 100, 200, 300 }
                };
                Program.Autocraft_PickReservedAssemblerIndex(ids, sets).Should().Be(0);
            }

            [Fact]
            public void Pool_of_one_returns_minus_one() {
                var ids = new List<long> { 100 };
                var sets = new List<HashSet<long>> { new HashSet<long> { 100 } };
                Program.Autocraft_PickReservedAssemblerIndex(ids, sets).Should().Be(-1);
            }

            [Fact]
            public void Empty_work_set_returns_minus_one() {
                var ids = new List<long> { 100, 200 };
                var sets = new List<HashSet<long>>();
                Program.Autocraft_PickReservedAssemblerIndex(ids, sets).Should().Be(-1);
            }

            [Fact]
            public void Asymmetric_two_for_one_versus_one_for_three() {
                var ids = new List<long> { 100, 200, 300 };
                var sets = new List<HashSet<long>> {
                    new HashSet<long> { 100 },
                    new HashSet<long> { 100 },
                    new HashSet<long> { 300 }
                };
                Program.Autocraft_PickReservedAssemblerIndex(ids, sets).Should().Be(0);
            }

            [Fact]
            public void All_sets_empty_returns_minus_one() {
                var ids = new List<long> { 100, 200 };
                var sets = new List<HashSet<long>> { new HashSet<long>(), new HashSet<long>() };
                Program.Autocraft_PickReservedAssemblerIndex(ids, sets).Should().Be(-1);
            }
        }

        public class ComputePoolSplitWithReservation_Tests {
            [Fact]
            public void Six_pool_balanced_work_reserve_on() {
                int a, d; bool r;
                Program.Autocraft_ComputePoolSplitWithReservation(6, 5, 5, true, out a, out d, out r);
                a.Should().Be(4);
                d.Should().Be(2);
                r.Should().BeTrue();
            }

            [Fact]
            public void Pool_of_one_falls_through_to_unreserved_split() {
                int a, d; bool r;
                Program.Autocraft_ComputePoolSplitWithReservation(1, 5, 5, true, out a, out d, out r);
                a.Should().Be(1);
                d.Should().Be(0);
                r.Should().BeFalse();
            }

            [Fact]
            public void Only_disassemble_work_no_reservation() {
                int a, d; bool r;
                Program.Autocraft_ComputePoolSplitWithReservation(6, 0, 5, true, out a, out d, out r);
                a.Should().Be(0);
                d.Should().Be(6);
                r.Should().BeFalse();
            }

            [Fact]
            public void Pool_of_two_with_both_sides_works() {
                int a, d; bool r;
                Program.Autocraft_ComputePoolSplitWithReservation(2, 1, 10, true, out a, out d, out r);
                a.Should().Be(1);
                d.Should().Be(1);
                r.Should().BeTrue();
            }

            [Fact]
            public void Reserve_disabled_matches_plain_split() {
                int a, d; bool r;
                Program.Autocraft_ComputePoolSplitWithReservation(6, 5, 5, false, out a, out d, out r);
                int plainA, plainD;
                Program.Autocraft_ComputePoolSplit(6, 5, 5, out plainA, out plainD);
                a.Should().Be(plainA);
                d.Should().Be(plainD);
                r.Should().BeFalse();
            }
        }

        public class CanFlipMode_Tests {
            [Fact]
            public void Empty_queue_allows_flip() {
                Program.Autocraft_CanFlipMode(true, MyAssemblerMode.Assembly, MyAssemblerMode.Disassembly)
                    .Should().BeTrue();
            }

            [Fact]
            public void Non_empty_queue_blocks_flip() {
                Program.Autocraft_CanFlipMode(false, MyAssemblerMode.Assembly, MyAssemblerMode.Disassembly)
                    .Should().BeFalse();
            }

            [Fact]
            public void Same_mode_always_allowed() {
                Program.Autocraft_CanFlipMode(false, MyAssemblerMode.Assembly, MyAssemblerMode.Assembly)
                    .Should().BeTrue();
            }
        }

        public class PerAssemblerTopUp_Tests {
            [Fact]
            public void Adds_to_reach_share() {
                Program.Autocraft_PerAssemblerTopUp(10, 3, 100).Should().Be(7);
            }

            [Fact]
            public void At_share_returns_zero() {
                Program.Autocraft_PerAssemblerTopUp(10, 10, 100).Should().Be(0);
            }

            [Fact]
            public void Above_share_returns_zero() {
                Program.Autocraft_PerAssemblerTopUp(10, 15, 100).Should().Be(0);
            }

            [Fact]
            public void Max_depth_clamps_topup() {
                Program.Autocraft_PerAssemblerTopUp(200, 0, 100).Should().Be(100);
            }
        }

        public class ComputeShareSurplus_Tests {
            [Fact]
            public void Above_share_returns_surplus() {
                Program.Autocraft_ComputeShareSurplus(10, 15).Should().Be(5);
            }

            [Fact]
            public void Below_share_returns_zero() {
                Program.Autocraft_ComputeShareSurplus(10, 5).Should().Be(0);
            }

            [Fact]
            public void At_share_returns_zero() {
                Program.Autocraft_ComputeShareSurplus(10, 10).Should().Be(0);
            }
        }

        public class ReservationStillValid_Tests {
            [Fact]
            public void Reserved_entity_in_one_set_is_valid() {
                var sets = new List<HashSet<long>> {
                    new HashSet<long> { 100 },
                    new HashSet<long> { 200 }
                };
                Program.Autocraft_ReservationStillValid(100, sets).Should().BeTrue();
            }

            [Fact]
            public void Reserved_entity_in_no_set_is_invalid() {
                var sets = new List<HashSet<long>> {
                    new HashSet<long> { 200 },
                    new HashSet<long> { 300 }
                };
                Program.Autocraft_ReservationStillValid(100, sets).Should().BeFalse();
            }

            [Fact]
            public void Empty_work_set_is_invalid() {
                var sets = new List<HashSet<long>>();
                Program.Autocraft_ReservationStillValid(100, sets).Should().BeFalse();
            }

            [Fact]
            public void Zero_reserved_id_is_invalid() {
                var sets = new List<HashSet<long>> { new HashSet<long> { 100 } };
                Program.Autocraft_ReservationStillValid(0, sets).Should().BeFalse();
            }
        }
    }
}
