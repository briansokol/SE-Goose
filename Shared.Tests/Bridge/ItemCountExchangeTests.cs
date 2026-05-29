using System.Collections.Generic;
using FluentAssertions;
using IngameScript;
using Xunit;

namespace Shared.Tests
{
    /// <summary>Tests covering the Crane-requests / Goose-responds grid-wide item-count exchange.</summary>
    public class ItemCountExchangeTests
    {
        private static Bridge NewBridge(FakeBridgeTransport transport, BridgeRole role)
        {
            var bridge = new Bridge(
                transport,
                role,
                _ => { },
                () => 0,
                () => new List<string>(),
                _ => { });
            bridge.HeartbeatTicks = 10;
            bridge.SnapshotDebounceTicks = 100;
            return bridge;
        }

        [Fact]
        public void ItemCountRequest_round_trips()
        {
            var msg = BridgeMessage.ItemCountRequest(new[] { "Component/SteelPlate", "Ingot/Iron" });
            var parsed = BridgeMessage.Parse(msg.Serialize());

            parsed.Kind.Should().Be(BridgeProtocol.KindItemCountRequest);
            parsed.GetString(BridgeProtocol.KeyKeys, null).Should().Be("Component/SteelPlate|Ingot/Iron");
        }

        [Fact]
        public void ItemCountResponse_round_trips()
        {
            var counts = new List<KeyValuePair<string, long>>
            {
                new KeyValuePair<string, long>("Component/SteelPlate", 5000),
                new KeyValuePair<string, long>("Ingot/Iron", 320),
            };
            var msg = BridgeMessage.ItemCountResponse(counts, BridgeProtocol.SnapshotMaxPayloadChars, null);
            var parsed = BridgeMessage.Parse(msg.Serialize());

            parsed.Kind.Should().Be(BridgeProtocol.KindItemCountResponse);
            parsed.GetString(BridgeProtocol.KeyCounts, null).Should().Be("Component/SteelPlate:5000|Ingot/Iron:320");
        }

        [Fact]
        public void Goose_replies_to_request_with_resolved_counts_including_zero_for_unknown()
        {
            var t = new FakeBridgeTransport();
            Bridge goose = NewBridge(t, BridgeRole.Goose);
            var grid = new Dictionary<string, long> { { "Component/SteelPlate", 5000 } };
            goose.SetItemCountResponder(key =>
            {
                long v;
                return grid.TryGetValue(key, out v) ? v : 0;
            });
            goose.Initialize();

            t.Inbox.Add(BridgeMessage.ItemCountRequest(new[] { "Component/SteelPlate", "Ingot/Iron" }).Serialize());
            goose.Tick(0);

            int idx = t.FindFirstKindIndex(BridgeProtocol.KindItemCountResponse);
            idx.Should().BeGreaterThanOrEqualTo(0);
            var reply = BridgeMessage.Parse(t.Sent[idx]);
            reply.GetString(BridgeProtocol.KeyCounts, null).Should().Be("Component/SteelPlate:5000|Ingot/Iron:0");
        }

        [Fact]
        public void Crane_dispatches_each_count_entry_to_handler()
        {
            var t = new FakeBridgeTransport();
            Bridge crane = NewBridge(t, BridgeRole.Crane);
            var received = new Dictionary<string, long>();
            crane.SetOnPeerItemCount((key, amount) => received[key] = amount);
            crane.Initialize();

            var counts = new List<KeyValuePair<string, long>>
            {
                new KeyValuePair<string, long>("Component/SteelPlate", 5000),
                new KeyValuePair<string, long>("Ingot/Iron", 320),
            };
            t.Inbox.Add(BridgeMessage.ItemCountResponse(counts, BridgeProtocol.SnapshotMaxPayloadChars, null).Serialize());
            crane.Tick(0);

            received.Should().HaveCount(2);
            received["Component/SteelPlate"].Should().Be(5000);
            received["Ingot/Iron"].Should().Be(320);
        }

        [Fact]
        public void Crane_skips_malformed_count_entries()
        {
            var t = new FakeBridgeTransport();
            Bridge crane = NewBridge(t, BridgeRole.Crane);
            var received = new Dictionary<string, long>();
            crane.SetOnPeerItemCount((key, amount) => received[key] = amount);
            crane.Initialize();

            t.Inbox.Add("kind=" + BridgeProtocol.KindItemCountResponse
                + ";counts=Component/SteelPlate:5000|garbage|Ingot/Iron:notanumber|Ingot/Cobalt:50");
            crane.Tick(0);

            received.Should().HaveCount(2);
            received["Component/SteelPlate"].Should().Be(5000);
            received["Ingot/Cobalt"].Should().Be(50);
        }

        [Fact]
        public void Goose_ignores_a_response_addressed_pattern_and_Crane_ignores_request()
        {
            var t = new FakeBridgeTransport();
            Bridge crane = NewBridge(t, BridgeRole.Crane);
            crane.SetItemCountResponder(_ => 999);
            crane.Initialize();

            t.Inbox.Add(BridgeMessage.ItemCountRequest(new[] { "Component/SteelPlate" }).Serialize());
            crane.Tick(0);

            t.FindFirstKindIndex(BridgeProtocol.KindItemCountResponse).Should().Be(-1);
        }

        [Fact]
        public void Response_payload_is_capped_and_truncation_flagged()
        {
            var counts = new List<KeyValuePair<string, long>>();
            for (int i = 0; i < 500; i++)
            {
                counts.Add(new KeyValuePair<string, long>("Component/VeryLongSubtypeName" + i, 123456));
            }
            bool truncated = false;
            var msg = BridgeMessage.ItemCountResponse(counts, BridgeProtocol.SnapshotMaxPayloadChars,
                () => truncated = true);

            truncated.Should().BeTrue();
            msg.Serialize().Length.Should().BeLessThanOrEqualTo(BridgeProtocol.SnapshotMaxPayloadChars);
        }

        [Fact]
        public void Crane_can_request_counts()
        {
            var t = new FakeBridgeTransport();
            Bridge crane = NewBridge(t, BridgeRole.Crane);
            crane.Initialize();

            crane.RequestItemCounts(new[] { "Component/SteelPlate", "Ingot/Iron" });

            int idx = t.FindFirstKindIndex(BridgeProtocol.KindItemCountRequest);
            idx.Should().BeGreaterThanOrEqualTo(0);
            BridgeMessage.Parse(t.Sent[idx]).GetString(BridgeProtocol.KeyKeys, null)
                .Should().Be("Component/SteelPlate|Ingot/Iron");
        }
    }
}
