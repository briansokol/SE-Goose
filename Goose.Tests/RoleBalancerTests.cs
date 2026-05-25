using FluentAssertions;
using IngameScript;
using Xunit;

namespace Goose.Tests
{
    /// <summary>Tests for the pure helpers in Program.RoleBalancer.cs.</summary>
    public class RoleBalancerTests
    {

        public class ComputeEqualSplitTargets_Tests
        {
            [Fact]
            public void Two_box_even_split()
            {
                long[] holdings = { 10, 0 };
                long[] capacity = { long.MaxValue, long.MaxValue };
                long[] targets = new long[2];
                Program.ComputeEqualSplitTargets(holdings, capacity, targets);
                targets.Should().Equal(5L, 5L);
            }

            [Fact]
            public void Two_box_total_zero_no_op()
            {
                long[] holdings = { 0, 0 };
                long[] capacity = { long.MaxValue, long.MaxValue };
                long[] targets = new long[2];
                Program.ComputeEqualSplitTargets(holdings, capacity, targets);
                targets.Should().Equal(0L, 0L);
            }

            [Fact]
            public void Three_box_remainder_distributes_to_first_uncapped()
            {
                long[] holdings = { 7, 0, 0 };
                long[] capacity = { long.MaxValue, long.MaxValue, long.MaxValue };
                long[] targets = new long[3];
                Program.ComputeEqualSplitTargets(holdings, capacity, targets);
                // 7 / 3 = 2 share, remainder 1 → first uncapped slot gets +1.
                targets.Should().Equal(3L, 2L, 2L);
            }

            [Fact]
            public void Small_box_caps_remainder_water_fills()
            {
                // [10, 0, 0, 0] cap-limited to 1 in slot 1 → 10 spread is 2.5 each;
                // slot 1 caps at 1, remaining 9 spreads across slots 0/2/3 = 3 each.
                long[] holdings = { 10, 0, 0, 0 };
                long[] capacity = { long.MaxValue, 1, long.MaxValue, long.MaxValue };
                long[] targets = new long[4];
                Program.ComputeEqualSplitTargets(holdings, capacity, targets);
                targets.Should().Equal(3L, 1L, 3L, 3L);
            }

            [Fact]
            public void All_full_no_changes()
            {
                long[] holdings = { 5, 5, 5 };
                long[] capacity = { 0, 0, 0 };
                long[] targets = new long[3];
                Program.ComputeEqualSplitTargets(holdings, capacity, targets);
                targets.Should().Equal(5L, 5L, 5L);
            }

            [Fact]
            public void Imbalance_with_zero_capacity_blocks_redistribution()
            {
                // One container holds 10, others have zero capacity to receive any.
                // Helper assigns 10 to slot 0 (its ceiling = 10) and zero to others.
                long[] holdings = { 10, 0, 0 };
                long[] capacity = { 0, 0, 0 };
                long[] targets = new long[3];
                Program.ComputeEqualSplitTargets(holdings, capacity, targets);
                targets.Should().Equal(10L, 0L, 0L);
            }

            [Fact]
            public void Total_zero_with_capacity_no_targets()
            {
                long[] holdings = { 0, 0, 0 };
                long[] capacity = { 100, 100, 100 };
                long[] targets = new long[3];
                Program.ComputeEqualSplitTargets(holdings, capacity, targets);
                targets.Should().Equal(0L, 0L, 0L);
            }

            [Fact]
            public void Single_container_returns_total()
            {
                long[] holdings = { 42 };
                long[] capacity = { long.MaxValue };
                long[] targets = new long[1];
                Program.ComputeEqualSplitTargets(holdings, capacity, targets);
                targets.Should().Equal(42L);
            }

            [Fact]
            public void Targets_sum_equals_holdings_when_no_caps_bind()
            {
                long[] holdings = { 17, 23, 5, 11 };
                long[] capacity = { long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue };
                long[] targets = new long[4];
                Program.ComputeEqualSplitTargets(holdings, capacity, targets);
                long sumIn = 0, sumOut = 0;
                for (int i = 0; i < holdings.Length; i++)
                { sumIn += holdings[i]; sumOut += targets[i]; }
                sumOut.Should().Be(sumIn);
            }
        }

        public class WillSameRoleBalancingRun_Tests
        {
            [Fact]
            public void Off_by_default_does_not_run()
            {
                Program.WillSameRoleBalancingRun(false, 5).Should().BeFalse();
            }

            [Fact]
            public void On_with_one_candidate_no_op()
            {
                Program.WillSameRoleBalancingRun(true, 1).Should().BeFalse();
            }

            [Fact]
            public void On_with_two_candidates_runs()
            {
                Program.WillSameRoleBalancingRun(true, 2).Should().BeTrue();
            }

            [Fact]
            public void On_with_zero_candidates_no_op()
            {
                Program.WillSameRoleBalancingRun(true, 0).Should().BeFalse();
            }

            [Fact]
            public void Off_with_zero_candidates_no_op()
            {
                Program.WillSameRoleBalancingRun(false, 0).Should().BeFalse();
            }
        }
    }
}
