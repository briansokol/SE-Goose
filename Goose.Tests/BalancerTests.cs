using FluentAssertions;
using IngameScript;
using Xunit;

namespace Goose.Tests
{
    /// <summary>Tests for the pure helpers in Program.Balancer.cs.</summary>
    public class BalancerTests
    {
        public class ComputeFillTargetVolume_Tests
        {
            [Theory]
            [InlineData(10f, 80, 8f)]
            [InlineData(10f, 0, 0f)]
            [InlineData(10f, 100, 10f)]
            [InlineData(10f, 50, 5f)]
            [InlineData(0f, 50, 0f)]
            [InlineData(0.064f, 80, 0.0512f)]
            [InlineData(64f, 25, 16f)]
            public void Returns_max_volume_times_percent(float maxVolume, int percent, float expected)
            {
                Program.ComputeFillTargetVolume(maxVolume, percent).Should().BeApproximately(expected, 0.0001f);
            }

            [Fact]
            public void Caller_is_responsible_for_clamping_so_method_does_not_clamp()
            {
                // Documents that this helper trusts its caller; ClampPercent is invoked at config-parse time.
                Program.ComputeFillTargetVolume(10f, 150).Should().BeApproximately(15f, 0.0001f);
                Program.ComputeFillTargetVolume(10f, -10).Should().BeApproximately(-1f, 0.0001f);
            }
        }


        public class ComputeReactorIngotTarget_Tests
        {
            [Theory]
            [InlineData(0.0, 25, 25L)]
            [InlineData(0.5, 25, 25L)]
            [InlineData(0.999, 25, 25L)]
            [InlineData(0.5, 10, 10L)]
            [InlineData(1.0, 10, 20L)]
            [InlineData(1.0, 25, 50L)]
            [InlineData(1.4, 25, 50L)]
            [InlineData(1.5, 25, 50L)]
            [InlineData(1.999, 25, 50L)]
            [InlineData(2.0, 25, 75L)]
            [InlineData(2.49, 25, 75L)]
            [InlineData(2.5, 25, 75L)]
            [InlineData(4.5, 25, 125L)]
            [InlineData(4.49, 25, 125L)]
            [InlineData(10.0, 25, 275L)]
            [InlineData(26.25, 25, 675L)]
            [InlineData(1.0, 1, 2L)]
            [InlineData(1.0, 100, 200L)]
            [InlineData(5.0, 50, 300L)]
            public void Returns_ingot_target_for_volume_and_ratio(double maxVolumeM3, int ingotsPer1000L, long expected)
            {
                Program.ComputeReactorIngotTarget(maxVolumeM3, ingotsPer1000L).Should().Be(expected);
            }

            [Fact]
            public void Zero_ratio_disables_regardless_of_volume()
            {
                Program.ComputeReactorIngotTarget(0.0, 0).Should().Be(0L);
                Program.ComputeReactorIngotTarget(0.5, 0).Should().Be(0L);
                Program.ComputeReactorIngotTarget(5.0, 0).Should().Be(0L);
                Program.ComputeReactorIngotTarget(100.0, 0).Should().Be(0L);
            }

            [Fact]
            public void Empty_inventory_with_active_ratio_returns_first_bucket()
            {
                Program.ComputeReactorIngotTarget(0.0, 25).Should().Be(25L);
            }
        }

        public class WillBlockBeBalanced_Tests
        {
            [Theory]
            [InlineData(Program.ConsumerKind.None, -1L, 0, false)]
            [InlineData(Program.ConsumerKind.None, -1L, 80, false)]
            [InlineData(Program.ConsumerKind.None, 100L, 80, false)]
            [InlineData(Program.ConsumerKind.Reactor, -1L, 0, false)]
            [InlineData(Program.ConsumerKind.Reactor, -1L, 80, true)]
            [InlineData(Program.ConsumerKind.Reactor, 100L, 0, true)]
            [InlineData(Program.ConsumerKind.Reactor, 100L, 80, true)]
            [InlineData(Program.ConsumerKind.Reactor, 0L, 0, true)]
            [InlineData(Program.ConsumerKind.Gas, -1L, 25, true)]
            [InlineData(Program.ConsumerKind.Gas, -1L, 0, false)]
            [InlineData(Program.ConsumerKind.Weapon, 50L, 0, true)]
            [InlineData(Program.ConsumerKind.Weapon, -1L, 80, true)]
            [InlineData(Program.ConsumerKind.Weapon, -1L, 0, false)]
            public void Returns_true_when_balancer_acts_on_block(Program.ConsumerKind kind, long tagCount, int classPercent, bool expected)
            {
                Program.WillBlockBeBalanced(kind, tagCount, classPercent).Should().Be(expected);
            }

            [Fact]
            public void None_kind_never_balanced_even_with_tag()
            {
                // [NoBalance] forces ConsumerKind.None; a co-existing [Balance=N] tag is ignored.
                Program.WillBlockBeBalanced(Program.ConsumerKind.None, 100L, 80).Should().BeFalse();
            }

            [Fact]
            public void Tag_with_zero_count_is_still_an_explicit_balance_intent()
            {
                // [Balance=0] explicitly says "fill to 0 units" -- not the same as no tag.
                // Documents that 0 tag count == drain to empty, not "skip me".
                Program.WillBlockBeBalanced(Program.ConsumerKind.Reactor, 0L, 0).Should().BeTrue();
            }
        }


        public class ComputeProportionalFactor_Tests
        {
            [Theory]
            [InlineData(300f, 0f, 300f, 1f)]      // supply == demand exactly
            [InlineData(500f, 0f, 300f, 1f)]      // supply > demand → clamped to 1
            [InlineData(150f, 0f, 300f, 0.5f)]    // half-supply → 0.5 factor
            [InlineData(100f, 0f, 300f, 0.3333333f)]
            [InlineData(0f, 0f, 300f, 0f)]        // no supply → 0
            [InlineData(300f, 0f, 0f, 1f)]        // no demand → 1
            [InlineData(0f, 0f, 0f, 1f)]          // both zero → 1 (degenerate)
            [InlineData(300f, 100f, 300f, 0.6666667f)]   // 100 reserved by tagged, 200 left for 300 demand
            [InlineData(300f, 300f, 100f, 0f)]    // tagged eats all supply, untagged starves
            [InlineData(300f, 400f, 100f, 0f)]    // tagged exceeds supply (their fill will be partial)
            [InlineData(500f, 100f, 300f, 1f)]    // 400 left vs 300 demand → clamped to 1
            public void Returns_factor_for_proportional_fill(float supply, float taggedReserved, float untaggedDemand, float expected)
            {
                Program.ComputeProportionalFactor(supply, taggedReserved, untaggedDemand).Should().BeApproximately(expected, 0.0001f);
            }

            [Fact]
            public void Tagged_reservation_does_not_starve_untagged_when_supply_is_plentiful()
            {
                // 1000 supply, 100 reserved, 300 demand → 900 left, factor capped at 1.
                Program.ComputeProportionalFactor(1000f, 100f, 300f).Should().BeApproximately(1f, 0.0001f);
            }
        }
    }
}
