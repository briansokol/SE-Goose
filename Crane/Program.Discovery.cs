using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript {
    public partial class Program : MyGridProgram {
        /// <summary>EntityIds of grids currently in management scope.</summary>
        readonly HashSet<long> _scopeGrids = new HashSet<long>();

        /// <summary>All blocks discovered in <see cref="CraneConfig.GroupName"/> that are eligible for Crane management.</summary>
        readonly List<IMyTerminalBlock> _allManagedBlocks = new List<IMyTerminalBlock>();

        /// <summary>Assemblers in the managed group (filtered to non-survival-kit assemblers via interface).</summary>
        readonly List<IMyAssembler> _assemblers = new List<IMyAssembler>();

        /// <summary>LCDs tagged <c>[CCraft]</c> — host quota config (CustomData) and render the status surface.</summary>
        readonly List<IMyTextSurface> _ccraftLcds = new List<IMyTextSurface>();

        /// <summary>LCDs tagged <c>[CError]</c> — render the warning log surface.</summary>
        readonly List<IMyTextSurface> _cerrorLcds = new List<IMyTextSurface>();

        /// <summary>Reusable mechanical-edge buffer fed to <see cref="ScopeBuilder.BuildScope"/>.</summary>
        readonly List<MechanicalEdge> _scopeMechBuf = new List<MechanicalEdge>();

        /// <summary>Reusable raw mechanical block buffer.</summary>
        readonly List<IMyMechanicalConnectionBlock> _scopeMechRaw = new List<IMyMechanicalConnectionBlock>();

        /// <summary>Snapshot of mechanical edges from the most recent scope build.</summary>
        readonly List<MechanicalEdge> _scopeMechCache = new List<MechanicalEdge>();

        /// <summary>Empty connector list — Crane does not federate via connectors in v1.</summary>
        readonly List<ConnectorEdge> _scopeConnBuf = new List<ConnectorEdge>();

        /// <summary>Rolling hash of the scope inputs.</summary>
        ulong _scopeDriftHash;

        /// <summary>Ticks elapsed since the last rescan.</summary>
        int _ticksSinceRescan = int.MaxValue;

        /// <summary>Set by the <c>rescan</c> command to trigger an immediate rescan on the next cycle.</summary>
        bool _rescanRequested = false;

        /// <summary>Reusable scratch list for resolving block-group members.</summary>
        readonly List<IMyTerminalBlock> _groupMemberBuffer = new List<IMyTerminalBlock>();

        /// <summary>Rebuilds <see cref="_scopeGrids"/> when due. Crane does not federate via connectors (v1).</summary>
        IEnumerator<YieldReason> StepRebuildScopeIfDue() {
            bool needs = _rescanRequested
                      || _scopeGrids.Count == 0
                      || _ticksSinceRescan >= _config.RescanIntervalTicks;

            if (!needs && _scopeGrids.Count > 0) {
                ulong currentHash = ComputeLiveScopeDriftHash();
                if (currentHash != _scopeDriftHash) {
                    _logger.LogAction("Scope drift detected");
                    _rescanRequested = true;
                    needs = true;
                }
            }

            if (!needs) {
                yield return YieldReason.ChunkBoundary;
                yield break;
            }
            RebuildScope();
            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>Walks live mechanical-connection blocks, projects them into POCOs, then runs <see cref="ScopeBuilder.BuildScope"/>.</summary>
        void RebuildScope() {
            _scopeMechRaw.Clear();
            GridTerminalSystem.GetBlocksOfType(_scopeMechRaw, m => !m.Closed);
            _scopeMechBuf.Clear();
            for (int i = 0; i < _scopeMechRaw.Count; i++) {
                IMyMechanicalConnectionBlock m = _scopeMechRaw[i];
                MechanicalEdge edge;
                edge.BaseGridId = m.CubeGrid != null ? m.CubeGrid.EntityId : 0;
                edge.TopGridId = (m.IsAttached && m.TopGrid != null) ? m.TopGrid.EntityId : 0;
                edge.Attached = m.IsAttached;
                edge.NoSubgridTag = BlockNameTags.NameHasTag(m.CustomName, BlockNameTags.NoSubgridTag);
                _scopeMechBuf.Add(edge);
            }

            _scopeConnBuf.Clear();
            ScopeBuilder.BuildScope(Me.CubeGrid.EntityId, _scopeMechBuf, _scopeConnBuf, false, _scopeGrids);

            _scopeMechCache.Clear();
            _scopeMechCache.AddRange(_scopeMechBuf);
            _scopeDriftHash = ScopeBuilder.ComputeScopeDriftHash(_scopeMechCache, null);
            _logger.LogActionOnce("scope:size:" + _scopeGrids.Count, "Scope: " + _scopeGrids.Count + " grid(s)");
        }

        /// <summary>Computes the scope drift hash from the live raw block list.</summary>
        ulong ComputeLiveScopeDriftHash() {
            _scopeMechRaw.Clear();
            GridTerminalSystem.GetBlocksOfType(_scopeMechRaw, m => !m.Closed);
            _scopeMechBuf.Clear();
            for (int i = 0; i < _scopeMechRaw.Count; i++) {
                IMyMechanicalConnectionBlock m = _scopeMechRaw[i];
                MechanicalEdge edge;
                edge.BaseGridId = m.CubeGrid != null ? m.CubeGrid.EntityId : 0;
                edge.TopGridId = (m.IsAttached && m.TopGrid != null) ? m.TopGrid.EntityId : 0;
                edge.Attached = m.IsAttached;
                edge.NoSubgridTag = BlockNameTags.NameHasTag(m.CustomName, BlockNameTags.NoSubgridTag);
                _scopeMechBuf.Add(edge);
            }
            return ScopeBuilder.ComputeScopeDriftHash(_scopeMechBuf, null);
        }

        /// <summary>Rescans the configured block group, classifies blocks (assembler / <c>[CCraft]</c> LCD / <c>[CError]</c> LCD).</summary>
        IEnumerator<YieldReason> StepRescanIfDue() {
            if (!_rescanRequested && _ticksSinceRescan < _config.RescanIntervalTicks) {
                _ticksSinceRescan++;
                yield return YieldReason.ChunkBoundary;
                yield break;
            }
            _rescanRequested = false;
            _ticksSinceRescan = 0;
            _configDirty = true;

            _allManagedBlocks.Clear();
            _assemblers.Clear();
            _ccraftLcds.Clear();
            _cerrorLcds.Clear();

            IMyBlockGroup group = GridTerminalSystem.GetBlockGroupWithName(_config.GroupName);
            if (group == null) {
                _logger.LogWarningOnce("group:missing:" + _config.GroupName,
                    "[Crane] Block group '" + _config.GroupName + "' not found. Create the group and add assemblers + LCDs to it.");
                yield return YieldReason.ChunkBoundary;
                yield break;
            }

            _groupMemberBuffer.Clear();
            group.GetBlocks(_groupMemberBuffer);
            for (int i = 0; i < _groupMemberBuffer.Count; i++) {
                IMyTerminalBlock block = _groupMemberBuffer[i];
                if (block == null || block.Closed) continue;
                if (block.CubeGrid == null) continue;
                if (!_scopeGrids.Contains(block.CubeGrid.EntityId)) continue;
                if (BlockNameTags.HasIgnoreTag(block.CustomName)) continue;
                if (block == Me) continue;

                IMyAssembler asm = block as IMyAssembler;
                if (asm != null) {
                    _assemblers.Add(asm);
                    _allManagedBlocks.Add(block);
                    continue;
                }

                IMyTextSurfaceProvider surfProvider = block as IMyTextSurfaceProvider;
                if (surfProvider != null) {
                    if (BlockNameTags.NameHasTag(block.CustomName, "[CCraft]") && surfProvider.SurfaceCount > 0) {
                        _ccraftLcds.Add(surfProvider.GetSurface(0));
                        _allManagedBlocks.Add(block);
                    } else if (BlockNameTags.NameHasTag(block.CustomName, "[CError]") && surfProvider.SurfaceCount > 0) {
                        _cerrorLcds.Add(surfProvider.GetSurface(0));
                        _allManagedBlocks.Add(block);
                    }
                }
            }

            _logger.LogAction("Rescan: " + _assemblers.Count + " asm, "
                + _ccraftLcds.Count + " [CCraft], " + _cerrorLcds.Count + " [CError]");

            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>The first <c>[CCraft]</c>-tagged terminal block found this scan (host of the quota INI sections).</summary>
        IMyTerminalBlock _ccraftConfigHost;

        /// <summary>Locates the <c>[CCraft]</c>-tagged terminal block currently hosting the quota config CustomData. Returns null when no <c>[CCraft]</c> LCD is present.</summary>
        IMyTerminalBlock FindCCraftConfigHost() {
            if (_ccraftLcds.Count == 0) return null;
            for (int i = 0; i < _groupMemberBuffer.Count; i++) {
                IMyTerminalBlock block = _groupMemberBuffer[i];
                if (block == null || block.Closed) continue;
                if (BlockNameTags.NameHasTag(block.CustomName, "[CCraft]")) return block;
            }
            return null;
        }
    }
}
