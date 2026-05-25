using FluentAssertions;
using Goose.Tests.Fakes;
using IngameScript;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;
using Xunit;

namespace Goose.Tests
{
    /// <summary>Tests for <see cref="Program.GetSortableInventory"/>: the sorter's per-block
    /// "which side do I drain?" decision.</summary>
    public class SortingTests
    {
        public class GetSortableInventory_Assembler_Tests
        {
            /// <summary>Assembly mode drains the output side (finished components).</summary>
            [Fact]
            public void Assembly_mode_returns_output_inventory()
            {
                IMyInventory input = new FakeInventory();
                IMyInventory output = new FakeInventory();
                var asm = new FakeAssembler { Mode = MyAssemblerMode.Assembly, InputInventory = input, OutputInventory = output };

                IMyInventory result = Program.GetSortableInventory(asm);

                result.Should().BeSameAs(output);
            }

            /// <summary>Disassembly mode drains the input side (where ingots come out).</summary>
            [Fact]
            public void Disassembly_mode_returns_input_inventory()
            {
                IMyInventory input = new FakeInventory();
                IMyInventory output = new FakeInventory();
                var asm = new FakeAssembler { Mode = MyAssemblerMode.Disassembly, InputInventory = input, OutputInventory = output };

                IMyInventory result = Program.GetSortableInventory(asm);

                result.Should().BeSameAs(input);
            }
        }
    }
}
