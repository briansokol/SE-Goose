using System.Collections.Generic;
using FluentAssertions;
using IngameScript;
using VRage.Game.ModAPI.Ingame;
using Xunit;

namespace Crane.Tests
{
    /// <summary>Tests for <see cref="AutocraftCounts.EffectiveActual"/> — the Goose-count-vs-local-scan fallback logic.</summary>
    public class AutocraftCountsTests
    {
        private static readonly MyItemType SteelPlate = MyItemType.MakeComponent("SteelPlate");
        private const string SteelPlateKey = "Component/SteelPlate";

        [Fact]
        public void Prefers_goose_count_when_linked_and_present()
        {
            var grid = new Dictionary<string, long> { { SteelPlateKey, 5000 } };
            var local = new Dictionary<MyItemType, long> { { SteelPlate, 12 } };

            AutocraftCounts.EffectiveActual(true, grid, SteelPlateKey, local, SteelPlate)
                .Should().Be(5000);
        }

        [Fact]
        public void Falls_back_to_local_when_peer_unlinked()
        {
            var grid = new Dictionary<string, long> { { SteelPlateKey, 5000 } };
            var local = new Dictionary<MyItemType, long> { { SteelPlate, 12 } };

            AutocraftCounts.EffectiveActual(false, grid, SteelPlateKey, local, SteelPlate)
                .Should().Be(12);
        }

        [Fact]
        public void Falls_back_to_local_when_key_not_yet_received()
        {
            var grid = new Dictionary<string, long>();
            var local = new Dictionary<MyItemType, long> { { SteelPlate, 12 } };

            AutocraftCounts.EffectiveActual(true, grid, SteelPlateKey, local, SteelPlate)
                .Should().Be(12);
        }

        [Fact]
        public void Returns_zero_when_neither_source_has_the_item()
        {
            var grid = new Dictionary<string, long>();
            var local = new Dictionary<MyItemType, long>();

            AutocraftCounts.EffectiveActual(true, grid, SteelPlateKey, local, SteelPlate)
                .Should().Be(0);
        }

        [Fact]
        public void Goose_zero_is_authoritative_over_local_when_linked()
        {
            var grid = new Dictionary<string, long> { { SteelPlateKey, 0 } };
            var local = new Dictionary<MyItemType, long> { { SteelPlate, 12 } };

            AutocraftCounts.EffectiveActual(true, grid, SteelPlateKey, local, SteelPlate)
                .Should().Be(0);
        }
    }
}
