using System.Collections.Generic;
using FluentAssertions;
using IngameScript;
using Xunit;

namespace Shared.Tests
{
    /// <summary>Tests for <see cref="ScopeArbitration"/>, the pure multi-Goose deference decision.</summary>
    public class ScopeArbitrationTests
    {
        private const long Root = 1;
        private const long MyPb = 100;

        private static FederationPeer Peer(long pbId, ulong signature, params long[] grids)
        {
            return new FederationPeer
            {
                PbId = pbId,
                Signature = signature,
                ConstructGrids = new LongSet(grids)
            };
        }

        private static ConnectorEdge Link(int localPriority, int otherPriority, bool otherTag = true, bool connected = true)
        {
            return new ConnectorEdge
            {
                OwnerGridId = Root,
                OtherGridId = 9,
                Connected = connected,
                FederateTag = true,
                OtherFederateTag = otherTag,
                LocalPriority = localPriority,
                OtherPriority = otherPriority
            };
        }

        [Fact]
        public void Duplicate_when_peer_shares_signature_with_different_pb()
        {
            var peers = new PeerList { Peer(200, 55UL) };
            ArbitrationResult result = ScopeArbitration.Decide(Root, MyPb, 55UL, new ConnectorEdgeList(), peers);
            result.DuplicateScope.Should().BeTrue();
        }

        [Fact]
        public void No_duplicate_for_own_pb_echo()
        {
            var peers = new PeerList { Peer(MyPb, 55UL) };
            ArbitrationResult result = ScopeArbitration.Decide(Root, MyPb, 55UL, new ConnectorEdgeList(), peers);
            result.DuplicateScope.Should().BeFalse();
        }

        [Fact]
        public void No_duplicate_for_different_signature()
        {
            var peers = new PeerList { Peer(200, 99UL) };
            ArbitrationResult result = ScopeArbitration.Decide(Root, MyPb, 55UL, new ConnectorEdgeList(), peers);
            result.DuplicateScope.Should().BeFalse();
        }

        [Fact]
        public void Dumb_ship_federates_when_both_tagged_and_no_peer_goose()
        {
            var conn = new ConnectorEdgeList { Link(0, 0) };
            ArbitrationResult result = ScopeArbitration.Decide(Root, MyPb, 1UL, conn, new PeerList());
            result.StandDown.Should().BeFalse();
            result.ApprovedFederateGrids.Should().BeEquivalentTo(new long[] { 9 });
        }

        [Fact]
        public void Single_side_tag_does_not_federate()
        {
            var conn = new ConnectorEdgeList { Link(0, 0, otherTag: false) };
            ArbitrationResult result = ScopeArbitration.Decide(Root, MyPb, 1UL, conn, new PeerList());
            result.ApprovedFederateGrids.Should().BeEmpty();
            result.StandDown.Should().BeFalse();
        }

        [Fact]
        public void Disconnected_link_is_ignored()
        {
            var conn = new ConnectorEdgeList { Link(0, 0, connected: false) };
            ArbitrationResult result = ScopeArbitration.Decide(Root, MyPb, 1UL, conn, new PeerList());
            result.ApprovedFederateGrids.Should().BeEmpty();
        }

        [Fact]
        public void Stand_down_when_peer_goose_outranks_me()
        {
            var conn = new ConnectorEdgeList { Link(localPriority: 1, otherPriority: 0) };
            var peers = new PeerList { Peer(200, 99UL, 9) };
            ArbitrationResult result = ScopeArbitration.Decide(Root, MyPb, 55UL, conn, peers);
            result.StandDown.Should().BeTrue();
            result.ApprovedFederateGrids.Should().BeEmpty();
        }

        [Fact]
        public void Equal_priority_peer_goose_does_not_federate_and_runs_on()
        {
            var conn = new ConnectorEdgeList { Link(localPriority: 0, otherPriority: 0) };
            var peers = new PeerList { Peer(200, 99UL, 9) };
            ArbitrationResult result = ScopeArbitration.Decide(Root, MyPb, 55UL, conn, peers);
            result.StandDown.Should().BeFalse();
            result.ApprovedFederateGrids.Should().BeEmpty();
        }

        [Fact]
        public void Higher_priority_master_federates_peer_goose()
        {
            var conn = new ConnectorEdgeList { Link(localPriority: 0, otherPriority: 2) };
            var peers = new PeerList { Peer(200, 99UL, 9) };
            ArbitrationResult result = ScopeArbitration.Decide(Root, MyPb, 55UL, conn, peers);
            result.StandDown.Should().BeFalse();
            result.ApprovedFederateGrids.Should().BeEquivalentTo(new long[] { 9 });
        }

        [Fact]
        public void Any_outranking_neighbor_clears_all_approvals()
        {
            var conn = new ConnectorEdgeList
            {
                new ConnectorEdge { OwnerGridId = Root, OtherGridId = 8, Connected = true, FederateTag = true, OtherFederateTag = true, LocalPriority = 0, OtherPriority = 5 },
                new ConnectorEdge { OwnerGridId = Root, OtherGridId = 9, Connected = true, FederateTag = true, OtherFederateTag = true, LocalPriority = 1, OtherPriority = 0 }
            };
            var peers = new PeerList { Peer(200, 99UL, 8), Peer(300, 88UL, 9) };
            ArbitrationResult result = ScopeArbitration.Decide(Root, MyPb, 55UL, conn, peers);
            result.StandDown.Should().BeTrue();
            result.ApprovedFederateGrids.Should().BeEmpty();
        }
    }
}
