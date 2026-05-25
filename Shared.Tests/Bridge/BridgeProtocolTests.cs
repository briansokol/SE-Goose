using System.Collections.Generic;
using FluentAssertions;
using IngameScript;
using Xunit;

namespace Shared.Tests
{
    /// <summary>Protocol-level tests: bootstrap, heartbeats, snapshot triggers, version handling, and the catalog-key callback.</summary>
    public class BridgeProtocolTests
    {
        private static Bridge NewBridge(FakeBridgeTransport transport, BridgeRole role,
            List<string> peerKeys, List<string> warnings, int catalogCount = 0)
        {
            var bridge = new Bridge(
                transport,
                role,
                key => peerKeys.Add(key),
                () => catalogCount,
                () => new List<string>(),
                w => warnings.Add(w));
            bridge.HeartbeatTicks = 10;
            bridge.SnapshotDebounceTicks = 100;
            bridge.PeerStaleMultiplier = 3;
            bridge.HoldDriftThreshold = 5;
            return bridge;
        }

        [Fact]
        public void Initialize_sends_hello_immediately()
        {
            var t = new FakeBridgeTransport();
            Bridge bridge = NewBridge(t, BridgeRole.Goose, new List<string>(), new List<string>());

            bridge.Initialize();

            t.CountKind(BridgeProtocol.KindHello).Should().Be(1);
        }

        [Fact]
        public void Initialize_is_idempotent()
        {
            var t = new FakeBridgeTransport();
            Bridge bridge = NewBridge(t, BridgeRole.Goose, new List<string>(), new List<string>());

            bridge.Initialize();
            bridge.Initialize();

            t.CountKind(BridgeProtocol.KindHello).Should().Be(1);
        }

        [Fact]
        public void Disabled_bridge_sends_nothing()
        {
            var t = new FakeBridgeTransport();
            Bridge bridge = NewBridge(t, BridgeRole.Goose, new List<string>(), new List<string>());
            bridge.Enabled = false;

            bridge.Initialize();
            bridge.Tick(0);
            bridge.AnnounceCatalogAdd("Ingot/Iron");
            bridge.AnnounceAssemblerHold(1L, 100, "Ingot/Iron:1");

            t.Sent.Should().BeEmpty();
        }

        [Fact]
        public void Heartbeat_fires_on_cadence_after_peer_seen()
        {
            var t = new FakeBridgeTransport();
            var keys = new List<string>();
            Bridge bridge = NewBridge(t, BridgeRole.Goose, keys, new List<string>());
            bridge.Initialize();
            t.Inbox.Add(BridgeMessage.Hello(BridgeRole.Crane).Serialize());

            bridge.Tick(0);
            int sentBeforeCadence = t.Sent.Count;
            bridge.Tick(5);
            t.Sent.Count.Should().Be(sentBeforeCadence);

            bridge.Tick(10);
            t.CountKind(BridgeProtocol.KindHeartbeat).Should().Be(1);
        }

        [Fact]
        public void While_peer_unknown_subsequent_emits_send_hello_not_heartbeat()
        {
            var t = new FakeBridgeTransport();
            Bridge bridge = NewBridge(t, BridgeRole.Goose, new List<string>(), new List<string>());
            bridge.Initialize();

            bridge.Tick(0);
            bridge.Tick(10);
            bridge.Tick(20);

            t.CountKind(BridgeProtocol.KindHello).Should().BeGreaterOrEqualTo(2);
            t.CountKind(BridgeProtocol.KindHeartbeat).Should().Be(0);
        }

        [Fact]
        public void Hello_from_peer_triggers_snapshot_when_catalog_nonempty()
        {
            var t = new FakeBridgeTransport();
            var bridge = new Bridge(
                t,
                BridgeRole.Goose,
                _ => { },
                () => 3,
                () => new List<string> { "Ingot/Iron", "Ingot/Cobalt", "Ingot/Silver" },
                _ => { });
            bridge.HeartbeatTicks = 10;
            bridge.SnapshotDebounceTicks = 100;
            bridge.Initialize();

            t.Inbox.Add(BridgeMessage.Hello(BridgeRole.Crane).Serialize());
            bridge.Tick(0);

            t.CountKind(BridgeProtocol.KindCatalogSnapshot).Should().Be(1);
        }

        [Fact]
        public void Snapshot_is_debounced_within_window()
        {
            var t = new FakeBridgeTransport();
            var bridge = new Bridge(
                t,
                BridgeRole.Goose,
                _ => { },
                () => 1,
                () => new List<string> { "Ingot/Iron" },
                _ => { });
            bridge.HeartbeatTicks = 10;
            bridge.SnapshotDebounceTicks = 100;
            bridge.Initialize();

            t.Inbox.Add(BridgeMessage.Hello(BridgeRole.Crane).Serialize());
            bridge.Tick(0);

            t.Inbox.Add(BridgeMessage.Hello(BridgeRole.Crane).Serialize());
            bridge.Tick(50);

            t.CountKind(BridgeProtocol.KindCatalogSnapshot).Should().Be(1);

            t.Inbox.Add(BridgeMessage.Hello(BridgeRole.Crane).Serialize());
            bridge.Tick(200);
            t.CountKind(BridgeProtocol.KindCatalogSnapshot).Should().Be(2);
        }

