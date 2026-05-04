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
        const long RemoteId = 5;
        const long RemoteTopId = 6;

        static Program.MechanicalEdge Mech(long baseId, long topId, bool attached = true, bool noSubgridTag = false) {
            return new Program.MechanicalEdge {
                BaseGridId = baseId,
                TopGridId = topId,
                Attached = attached,
                NoSubgridTag = noSubgridTag
            };
        }

        static Program.ConnectorEdge Conn(long ownerId, long otherId, bool connected = true, bool federateTag = true) {
            return new Program.ConnectorEdge {
                OwnerGridId = ownerId,
                OtherGridId = otherId,
                Connected = connected,
                FederateTag = federateTag
            };
        }

        static HashSet<long> Run(IList<Program.MechanicalEdge> mech) {
            var output = new HashSet<long>();
            Program.BuildScope(RootId, mech, new List<Program.ConnectorEdge>(), false, output);
            return output;
        }

        static HashSet<long> RunFull(IList<Program.MechanicalEdge> mech, IList<Program.ConnectorEdge> conn, bool enableFederation = true) {
            var output = new HashSet<long>();
            Program.BuildScope(RootId, mech, conn, enableFederation, output);
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

        [Fact]
        public void Disconnected_federate_connector_does_not_extend_scope() {
            RunFull(
                new List<Program.MechanicalEdge>(),
                new List<Program.ConnectorEdge> { Conn(RootId, RemoteId, connected: false) }
            ).Should().BeEquivalentTo(new[] { RootId });
        }

        [Fact]
        public void Connected_federate_connector_admits_remote_grid() {
            RunFull(
                new List<Program.MechanicalEdge>(),
                new List<Program.ConnectorEdge> { Conn(RootId, RemoteId) }
            ).Should().BeEquivalentTo(new[] { RootId, RemoteId });
        }

        [Fact]
        public void Connected_connector_without_federate_tag_is_ignored() {
            RunFull(
                new List<Program.MechanicalEdge>(),
                new List<Program.ConnectorEdge> { Conn(RootId, RemoteId, federateTag: false) }
            ).Should().BeEquivalentTo(new[] { RootId });
        }

        [Fact]
        public void EnableFederation_disabled_overrides_tag() {
            RunFull(
                new List<Program.MechanicalEdge>(),
                new List<Program.ConnectorEdge> { Conn(RootId, RemoteId) },
                enableFederation: false
            ).Should().BeEquivalentTo(new[] { RootId });
        }

        [Fact]
        public void Federated_grid_mechanical_subgrid_is_transitively_included() {
            RunFull(
                new List<Program.MechanicalEdge> { Mech(RemoteId, RemoteTopId) },
                new List<Program.ConnectorEdge> { Conn(RootId, RemoteId) }
            ).Should().BeEquivalentTo(new[] { RootId, RemoteId, RemoteTopId });
        }

        [Fact]
        public void Federation_does_not_chain_through_a_remote_grid() {
            RunFull(
                new List<Program.MechanicalEdge>(),
                new List<Program.ConnectorEdge> {
                    Conn(RootId, RemoteId),
                    Conn(RemoteId, 7)
                }
            ).Should().BeEquivalentTo(new[] { RootId, RemoteId });
        }

        [Fact]
        public void Cycle_through_federated_connector_terminates() {
            RunFull(
                new List<Program.MechanicalEdge> { Mech(RemoteId, RootId) },
                new List<Program.ConnectorEdge> { Conn(RootId, RemoteId) }
            ).Should().BeEquivalentTo(new[] { RootId, RemoteId });
        }
    }
}
