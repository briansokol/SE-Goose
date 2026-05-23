using FluentAssertions;
using IngameScript;
using Xunit;

namespace Shared.Tests {
    /// <summary>Tests for <see cref="DeficitSplit"/>.</summary>
    public class DeficitSplitTests {
        [Fact]
        public void Zero_capable_returns_empty() {
            DeficitSplit.SplitDeficit(100, 0, 5).Should().BeEmpty();
        }

        [Fact]
        public void Non_positive_deficit_returns_empty() {
            DeficitSplit.SplitDeficit(0, 3, 5).Should().BeEmpty();
            DeficitSplit.SplitDeficit(-10, 3, 5).Should().BeEmpty();
        }

        [Fact]
        public void Single_capable_gets_all() {
            DeficitSplit.SplitDeficit(100, 1, 5).Should().Equal(new long[] { 100 });
        }

        [Fact]
        public void Below_min_batch_goes_to_single() {
            DeficitSplit.SplitDeficit(3, 4, 5).Should().Equal(new long[] { 3 });
        }

        [Fact]
        public void At_or_above_min_batch_splits_ceil_share() {
            // 100 / 4 = 25 each, last absorbs remainder. Exact split → 25,25,25,25.
            DeficitSplit.SplitDeficit(100, 4, 5).Should().Equal(new long[] { 25, 25, 25, 25 });
        }

        [Fact]
        public void Last_absorbs_remainder() {
            // 10 / 3 = ceil 4, then last = 10 - 4 - 4 = 2.
            DeficitSplit.SplitDeficit(10, 3, 5).Should().Equal(new long[] { 4, 4, 2 });
        }
    }
}