        [Fact]
        public void Heartbeat_with_catalog_drift_triggers_snapshot()
        {
            var t = new FakeBridgeTransport();
            var keys = new List<string>();
            for (int i = 0; i < 20; i++)
            {
                keys.Add("Ingot/" + i);
            }

            var bridge = new Bridge(
                t,
                BridgeRole.Goose,
                _ => { },
                () => keys.Count,
                () => keys,
                _ => { });
            bridge.HeartbeatTicks = 10;
            bridge.SnapshotDebounceTicks = 100;
            bridge.HoldDriftThreshold = 5;
            bridge.Initialize();

            t.Inbox.Add(BridgeMessage.Heartbeat(BridgeRole.Crane, 10).Serialize());
            bridge.Tick(0);

            t.CountKind(BridgeProtocol.KindCatalogSnapshot).Should().BeGreaterOrEqualTo(1);
        }

        [Fact]
        public void Heartbeat_with_small_drift_does_not_trigger_snapshot()
        {
            var t = new FakeBridgeTransport();
            var keys = new List<string>();
            for (int i = 0; i < 7; i++)
            {
                keys.Add("Ingot/" + i);
            }

            var bridge = new Bridge(
                t,
                BridgeRole.Goose,
                _ => { },
                () => keys.Count,
                () => keys,
                _ => { });
            bridge.HeartbeatTicks = 10;
            bridge.SnapshotDebounceTicks = 100;
            bridge.HoldDriftThreshold = 5;
            bridge.Initialize();

            t.Inbox.Add(BridgeMessage.Heartbeat(BridgeRole.Crane, 4).Serialize());
            bridge.Tick(0);

            t.CountKind(BridgeProtocol.KindCatalogSnapshot).Should().Be(0);
        }

        [Fact]
        public void Hello_with_wrong_version_is_ignored_and_warns()
        {
            var t = new FakeBridgeTransport();
            var warnings = new List<string>();
            var bridge = new Bridge(
                t,
                BridgeRole.Goose,
                _ => { },
                () => 5,
                () => new List<string> { "A/B" },
                w => warnings.Add(w));
            bridge.HeartbeatTicks = 10;
            bridge.Initialize();

            string wrongVersionHello = "kind=hello;role=crane;v=99";
            t.Inbox.Add(wrongVersionHello);
            bridge.Tick(0);

            t.CountKind(BridgeProtocol.KindCatalogSnapshot).Should().Be(0);
            bridge.PeerStatus.Linked.Should().BeFalse();
            warnings.Should().NotBeEmpty();
        }

        [Fact]
        public void CatalogAdd_invokes_callback_with_key()
        {
            var t = new FakeBridgeTransport();
            var keys = new List<string>();
            Bridge bridge = NewBridge(t, BridgeRole.Goose, keys, new List<string>());
            bridge.Initialize();

            t.Inbox.Add(BridgeMessage.CatalogAdd("Ingot/Iron").Serialize());
            bridge.Tick(0);

            keys.Should().ContainSingle().Which.Should().Be("Ingot/Iron");
        }

        [Fact]
        public void CatalogSnapshot_invokes_callback_per_key()
        {
            var t = new FakeBridgeTransport();
            var keys = new List<string>();
            Bridge bridge = NewBridge(t, BridgeRole.Goose, keys, new List<string>());
            bridge.Initialize();

            BridgeMessage snap = new BridgeMessage(BridgeProtocol.KindCatalogSnapshot)
                .Set(BridgeProtocol.KeyKeys, "A/B|C/D|E/F");
            t.Inbox.Add(snap.Serialize());
            bridge.Tick(0);

            keys.Should().Equal(new[] { "A/B", "C/D", "E/F" });
        }

        [Fact]
        public void AnnounceCatalogAdd_emits_single_message()
        {
            var t = new FakeBridgeTransport();
            Bridge bridge = NewBridge(t, BridgeRole.Goose, new List<string>(), new List<string>());
            bridge.Initialize();

            bridge.AnnounceCatalogAdd("Ingot/Iron");

            t.CountKind(BridgeProtocol.KindCatalogAdd).Should().Be(1);
        }

        [Fact]
        public void AnnounceCatalogAdd_ignores_empty_key()
        {
            var t = new FakeBridgeTransport();
            Bridge bridge = NewBridge(t, BridgeRole.Goose, new List<string>(), new List<string>());
            bridge.Initialize();

            int before = t.CountKind(BridgeProtocol.KindCatalogAdd);
            bridge.AnnounceCatalogAdd(null);
            bridge.AnnounceCatalogAdd(string.Empty);

            t.CountKind(BridgeProtocol.KindCatalogAdd).Should().Be(before);
        }
    }
}
