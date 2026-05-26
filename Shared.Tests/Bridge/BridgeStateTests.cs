using System.Collections.Generic;
using FluentAssertions;
using IngameScript;
using Xunit;

namespace Shared.Tests
{
    /// <summary>Tests covering bridge state: hold TTL, eviction, and peer-link staleness.</summary>
    public class BridgeStateTests
    {
        private static Bridge NewGoose(FakeBridgeTransport transport, int maxHolds = 64)
        {
            var bridge = new Bridge(
                transport,
                BridgeRole.Goose,
                _ => { },
                () => 0,
                () => new List<string>(),
                _ => { });
            bridge.HeartbeatTicks = 10;
            bridge.MaxHolds = maxHolds;
            bridge.SnapshotDebounceTicks = 100;
            bridge.PeerStaleMultiplier = 3;
            return bridge;
        }

        [Fact]
        public void Hold_is_active_within_ttl()
        {
            var t = new FakeBridgeTransport();
            Bridge bridge = NewGoose(t);
            bridge.Initialize();

            t.Inbox.Add(BridgeMessage.AssemblerHold(42L, 5, "Ingot/Iron:1").Serialize());
            bridge.Tick(0);

            bridge.IsAssemblerHeld(42L).Should().BeTrue();
            bridge.GetHoldHint(42L).Should().NotBeNull();
            bridge.GetHoldHint(42L).NeedRaw.Should().Be("Ingot/Iron:1");
        }

        [Fact]
        public void Hold_expires_after_ttl_and_is_swept()
        {
            var t = new FakeBridgeTransport();
            Bridge bridge = NewGoose(t);
            bridge.Initialize();

            t.Inbox.Add(BridgeMessage.AssemblerHold(42L, 5, "Ingot/Iron:1").Serialize());
            bridge.Tick(0);
            bridge.IsAssemblerHeld(42L).Should().BeTrue();

            bridge.Tick(5);
            bridge.IsAssemblerHeld(42L).Should().BeTrue();

            bridge.Tick(6);
            bridge.IsAssemblerHeld(42L).Should().BeFalse();
            bridge.ActiveHoldCount.Should().Be(0);
        }

        [Fact]
        public void GetHoldHint_returns_null_when_no_hold()
        {
            var t = new FakeBridgeTransport();
            Bridge bridge = NewGoose(t);
            bridge.Initialize();

            bridge.GetHoldHint(99L).Should().BeNull();
            bridge.IsAssemblerHeld(99L).Should().BeFalse();
        }

        [Fact]
        public void Re_announcement_extends_hold()
        {
            var t = new FakeBridgeTransport();
            Bridge bridge = NewGoose(t);
            bridge.Initialize();

            t.Inbox.Add(BridgeMessage.AssemblerHold(42L, 5, "old").Serialize());
            bridge.Tick(0);

            t.Inbox.Add(BridgeMessage.AssemblerHold(42L, 5, "fresh").Serialize());
            bridge.Tick(5);

            bridge.IsAssemblerHeld(42L).Should().BeTrue();
            bridge.GetHoldHint(42L).NeedRaw.Should().Be("fresh");
            bridge.ActiveHoldCount.Should().Be(1);
        }

        [Fact]
        public void Eviction_kicks_in_at_max_holds()
        {
            var t = new FakeBridgeTransport();
            Bridge bridge = NewGoose(t, maxHolds: 3);
            bridge.Initialize();

            t.Inbox.Add(BridgeMessage.AssemblerHold(1L, 1000, "a").Serialize());
            t.Inbox.Add(BridgeMessage.AssemblerHold(2L, 1000, "b").Serialize());
            t.Inbox.Add(BridgeMessage.AssemblerHold(3L, 1000, "c").Serialize());
            bridge.Tick(0);
            bridge.ActiveHoldCount.Should().Be(3);

            t.Inbox.Add(BridgeMessage.AssemblerHold(4L, 1000, "d").Serialize());
            bridge.Tick(1);

            bridge.ActiveHoldCount.Should().Be(3);
            bridge.IsAssemblerHeld(1L).Should().BeFalse();
            bridge.IsAssemblerHeld(4L).Should().BeTrue();
        }

        [Fact]
        public void PeerLinked_goes_stale_after_three_heartbeats_without_message()
        {
            var t = new FakeBridgeTransport();
            Bridge bridge = NewGoose(t);
            bridge.HeartbeatTicks = 10;
            bridge.PeerStaleMultiplier = 3;
            bridge.Initialize();

            t.Inbox.Add(BridgeMessage.Hello(BridgeRole.Crane).Serialize());
            bridge.Tick(0);
            bridge.PeerStatus.Linked.Should().BeTrue();

            bridge.Tick(30);
            bridge.PeerStatus.Linked.Should().BeTrue();

            bridge.Tick(31);
            bridge.PeerStatus.Linked.Should().BeFalse();
        }

        [Fact]
        public void PeerStatus_reports_LastSeenTick()
        {
            var t = new FakeBridgeTransport();
            Bridge bridge = NewGoose(t);
            bridge.Initialize();

            t.Inbox.Add(BridgeMessage.Heartbeat(BridgeRole.Crane, 12).Serialize());
            bridge.Tick(50);

            bridge.PeerStatus.LastSeenTick.Should().Be(50);
            bridge.PeerStatus.PeerCatalogCount.Should().Be(12);
        }

        [Fact]
        public void AssemblerHold_addressed_to_Crane_is_ignored()
        {
            var t = new FakeBridgeTransport();
            var bridge = new Bridge(
                t,
                BridgeRole.Crane,
                _ => { },
                () => 0,
                () => new List<string>(),
                _ => { });
            bridge.HeartbeatTicks = 10;
            bridge.Initialize();

            t.Inbox.Add(BridgeMessage.AssemblerHold(1L, 100, "x").Serialize());
            bridge.Tick(0);

            bridge.IsAssemblerHeld(1L).Should().BeFalse();
        }

        [Fact]
        public void AssemblerHold_with_zero_or_negative_ttl_is_ignored()
        {
            var t = new FakeBridgeTransport();
            Bridge bridge = NewGoose(t);
            bridge.Initialize();

            t.Inbox.Add(BridgeMessage.AssemblerHold(7L, 0, "x").Serialize());
            t.Inbox.Add(BridgeMessage.AssemblerHold(8L, -1, "y").Serialize());
            bridge.Tick(0);

            bridge.IsAssemblerHeld(7L).Should().BeFalse();
            bridge.IsAssemblerHeld(8L).Should().BeFalse();
        }
    }
}
