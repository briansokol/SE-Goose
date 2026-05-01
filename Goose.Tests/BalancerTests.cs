using FluentAssertions;
using IngameScript;
using Xunit;

namespace Goose.Tests {
    /// <summary>Tests for the pure helpers in Program.Balancer.cs.</summary>
    public class BalancerTests {
        public class ComputeWeaponTargetVolume_Tests {
            [Theory]
            [InlineData(10f, 80, 8f)]
            [InlineData(10f, 0, 0f)]
            [InlineData(10f, 100, 10f)]
            [InlineData(10f, 50, 5f)]
            [InlineData(0f, 50, 0f)]
            [InlineData(0.064f, 80, 0.0512f)]
            [InlineData(64f, 25, 16f)]
            public void Returns_max_volume_times_percent(float maxVolume, int percent, float expected) {
                Program.ComputeWeaponTargetVolume(maxVolume, percent).Should().BeApproximately(expected, 0.0001f);
            }

            [Fact]
            public void Caller_is_responsible_for_clamping_so_method_does_not_clamp() {
                // Documents that this helper trusts its caller; ClampPercent is invoked at config-parse time.
                Program.ComputeWeaponTargetVolume(10f, 150).Should().BeApproximately(15f, 0.0001f);
                Program.ComputeWeaponTargetVolume(10f, -10).Should().BeApproximately(-1f, 0.0001f);
            }
        }

        public class IsConsumerKindFromProbes_Tests {
            [Theory]
            // (canIngotU, canOreIce, canAnyAmmo, canSteelPlate, expected)
            [InlineData(true, true, true, true, Program.ConsumerKind.None)]      // generic cargo container
            [InlineData(false, false, false, true, Program.ConsumerKind.None)]   // generic cargo accepting only steel plate
            [InlineData(true, false, false, false, Program.ConsumerKind.Reactor)]
            [InlineData(false, true, false, false, Program.ConsumerKind.Gas)]
            [InlineData(false, false, true, false, Program.ConsumerKind.Weapon)]
            [InlineData(false, false, false, false, Program.ConsumerKind.None)]  // accepts nothing recognised
            public void Resolves_consumer_kind_from_probe_results(bool canIngotU, bool canOreIce, bool canAmmo, bool canSteelPlate, Program.ConsumerKind expected) {
                Program.IsConsumerKindFromProbes(canIngotU, canOreIce, canAmmo, canSteelPlate).Should().Be(expected);
            }

            [Fact]
            public void SteelPlate_acceptance_overrides_everything() {
                // A container that accepts uranium AND steel plate is a generic container, not a reactor.
                Program.IsConsumerKindFromProbes(true, false, false, true).Should().Be(Program.ConsumerKind.None);
            }

            [Fact]
            public void Reactor_wins_over_gas_and_weapon_on_overlap() {
                // Hypothetical modded block that accepts uranium AND ice AND ammo (no steel plate).
                Program.IsConsumerKindFromProbes(true, true, true, false).Should().Be(Program.ConsumerKind.Reactor);
            }

            [Fact]
            public void Gas_wins_over_weapon_on_overlap() {
                Program.IsConsumerKindFromProbes(false, true, true, false).Should().Be(Program.ConsumerKind.Gas);
            }
        }
    }
}
