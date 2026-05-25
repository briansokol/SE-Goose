using FluentAssertions;
using IngameScript;
using Xunit;

namespace Shared.Tests
{
    /// <summary>Tests for <see cref="BlueprintMisses"/>.</summary>
    public class BlueprintMissesTests
    {
        [Theory]
        [InlineData("Construction", "ConstructionComponent")]
        [InlineData("Computer", "ComputerComponent")]
        [InlineData("Detector", "DetectorComponent")]
        [InlineData("Explosives", "ExplosivesComponent")]
        [InlineData("Girder", "GirderComponent")]
        [InlineData("Medical", "MedicalComponent")]
        [InlineData("Motor", "MotorComponent")]
        [InlineData("RadioCommunication", "RadioCommunicationComponent")]
        [InlineData("Reactor", "ReactorComponent")]
        [InlineData("Thrust", "ThrustComponent")]
        public void Map_contains_curated_entry(string item, string blueprint)
        {
            BlueprintMisses.CuratedMap.Should().ContainKey(item);
            BlueprintMisses.CuratedMap[item].Should().Be(blueprint);
        }

        [Fact]
        public void Map_has_ten_entries()
        {
            BlueprintMisses.CuratedMap.Should().HaveCount(10);
        }
    }
}
