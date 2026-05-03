using System.Collections.Generic;
using FluentAssertions;
using IngameScript;
using Xunit;

namespace Goose.Tests {
    /// <summary>Tests for the pure BFS in <c>Program.Scope.cs</c>. Uses the POCO edge inputs so we never touch the SE runtime.</summary>
    public class ScopeTests {
        const long RootId = 1;
        const long MidId = 2;
        const long LeafId = 3;

        static Program.MechanicalEdge Mech(long baseId, long topId, bool attached = true, bool noSubgridTag = false) {
            return new Program.MechanicalEdge {
                BaseGridId = baseId,
                TopGridId = topId,
                Attached = attached,
                NoSubgridTag = noSubgridTag
            };
        }

        static HashSet<long> Run(IList<Program.MechanicalEdge> mech) {
            var output = new HashSet<long>();
            Program.BuildScope(RootId, mech, new List<Program.ConnectorEdge>(), false, output);
            return output;
        }

        [Fact]
        public void Bare_grid_with_no_edges_returns_root_only() {
            Run(new List<Program.MechanicalEdge>())
                .Should().BeEquivalentTo(new[] { RootId });
        }

        [Fact]
        public void Single_attached_rotor_includes_top_grid() {
            Run(new List<Program.MechanicalEdge> { Mech(RootId, MidId) })
                .Should().BeEquivalentTo(new[] { RootId, MidId });
        }

        [Fact]
        public void Two_step_chain_includes_every_grid() {
            Run(new List<Program.MechanicalEdge> {
                Mech(RootId, MidId),
                Mech(MidId, LeafId)
            }).Should().BeEquivalentTo(new[] { RootId, MidId, LeafId });
        }

        [Fact]
        public void Detached_rotor_does_not_extend_scope() {
            Run(new List<Program.MechanicalEdge> { Mech(RootId, MidId, attached: false) })
                .Should().BeEquivalentTo(new[] { RootId });
        }

        [Fact]
        public void Mechanical_cycle_terminates() {
            Run(new List<Program.MechanicalEdge> {
                Mech(RootId, MidId),
                Mech(MidId, RootId)
            }).Should().BeEquivalentTo(new[] { RootId, MidId });
        }

        [Fact]
        public void NoSubgrid_tag_blocks_extension_past_the_rotor() {
            Run(new List<Program.MechanicalEdge> {
                Mech(RootId, MidId, noSubgridTag: true)
            }).Should().BeEquivalentTo(new[] { RootId });
        }

        [Fact]
        public void NoSubgrid_mid_chain_cuts_off_descendants() {
            Run(new List<Program.MechanicalEdge> {
                Mech(RootId, MidId),
                Mech(MidId, LeafId, noSubgridTag: true)
            }).Should().BeEquivalentTo(new[] { RootId, MidId });
        }
    }
}
