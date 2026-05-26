using System.Collections.Generic;
using FluentAssertions;
using IngameScript;
using Xunit;

namespace Shared.Tests
{
    /// <summary>Round-trip and edge-case tests for <see cref="BridgeMessage"/> parse/serialize.</summary>
    public class BridgeMessageTests
    {
        [Fact]
        public void Hello_round_trips_role_and_version()
        {
            var src = BridgeMessage.Hello(BridgeRole.Goose);
            string wire = src.Serialize();

            var parsed = BridgeMessage.Parse(wire);
            parsed.Should().NotBeNull();
            parsed.Kind.Should().Be(BridgeProtocol.KindHello);
            parsed.GetString(BridgeProtocol.KeyRole, null).Should().Be(BridgeProtocol.RoleGoose);
            parsed.GetInt(BridgeProtocol.KeyVersion, -1).Should().Be(BridgeProtocol.ProtocolVersion);
        }

        [Fact]
        public void Heartbeat_round_trips_catalog_count()
        {
            var src = BridgeMessage.Heartbeat(BridgeRole.Crane, 42);
            var parsed = BridgeMessage.Parse(src.Serialize());

            parsed.Kind.Should().Be(BridgeProtocol.KindHeartbeat);
            parsed.GetString(BridgeProtocol.KeyRole, null).Should().Be(BridgeProtocol.RoleCrane);
            parsed.GetInt(BridgeProtocol.KeyCatalogCount, -1).Should().Be(42);
        }

        [Fact]
        public void CatalogAdd_round_trips_key()
        {
            var src = BridgeMessage.CatalogAdd("Ingot/Iron");
            var parsed = BridgeMessage.Parse(src.Serialize());

            parsed.Kind.Should().Be(BridgeProtocol.KindCatalogAdd);
            parsed.GetString(BridgeProtocol.KeyKey, null).Should().Be("Ingot/Iron");
        }

        [Fact]
        public void AssemblerHold_round_trips_id_ttl_need()
        {
            var src = BridgeMessage.AssemblerHold(123456789L, 300, "Ingot/Iron:200,Ingot/Cobalt:50");
            var parsed = BridgeMessage.Parse(src.Serialize());

            parsed.Kind.Should().Be(BridgeProtocol.KindAssemblerHold);
            parsed.GetLong(BridgeProtocol.KeyEntityId, -1).Should().Be(123456789L);
            parsed.GetInt(BridgeProtocol.KeyTtl, -1).Should().Be(300);
            parsed.GetString(BridgeProtocol.KeyNeed, null).Should().Be("Ingot/Iron:200,Ingot/Cobalt:50");
        }

        [Fact]
        public void Parse_returns_null_for_empty_payload()
        {
            BridgeMessage.Parse(null).Should().BeNull();
            BridgeMessage.Parse(string.Empty).Should().BeNull();
        }

        [Fact]
        public void Parse_returns_null_when_kind_missing()
        {
            BridgeMessage.Parse("role=goose;v=1").Should().BeNull();
        }

        [Fact]
        public void Parse_returns_null_on_malformed_pair()
        {
            BridgeMessage.Parse("kind=hello;malformed").Should().BeNull();
        }

        [Fact]
        public void Snapshot_chunking_emits_a_single_chunk_when_under_budget()
        {
            var keys = new List<string> { "Ingot/Iron", "Ingot/Cobalt", "Ingot/Silver" };

            var chunks = new List<BridgeMessage>(
                BridgeMessage.CatalogSnapshotChunks(keys, 2048));

            chunks.Should().HaveCount(1);
            chunks[0].Kind.Should().Be(BridgeProtocol.KindCatalogSnapshot);
            chunks[0].GetString(BridgeProtocol.KeyKeys, null)
                .Should().Be("Ingot/Iron|Ingot/Cobalt|Ingot/Silver");
        }

        [Fact]
        public void Snapshot_chunking_splits_long_payload_and_union_recovers_input()
        {
            var keys = new List<string>();
            for (int i = 0; i < 300; i++)
            {
                keys.Add("Ingot/" + i.ToString("000"));
            }

            var chunks = new List<BridgeMessage>(
                BridgeMessage.CatalogSnapshotChunks(keys, 200));

            chunks.Count.Should().BeGreaterThan(1);

            var union = new List<string>();
            foreach (BridgeMessage chunk in chunks)
            {
                chunk.Serialize().Length.Should().BeLessOrEqualTo(200);
                string keysField = chunk.GetString(BridgeProtocol.KeyKeys, string.Empty);
                union.AddRange(keysField.Split(BridgeProtocol.KeysDelimiter));
            }

            union.Should().Equal(keys);
        }

        [Fact]
        public void Snapshot_chunking_skips_null_and_empty_keys()
        {
            var keys = new List<string> { "A/B", null, "C/D", "" };

            var chunks = new List<BridgeMessage>(
                BridgeMessage.CatalogSnapshotChunks(keys, 2048));

            chunks.Should().HaveCount(1);
            chunks[0].GetString(BridgeProtocol.KeyKeys, null).Should().Be("A/B|C/D");
        }

        [Fact]
        public void Snapshot_chunking_returns_empty_for_null_or_empty_input()
        {
            new List<BridgeMessage>(BridgeMessage.CatalogSnapshotChunks(null, 2048)).Should().BeEmpty();
            new List<BridgeMessage>(BridgeMessage.CatalogSnapshotChunks(new List<string>(), 2048)).Should().BeEmpty();
        }

        [Fact]
        public void GetInt_returns_fallback_for_unparseable_value()
        {
            var msg = BridgeMessage.Parse("kind=hello;v=abc");
            msg.GetInt(BridgeProtocol.KeyVersion, 99).Should().Be(99);
        }
    }
}
