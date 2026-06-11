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
            var output = new LongSet();
            ScopeBuilder.BuildScope(1, new MechanicalEdgeList(), new ConnectorEdgeList(), true, output);
            output.Should().BeEquivalentTo(new long[] { 1 });
        }

        [Fact]
        public void Mechanical_edges_extend_scope_when_attached()
        {
            var output = new LongSet();
            var mech = new MechanicalEdgeList {
                new MechanicalEdge { BaseGridId = 1, TopGridId = 2, Attached = true },
                new MechanicalEdge { BaseGridId = 2, TopGridId = 3, Attached = true }
            };
            ScopeBuilder.BuildScope(1, mech, new ConnectorEdgeList(), true, output);
            output.Should().BeEquivalentTo(new long[] { 1, 2, 3 });
        }

        [Fact]
        public void Detached_edges_do_not_extend()
        {
            var output = new LongSet();
            var mech = new MechanicalEdgeList {
                new MechanicalEdge { BaseGridId = 1, TopGridId = 2, Attached = false }
            };
            ScopeBuilder.BuildScope(1, mech, new ConnectorEdgeList(), true, output);
            output.Should().BeEquivalentTo(new long[] { 1 });
        }

        [Fact]
        public void NoSubgrid_tag_blocks_traversal()
        {
            var output = new LongSet();
            var mech = new MechanicalEdgeList {
                new MechanicalEdge { BaseGridId = 1, TopGridId = 2, Attached = true, NoSubgridTag = true }
            };
            ScopeBuilder.BuildScope(1, mech, new ConnectorEdgeList(), true, output);
            output.Should().BeEquivalentTo(new long[] { 1 });
        }

        [Fact]
        public void Federate_connector_crosses_when_connected_and_tagged()
        {
            var output = new LongSet();
            var conn = new ConnectorEdgeList {
                new ConnectorEdge { OwnerGridId = 1, OtherGridId = 9, Connected = true, FederateTag = true }
            };
            ScopeBuilder.BuildScope(1, new MechanicalEdgeList(), conn, true, output);
            output.Should().BeEquivalentTo(new long[] { 1, 9 });
        }

        [Fact]
        public void Federation_disabled_ignores_connectors()
        {
            var output = new LongSet();
            var conn = new ConnectorEdgeList {
                new ConnectorEdge { OwnerGridId = 1, OtherGridId = 9, Connected = true, FederateTag = true }
            };
            ScopeBuilder.BuildScope(1, new MechanicalEdgeList(), conn, false, output);
            output.Should().BeEquivalentTo(new long[] { 1 });
        }

        [Fact]
        public void Untagged_or_disconnected_connector_does_not_cross()
        {
            var output = new LongSet();
            var conn = new ConnectorEdgeList {
                new ConnectorEdge { OwnerGridId = 1, OtherGridId = 9, Connected = false, FederateTag = true },
                new ConnectorEdge { OwnerGridId = 1, OtherGridId = 8, Connected = true, FederateTag = false }
            };
            ScopeBuilder.BuildScope(1, new MechanicalEdgeList(), conn, true, output);
            output.Should().BeEquivalentTo(new long[] { 1 });
        }

        [Fact]
        public void Drift_hash_stable_under_reorder()
        {
            var mech1 = new MechanicalEdgeList {
                new MechanicalEdge { BaseGridId = 1, TopGridId = 2, Attached = true },
                new MechanicalEdge { BaseGridId = 2, TopGridId = 3, Attached = true }
            };
            var mech2 = new MechanicalEdgeList {
                new MechanicalEdge { BaseGridId = 1, TopGridId = 2, Attached = true },
                new MechanicalEdge { BaseGridId = 2, TopGridId = 3, Attached = true }
            };
            ScopeBuilder.ComputeScopeDriftHash(mech1, null)
                .Should().Be(ScopeBuilder.ComputeScopeDriftHash(mech2, null));
        }

        [Fact]
        public void Drift_hash_changes_on_attach_flip()
        {
            var mech1 = new MechanicalEdgeList {
                new MechanicalEdge { BaseGridId = 1, TopGridId = 2, Attached = true }
            };
            var mech2 = new MechanicalEdgeList {
                new MechanicalEdge { BaseGridId = 1, TopGridId = 2, Attached = false }
            };
            ScopeBuilder.ComputeScopeDriftHash(mech1, null)
                .Should().NotBe(ScopeBuilder.ComputeScopeDriftHash(mech2, null));
        }

        [Fact]
        public void Approved_overload_root_only_when_no_approvals()
        {
            var output = new LongSet();
            ScopeBuilder.BuildScope(1, new MechanicalEdgeList(), new LongSet(), output);
            output.Should().BeEquivalentTo(new long[] { 1 });
        }

        [Fact]
        public void Approved_overload_federates_listed_grids()
        {
            var output = new LongSet();
            ScopeBuilder.BuildScope(1, new MechanicalEdgeList(), new LongSet { 9 }, output);
            output.Should().BeEquivalentTo(new long[] { 1, 9 });
        }

        [Fact]
        public void Approved_overload_includes_subgrids_of_approved_grid()
        {
            var output = new LongSet();
            var mech = new MechanicalEdgeList {
                new MechanicalEdge { BaseGridId = 9, TopGridId = 11, Attached = true }
            };
            ScopeBuilder.BuildScope(1, mech, new LongSet { 9 }, output);
            output.Should().BeEquivalentTo(new long[] { 1, 9, 11 });
        }

        [Fact]
        public void Approved_overload_null_set_yields_root_and_subgrids()
        {
            var output = new LongSet();
            var mech = new MechanicalEdgeList {
                new MechanicalEdge { BaseGridId = 1, TopGridId = 2, Attached = true }
            };
            ScopeBuilder.BuildScope(1, mech, (LongSet)null, output);
            output.Should().BeEquivalentTo(new long[] { 1, 2 });
        }

        [Fact]
        public void Drift_hash_changes_on_priority_change()
        {
            var c1 = new ConnectorEdgeList {
                new ConnectorEdge { OwnerGridId = 1, OtherGridId = 9, Connected = true, FederateTag = true, LocalPriority = 0 }
            };
            var c2 = new ConnectorEdgeList {
                new ConnectorEdge { OwnerGridId = 1, OtherGridId = 9, Connected = true, FederateTag = true, LocalPriority = 2 }
            };
            ScopeBuilder.ComputeScopeDriftHash(null, c1)
                .Should().NotBe(ScopeBuilder.ComputeScopeDriftHash(null, c2));
        }

        [Fact]
        public void Drift_hash_changes_on_other_federate_tag()
        {
            var c1 = new ConnectorEdgeList {
                new ConnectorEdge { OwnerGridId = 1, OtherGridId = 9, Connected = true, FederateTag = true, OtherFederateTag = false }
            };
            var c2 = new ConnectorEdgeList {
                new ConnectorEdge { OwnerGridId = 1, OtherGridId = 9, Connected = true, FederateTag = true, OtherFederateTag = true }
            };
            ScopeBuilder.ComputeScopeDriftHash(null, c1)
                .Should().NotBe(ScopeBuilder.ComputeScopeDriftHash(null, c2));
        }

        [Fact]
        public void Construct_signature_independent_of_order()
        {
            long[] a = new long[] { 1, 2, 3 };
            long[] b = new long[] { 3, 1, 2 };
            ScopeBuilder.ComputeConstructSignature(a)
                .Should().Be(ScopeBuilder.ComputeConstructSignature(b));
        }

        [Fact]
        public void Construct_signature_differs_for_different_sets()
        {
            long[] a = new long[] { 1, 2, 3 };
            long[] b = new long[] { 1, 2, 4 };
            ScopeBuilder.ComputeConstructSignature(a)
                .Should().NotBe(ScopeBuilder.ComputeConstructSignature(b));
        }

        [Fact]
        public void Construct_signature_differs_when_member_added()
        {
            long[] a = new long[] { 1 };
            long[] b = new long[] { 1, 2 };
            ScopeBuilder.ComputeConstructSignature(a)
                .Should().NotBe(ScopeBuilder.ComputeConstructSignature(b));
        }

        [Fact]
        public void Group_mode_admits_only_members()
        {
            var groupIds = new LongSet { 100, 200 };
            var scopeGrids = new LongSet { 1, 2 };
            ScopeBuilder.IsBlockInScope(true, groupIds, scopeGrids, 100, 1).Should().BeTrue();
            ScopeBuilder.IsBlockInScope(true, groupIds, scopeGrids, 300, 1).Should().BeFalse();
        }

        [Fact]
        public void Group_mode_excludes_non_member_on_in_scope_grid()
        {
            var groupIds = new LongSet { 100 };
            var scopeGrids = new LongSet { 1 };
            ScopeBuilder.IsBlockInScope(true, groupIds, scopeGrids, 300, 1).Should().BeFalse();
        }

        [Fact]
        public void Grid_mode_uses_scope_grids()
        {
            var scopeGrids = new LongSet { 1, 2 };
            ScopeBuilder.IsBlockInScope(false, null, scopeGrids, 100, 1).Should().BeTrue();
            ScopeBuilder.IsBlockInScope(false, null, scopeGrids, 100, 3).Should().BeFalse();
        }

        [Fact]
        public void Group_mode_with_null_members_admits_nothing()
        {
            var scopeGrids = new LongSet { 1 };
            ScopeBuilder.IsBlockInScope(true, null, scopeGrids, 100, 1).Should().BeFalse();
        }

        [Fact]
        public void Grid_mode_with_null_scope_admits_nothing()
        {
            ScopeBuilder.IsBlockInScope(false, null, null, 100, 1).Should().BeFalse();
        }

        [Fact]
        public void IsManagedTarget_no_group_admits_any()
        {
            ScopeBuilder.IsManagedTarget(false, null, 100).Should().BeTrue();
        }

        [Fact]
        public void IsManagedTarget_group_admits_member()
        {
            ScopeBuilder.IsManagedTarget(true, new LongSet { 100 }, 100).Should().BeTrue();
        }

        [Fact]
        public void IsManagedTarget_group_excludes_non_member()
        {
            ScopeBuilder.IsManagedTarget(true, new LongSet { 100 }, 300).Should().BeFalse();
        }

        [Fact]
        public void IsManagedTarget_group_with_null_members_admits_nothing()
        {
            ScopeBuilder.IsManagedTarget(true, null, 100).Should().BeFalse();
        }
    }
}
