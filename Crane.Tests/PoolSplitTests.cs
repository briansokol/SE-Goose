using FluentAssertions;
using IngameScript;
using Xunit;

namespace Shared.Tests
{
    /// <summary>Tests for <see cref="PoolSplit"/>.</summary>
    public class PoolSplitTests
    {
        [Fact]
        public void Empty_pool_returns_zeros()
        {
            int a, d;
            bool r;
            PoolSplit.ComputePoolSplitWithReservation(0, 5, 5, true, out a, out d, out r);
            a.Should().Be(0);
            d.Should().Be(0);
            r.Should().BeFalse();
        }

        [Fact]
        public void Only_assemble_work_takes_pool()
        {
            int a, d;
            bool r;
            PoolSplit.ComputePoolSplitWithReservation(6, 4, 0, true, out a, out d, out r);
            a.Should().Be(6);
            d.Should().Be(0);
            r.Should().BeFalse();
        }

        [Fact]
        public void Only_disassemble_work_takes_pool()
        {
            int a, d;
            bool r;
            PoolSplit.ComputePoolSplitWithReservation(6, 0, 4, true, out a, out d, out r);
            a.Should().Be(0);
            d.Should().Be(6);
            r.Should().BeFalse();
        }

        [Fact]
        public void Reservation_subtracts_one_when_both_sides_active()
        {
            int a, d;
            bool r;
            PoolSplit.ComputePoolSplitWithReservation(6, 4, 2, true, out a, out d, out r);
            r.Should().BeTrue();
            (a + d).Should().Be(6);
            d.Should().BeGreaterThanOrEqualTo(1);
            a.Should().BeGreaterThanOrEqualTo(1);
        }

        [Fact]
        public void Pool_of_one_disables_reservation()
        {
            int a, d;
            bool r;
            PoolSplit.ComputePoolSplitWithReservation(1, 5, 5, true, out a, out d, out r);
            r.Should().BeFalse();
            (a + d).Should().Be(1);
        }

        [Fact]
        public void Both_sides_each_get_at_least_one()
        {
            int a, d;
            bool r;
            PoolSplit.ComputePoolSplitWithReservation(4, 100, 1, true, out a, out d, out r);
            a.Should().BeGreaterThanOrEqualTo(1);
            d.Should().BeGreaterThanOrEqualTo(1);
        }
    }
}
