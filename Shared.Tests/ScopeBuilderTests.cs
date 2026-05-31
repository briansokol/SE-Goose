using System.Collections.Generic;
using FluentAssertions;
using IngameScript;
using Xunit;

namespace Shared.Tests
{
    /// <summary>Tests for <see cref="ScopeBuilder"/>.</summary>
    public class ScopeBuilderTests
    {
        [Fact]
        public void Root_only_when_no_edges()
        {
            var output = new HashSet<long>();
            ScopeBuilder.BuildScope(1, new List<MechanicalEdge>(), new List<ConnectorEdge>(), true, output);
            output.Should().BeEquivalentTo(new long[] { 1 });
        }

        [Fact]
        public void Mechanical_edges_extend_scope_when_attached()
        {
            var output = new HashSet<long>();
            var mech = new List<MechanicalEdge> {
                new MechanicalEdge { BaseGridId = 1, TopGridId = 2, Attached = true },
                new MechanicalEdge { BaseGridId = 2, TopGridId = 3, Attached = true }
            };
            ScopeBuilder.BuildScope(1, mech, new List<ConnectorEdge>(), true, output);
            output.Should().BeEquivalentTo(new long[] { 1, 2, 3 });
        }

        [Fact]
        public void Detached_edges_do_not_extend()
        {
            var output = new HashSet<long>();
            var mech = new List<MechanicalEdge> {
                new MechanicalEdge { BaseGridId = 1, TopGridId = 2, Attached = false }
            };
            ScopeBuilder.BuildScope(1, mech, new List<ConnectorEdge>(), true, output);
            output.Should().BeEquivalentTo(new long[] { 1 });
        }

        [Fact]
        public void NoSubgrid_tag_blocks_traversal()
        {
            var output = new HashSet<long>();
            var mech = new List<MechanicalEdge> {
                new MechanicalEdge { BaseGridId = 1, TopGridId = 2, Attached = true, NoSubgridTag = true }
            };
            ScopeBuilder.BuildScope(1, mech, new List<ConnectorEdge>(), true, output);
            output.Should().BeEquivalentTo(new long[] { 1 });
        }

        [Fact]
        public void Federate_connector_crosses_when_connected_and_tagged()
        {
            var output = new HashSet<long>();
            var conn = new List<ConnectorEdge> {
                new ConnectorEdge { OwnerGridId = 1, OtherGridId = 9, Connected = true, FederateTag = true }
            };
            ScopeBuilder.BuildScope(1, new List<MechanicalEdge>(), conn, true, output);
            output.Should().BeEquivalentTo(new long[] { 1, 9 });
        }

        [Fact]
        public void Federation_disabled_ignores_connectors()
        {
            var output = new HashSet<long>();
            var conn = new List<ConnectorEdge> {
                new ConnectorEdge { OwnerGridId = 1, OtherGridId = 9, Connected = true, FederateTag = true }
            };
            ScopeBuilder.BuildScope(1, new List<MechanicalEdge>(), conn, false, output);
            output.Should().BeEquivalentTo(new long[] { 1 });
        }

        [Fact]
        public void Untagged_or_disconnected_connector_does_not_cross()
        {
            var output = new HashSet<long>();
            var conn = new List<ConnectorEdge> {
                new ConnectorEdge { OwnerGridId = 1, OtherGridId = 9, Connected = false, FederateTag = true },
                new ConnectorEdge { OwnerGridId = 1, OtherGridId = 8, Connected = true, FederateTag = false }
            };
            ScopeBuilder.BuildScope(1, new List<MechanicalEdge>(), conn, true, output);
            output.Should().BeEquivalentTo(new long[] { 1 });
        }

        [Fact]
        public void Drift_hash_stable_under_reorder()
        {
            var mech1 = new List<MechanicalEdge> {
                new MechanicalEdge { BaseGridId = 1, TopGridId = 2, Attached = true },
                new MechanicalEdge { BaseGridId = 2, TopGridId = 3, Attached = true }
            };
            var mech2 = new List<MechanicalEdge> {
                new MechanicalEdge { BaseGridId = 1, TopGridId = 2, Attached = true },
                new MechanicalEdge { BaseGridId = 2, TopGridId = 3, Attached = true }
            };
            ScopeBuilder.ComputeScopeDriftHash(mech1, null)
                .Should().Be(ScopeBuilder.ComputeScopeDriftHash(mech2, null));
        }

        [Fact]
        public void Drift_hash_changes_on_attach_flip()
        {
            var mech1 = new List<MechanicalEdge> {
                new MechanicalEdge { BaseGridId = 1, TopGridId = 2, Attached = true }
            };
            var mech2 = new List<MechanicalEdge> {
                new MechanicalEdge { BaseGridId = 1, TopGridId = 2, Attached = false }
            };
            ScopeBuilder.ComputeScopeDriftHash(mech1, null)
                .Should().NotBe(ScopeBuilder.ComputeScopeDriftHash(mech2, null));
        }

        [Fact]
        public void Group_mode_admits_only_members()
        {
            var groupIds = new HashSet<long> { 100, 200 };
            var scopeGrids = new HashSet<long> { 1, 2 };
            ScopeBuilder.IsBlockInScope(true, groupIds, scopeGrids, 100, 1).Should().BeTrue();
            ScopeBuilder.IsBlockInScope(true, groupIds, scopeGrids, 300, 1).Should().BeFalse();
        }

        [Fact]
        public void Group_mode_excludes_non_member_on_in_scope_grid()
        {
            var groupIds = new HashSet<long> { 100 };
            var scopeGrids = new HashSet<long> { 1 };
            ScopeBuilder.IsBlockInScope(true, groupIds, scopeGrids, 300, 1).Should().BeFalse();
        }

        [Fact]
        public void Grid_mode_uses_scope_grids()
        {
            var scopeGrids = new HashSet<long> { 1, 2 };
            ScopeBuilder.IsBlockInScope(false, null, scopeGrids, 100, 1).Should().BeTrue();
            ScopeBuilder.IsBlockInScope(false, null, scopeGrids, 100, 3).Should().BeFalse();
        }

        [Fact]
        public void Group_mode_with_null_members_admits_nothing()
        {
            var scopeGrids = new HashSet<long> { 1 };
            ScopeBuilder.IsBlockInScope(true, null, scopeGrids, 100, 1).Should().BeFalse();
        }

        [Fact]
        public void Grid_mode_with_null_scope_admits_nothing()
        {
            ScopeBuilder.IsBlockInScope(false, null, null, 100, 1).Should().BeFalse();
        }
    }
}
