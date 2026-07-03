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

        /// <summary>Tests for <see cref="Program.IsGenericPullSource"/>: pass-3 source
        /// eligibility when a stock container pulls an item from generic inventories.</summary>
        public class IsGenericPullSource_Tests
        {
            /// <summary>Blocks with no ContainerEntry (e.g. bare inventories) are valid pull sources.</summary>
            [Fact]
            public void Null_entry_is_eligible()
            {
                Program.IsGenericPullSource(null, Program.ItemCategory.Meals).Should().BeTrue();
            }

            /// <summary>Stock containers are excluded; pass 1 (stock-to-stock excess) owns them.</summary>
            [Fact]
            public void Stock_entry_is_not_eligible()
            {
                var entry = new Program.ContainerEntry { IsStock = true };

                Program.IsGenericPullSource(entry, Program.ItemCategory.Meals).Should().BeFalse();
            }

            /// <summary>Containers tagged for the item's own category are excluded; pass 2 (category routes) owns them.</summary>
            [Fact]
            public void Entry_tagged_for_items_category_is_not_eligible()
            {
                var entry = new Program.ContainerEntry();
                entry.Categories.Add(Program.ItemCategory.Ammo);

                Program.IsGenericPullSource(entry, Program.ItemCategory.Ammo).Should().BeFalse();
            }

            /// <summary>Regression: a container tagged for a DIFFERENT category (Steak Dinner
            /// sitting in "Cargo Ammo") must remain a valid pull source for the foreign item.</summary>
            [Fact]
            public void Entry_tagged_for_other_category_is_eligible()
            {
                var entry = new Program.ContainerEntry();
                entry.Categories.Add(Program.ItemCategory.Ammo);

                Program.IsGenericPullSource(entry, Program.ItemCategory.Meals).Should().BeTrue();
            }

            /// <summary>Untagged, non-stock entries are valid pull sources.</summary>
            [Fact]
            public void Untagged_entry_is_eligible()
            {
                var entry = new Program.ContainerEntry();

                Program.IsGenericPullSource(entry, Program.ItemCategory.Meals).Should().BeTrue();
            }
        }
    }
}
