using FluentAssertions;
using IngameScript;
using Xunit;

namespace Goose.Tests {
    /// <summary>Tests for the pure helpers in Program.Balancer.cs.</summary>
    public class BalancerTests {
        public class ComputeFillTargetVolume_Tests {
            [Theory]
            [InlineData(10f, 80, 8f)]
            [InlineData(10f, 0, 0f)]
            [InlineData(10f, 100, 10f)]
            [InlineData(10f, 50, 5f)]
            [InlineData(0f, 50, 0f)]
            [InlineData(0.064f, 80, 0.0512f)]
            [InlineData(64f, 25, 16f)]
            public void Returns_max_volume_times_percent(float maxVolume, int percent, float expected) {
                Program.ComputeFillTargetVolume(maxVolume, percent).Should().BeApproximately(expected, 0.0001f);
            }

            [Fact]
            public void Caller_is_responsible_for_clamping_so_method_does_not_clamp() {
                // Documents that this helper trusts its caller; ClampPercent is invoked at config-parse time.
                Program.ComputeFillTargetVolume(10f, 150).Should().BeApproximately(15f, 0.0001f);
                Program.ComputeFillTargetVolume(10f, -10).Should().BeApproximately(-1f, 0.0001f);
            }
        }

        public class IsWeaponFromProbe_Tests {
            [Theory]
            [InlineData(false, false, Program.ConsumerKind.None)]   // accepts nothing recognised
            [InlineData(true, false, Program.ConsumerKind.Weapon)]  // accepts ammo, not steel plate
            [InlineData(false, true, Program.ConsumerKind.None)]    // generic cargo
            [InlineData(true, true, Program.ConsumerKind.None)]     // accepts steel plate => generic cargo, ammo gate ignored
            public void Resolves_weapon_from_probe_results(bool canAmmo, bool canSteelPlate, Program.ConsumerKind expected) {
                Program.IsWeaponFromProbe(canAmmo, canSteelPlate).Should().Be(expected);
            }

            [Fact]
            public void SteelPlate_acceptance_blocks_weapon_classification() {
                // A container that accepts ammo AND steel plate is a generic container, not a weapon.
                Program.IsWeaponFromProbe(true, true).Should().Be(Program.ConsumerKind.None);
            }
        }


        public class WillBlockBeBalanced_Tests {
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
            public void Returns_true_when_balancer_acts_on_block(Program.ConsumerKind kind, long tagCount, int classPercent, bool expected) {
                Program.WillBlockBeBalanced(kind, tagCount, classPercent).Should().Be(expected);
            }

            [Fact]
            public void None_kind_never_balanced_even_with_tag() {
                // [NoBalance] forces ConsumerKind.None; a co-existing [Balance=N] tag is ignored.
                Program.WillBlockBeBalanced(Program.ConsumerKind.None, 100L, 80).Should().BeFalse();
            }

            [Fact]
            public void Tag_with_zero_count_is_still_an_explicit_balance_intent() {
                // [Balance=0] explicitly says "fill to 0 units" -- not the same as no tag.
                // Documents that 0 tag count == drain to empty, not "skip me".
                Program.WillBlockBeBalanced(Program.ConsumerKind.Reactor, 0L, 0).Should().BeTrue();
            }
        }


        public class ComputeProportionalFactor_Tests {
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
            public void Returns_factor_for_proportional_fill(float supply, float taggedReserved, float untaggedDemand, float expected) {
                Program.ComputeProportionalFactor(supply, taggedReserved, untaggedDemand).Should().BeApproximately(expected, 0.0001f);
            }

            [Fact]
            public void Tagged_reservation_does_not_starve_untagged_when_supply_is_plentiful() {
                // 1000 supply, 100 reserved, 300 demand → 900 left, factor capped at 1.
                Program.ComputeProportionalFactor(1000f, 100f, 300f).Should().BeApproximately(1f, 0.0001f);
            }
        }
    }
}
