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
        public enum YieldReason { BudgetHit, ChunkBoundary, ExternalWait }

        const float DefaultBudgetFraction = 0.5f;
        int _stepIndex;
        int _subStep;
        string _stepLabel = "init";
        static readonly string[] StepLabels = {
            "RescanIfDue", "ParseConfigIfDirty", "CategorizeContainers",
            "ScanInventories", "FulfillStockQuotas", "SortGenericCargo"
        };
        IEnumerator<YieldReason> _workIterator;

        bool BudgetExceeded() {
            return Runtime.CurrentInstructionCount >
                   Runtime.MaxInstructionCount * _config.BudgetFraction;
        }

        IEnumerator<YieldReason> StepRoot() {
            while (true) {
                for (int i = 0; i < 6; i++) {
                    _stepIndex = i;
                    _subStep = 0;
                    _stepLabel = StepLabels[i];
                    IEnumerator<YieldReason> step = StepFor(i);
                    // Explicit MoveNext pump (NOT foreach) — keeps the
                    // outer iterator state machine flat so exceptions in
                    // child iterators propagate cleanly to RunOneTick's catch.
                    while (step.MoveNext()) {
                        yield return step.Current;
                    }
                    yield return YieldReason.ChunkBoundary;
                }
            }
        }

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

        void RestartWork() {
            _workIterator = StepRoot();
        }
    }
}
