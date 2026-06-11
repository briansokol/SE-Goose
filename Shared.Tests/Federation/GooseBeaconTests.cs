using System.Collections.Generic;
using FluentAssertions;
using IngameScript;
using Xunit;

namespace Shared.Tests.Federation
{
    /// <summary>Tests for <see cref="GooseBeacon"/> presence/announce protocol over a fake transport.</summary>
    public class GooseBeaconTests
    {
        private const long MyPb = 100;

        private static GooseBeacon NewBeacon(FakeBridgeTransport transport, ulong signature, params long[] grids)
        {
            var gridSet = new List<long>(grids);
            var beacon = new GooseBeacon(
                transport,
                MyPb,
                () => signature,
                () => gridSet,
                _ => { });
            beacon.HeartbeatTicks = 6;
            beacon.PeerStaleMultiplier = 3;
            return beacon;
        }

        [Fact]
        public void Initialize_sends_an_announce()
        {
            var t = new FakeBridgeTransport();
            GooseBeacon beacon = NewBeacon(t, 55UL, 1, 2);
            beacon.Initialize();
            t.CountKind(FederationProtocol.KindAnnounce).Should().Be(1);
        }

        [Fact]
        public void Incoming_announce_populates_peer_table()
        {
            var t = new FakeBridgeTransport();
            GooseBeacon beacon = NewBeacon(t, 55UL, 1);
            beacon.Initialize();
            t.Inbox.Add(GooseBeacon.Announce(200, 99UL, new long[] { 8, 9 }).Serialize());

            beacon.Tick(1);

            PeerList peers = beacon.GetLivePeers(1);
            peers.Should().HaveCount(1);
            peers[0].PbId.Should().Be(200);
            peers[0].Signature.Should().Be(99UL);
            peers[0].ConstructGrids.Should().BeEquivalentTo(new long[] { 8, 9 });
        }

        [Fact]
        public void Own_announce_is_ignored()
        {
            var t = new FakeBridgeTransport();
            GooseBeacon beacon = NewBeacon(t, 55UL, 1);
            beacon.Initialize();
            t.Inbox.Add(GooseBeacon.Announce(MyPb, 55UL, new long[] { 1 }).Serialize());

            beacon.Tick(1);

            beacon.GetLivePeers(1).Should().BeEmpty();
        }

        [Fact]
        public void Stale_peer_is_dropped_after_missed_heartbeats()
        {
            var t = new FakeBridgeTransport();
            GooseBeacon beacon = NewBeacon(t, 55UL, 1);
            beacon.Initialize();
            t.Inbox.Add(GooseBeacon.Announce(200, 99UL, new long[] { 9 }).Serialize());
            beacon.Tick(1);
            beacon.GetLivePeers(1).Should().HaveCount(1);

            beacon.Tick(1 + 6 * 3 + 1);

            beacon.GetLivePeers(1 + 6 * 3 + 1).Should().BeEmpty();
        }

        [Fact]
        public void Emits_announce_on_heartbeat_cadence()
        {
            var t = new FakeBridgeTransport();
            GooseBeacon beacon = NewBeacon(t, 55UL, 1);
            beacon.Initialize();
            int afterInit = t.CountKind(FederationProtocol.KindAnnounce);

            beacon.Tick(6);

            t.CountKind(FederationProtocol.KindAnnounce).Should().BeGreaterThan(afterInit);
        }

        [Fact]
        public void Refreshed_peer_stays_live()
        {
            var t = new FakeBridgeTransport();
            GooseBeacon beacon = NewBeacon(t, 55UL, 1);
            beacon.Initialize();
            t.Inbox.Add(GooseBeacon.Announce(200, 99UL, new long[] { 9 }).Serialize());
            beacon.Tick(1);
            t.Inbox.Add(GooseBeacon.Announce(200, 99UL, new long[] { 9 }).Serialize());
            beacon.Tick(10);

            beacon.GetLivePeers(10).Should().HaveCount(1);
        }
    }
}
