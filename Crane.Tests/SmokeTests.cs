using FluentAssertions;
using VRage.Game.ModAPI.Ingame;
using Xunit;

namespace Crane.Tests
{
    /// <summary>One-shot environmental checks: confirm the SE reference assemblies are usable in tests.</summary>
    public class SmokeTests
    {
        /// <summary>Verifies <see cref="MyItemType"/> can be produced via a static factory at test runtime.</summary>
        [Fact]
        public void MyItemType_make_ore_works()
        {
            var type = MyItemType.MakeOre("Iron");
            type.TypeId.Should().Be("MyObjectBuilder_Ore");
            type.SubtypeId.Should().Be("Iron");
        }
    }
}
