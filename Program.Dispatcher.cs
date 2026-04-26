using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript {
    public partial class Program : MyGridProgram {
        /// <summary>Why a step yielded back to the dispatcher.</summary>
        public enum YieldReason {
            /// <summary>Per-tick instruction budget was hit; resume next tick.</summary>
            BudgetHit,
            /// <summary>Logical chunk completed; resume next tick.</summary>
            ChunkBoundary,
            /// <summary>Step is waiting on external state (reserved for future use).</summary>
            ExternalWait
        }

        /// <summary>Fallback budget fraction used before configuration is parsed.</summary>
        const float DefaultBudgetFraction = 0.5f;

        /// <summary>Index of the currently executing top-level step.</summary>
        int _stepIndex;

        /// <summary>Reserved sub-step counter for finer-grained progress reporting.</summary>
        int _subStep;

        /// <summary>Human-readable label of the currently executing step.</summary>
        string _stepLabel = "init";

        /// <summary>Step labels indexed by step number; mirrored on the Echo display.</summary>
        static readonly string[] StepLabels = {
            "RescanIfDue", "ParseConfigIfDirty", "CategorizeContainers",
            "ScanInventories", "FulfillStockQuotas", "SortGenericCargo"
        };

        /// <summary>The active root iterator that <see cref="RunOneTick"/> pumps.</summary>
        IEnumerator<YieldReason> _workIterator;

        /// <summary>Returns true when consumed instructions exceed the configured per-tick budget.</summary>
        bool BudgetExceeded() {
            return Runtime.CurrentInstructionCount >
                   Runtime.MaxInstructionCount * _config.BudgetFraction;
        }

        /// <summary>Top-level work loop iterating over each pipeline step indefinitely.</summary>
        IEnumerator<YieldReason> StepRoot() {
            while (true) {
                ResetOneShotWarnings();
                for (int i = 0; i < 6; i++) {
                    _stepIndex = i;
                    _subStep = 0;
                    _stepLabel = StepLabels[i];
                    IEnumerator<YieldReason> step = StepFor(i);
                    // Explicit MoveNext pump (NOT foreach) — keeps the outer
                    // iterator state machine flat so exceptions in child
                    // iterators propagate cleanly to RunOneTick's catch.
                    while (step.MoveNext()) {
                        yield return step.Current;
                    }
                    yield return YieldReason.ChunkBoundary;
                }
            }
        }

        /// <summary>Returns the iterator for the step at index <paramref name="i"/>.</summary>
        IEnumerator<YieldReason> StepFor(int i) {
            switch (i) {
                case 0: return StepRescanIfDue();
                case 1: return StepParseConfigIfDirty();
                case 2: return StepCategorizeContainers();
                case 3: return StepScanInventories();
                case 4: return StepFulfillStockQuotas();
                default: return StepSortGenericCargo();
            }
        }

        /// <summary>Pumps one entry of the work iterator, restarting the pipeline on completion or fault.</summary>
        void RunOneTick() {
            try {
                if (_workIterator == null || !_workIterator.MoveNext()) {
                    _workIterator = StepRoot();
                }
            } catch (Exception ex) {
                LogError("step " + _stepIndex + "." + _subStep + " " + _stepLabel, ex);
                _workIterator = StepRoot();
            }
        }

        /// <summary>Resets the work iterator so the pipeline restarts from step 0 on the next tick.</summary>
        void RestartWork() {
            _workIterator = StepRoot();
        }
    }
}
