using FluentAssertions;
using IngameScript;
using Xunit;

namespace Goose.Tests
{
    /// <summary>Tests for the pure helpers in Program.Dispatcher.cs.</summary>
    public class DispatcherTests
    {
        public class AdjustBudgetFraction_Tests
        {
            [Fact]
            public void Scales_down_when_last_run_exceeded_target()
            {
                Program.AdjustBudgetFraction(1.0, 0.5, 0.8f, 0.8f)
                    .Should().BeApproximately(0.6f, 0.0001f);
            }

            [Fact]
            public void Scales_up_when_last_run_well_under_target()
            {
                Program.AdjustBudgetFraction(0.1, 0.5, 0.4f, 0.8f)
                    .Should().BeApproximately(0.44f, 0.0001f);
            }

            [Fact]
            public void Holds_steady_inside_the_dead_band()
            {
                // Between half the target and the target: no change, so the
                // fraction does not oscillate every tick.
                Program.AdjustBudgetFraction(0.4, 0.5, 0.5f, 0.8f)
                    .Should().BeApproximately(0.5f, 0.0001f);
            }

            [Fact]
            public void Never_exceeds_the_configured_ceiling()
            {
                Program.AdjustBudgetFraction(0.0, 0.5, 0.79f, 0.8f)
                    .Should().BeApproximately(0.8f, 0.0001f);
            }

            [Fact]
            public void Never_falls_below_the_floor()
            {
                Program.AdjustBudgetFraction(99.0, 0.5, 0.1f, 0.8f)
                    .Should().BeApproximately(0.1f, 0.0001f);
            }

            [Fact]
            public void Repeated_overruns_converge_to_the_floor()
            {
                float f = 0.8f;
                for (int i = 0; i < 50; i++)
                {
                    f = Program.AdjustBudgetFraction(99.0, 0.5, f, 0.8f);
                }
                f.Should().BeApproximately(0.1f, 0.0001f);
            }

            [Fact]
            public void Zero_target_disables_adaptation_and_returns_ceiling()
            {
                Program.AdjustBudgetFraction(99.0, 0.0, 0.2f, 0.8f)
                    .Should().BeApproximately(0.8f, 0.0001f);
            }
        }


        public class ShouldEnumerateFederationEdges_Tests
        {
            [Fact]
            public void Always_enumerates_on_the_first_call()
            {
                Program.ShouldEnumerateFederationEdges(3, 6, false, false).Should().BeTrue();
            }

            [Fact]
            public void Enumerates_on_the_heartbeat_boundary()
            {
                Program.ShouldEnumerateFederationEdges(12, 6, false, true).Should().BeTrue();
            }

            [Fact]
            public void Skips_between_heartbeats()
            {
                Program.ShouldEnumerateFederationEdges(13, 6, false, true).Should().BeFalse();
            }

            [Fact]
            public void Enumerates_when_a_rescan_is_pending()
            {
                Program.ShouldEnumerateFederationEdges(13, 6, true, true).Should().BeTrue();
            }

            [Fact]
            public void Treats_a_nonpositive_heartbeat_as_every_tick()
            {
                Program.ShouldEnumerateFederationEdges(13, 0, false, true).Should().BeTrue();
            }
        }
    }
}
