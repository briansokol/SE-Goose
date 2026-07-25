using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        /// <summary>Index of the currently executing top-level step.</summary>
        private int _stepIndex;

        /// <summary>Reserved sub-step counter for finer-grained progress reporting.</summary>
        private int _subStep;

        /// <summary>Human-readable label of the currently executing step.</summary>
        private string _stepLabel = "init";

        /// <summary>Step labels indexed by step number; mirrored on the Echo display.</summary>
        private static readonly string[] StepLabels = {
            "RebuildScope", "RescanIfDue", "ParseConfigIfDirty", "CategorizeContainers",
            "CategorizeConsumers", "ScanInventories", "FulfillStockQuotas",
            "SortGenericCargo", "BalanceConsumers", "BalanceSameRoleContainers",
            "ManageRefineries", "RenderStatus"
        };

        /// <summary>The active root iterator that <see cref="RunOneTick"/> pumps.</summary>
        private IEnumerator<YieldReason> _workIterator;

        /// <summary>Lowest effective budget fraction the adaptive controller may select.</summary>
        private const float MinBudgetFraction = 0.1f;

        /// <summary>Budget fraction actually in force, adapted each tick from the previous run's duration.</summary>
        private float _effectiveBudgetFraction = 0.8f;

        /// <summary>
        /// Picks the next budget fraction from the previous run's wall-clock duration. Instruction
        /// count is a poor proxy for run time because the costly grid API calls barely advance it,
        /// so the fraction is trimmed whenever a run overruns and recovered once runs are cheap again.
        /// </summary>
        /// <param name="lastRunTimeMs">Duration of the previous run, in milliseconds.</param>
        /// <param name="targetRunTimeMs">Desired ceiling; values &lt;= 0 disable adaptation.</param>
        /// <param name="current">Fraction currently in force.</param>
        /// <param name="ceiling">Upper bound, taken from the configured budget fraction.</param>
        public static float AdjustBudgetFraction(double lastRunTimeMs, double targetRunTimeMs, float current, float ceiling)
        {
            if (targetRunTimeMs <= 0.0)
            {
                return ceiling;
            }

            float next = current;
            if (lastRunTimeMs > targetRunTimeMs)
            {
                next = current * 0.75f;
            }
            else if (lastRunTimeMs < targetRunTimeMs * 0.5)
            {
                next = current * 1.1f;
            }

            if (next > ceiling)
            {
                next = ceiling;
            }
            if (next < MinBudgetFraction)
            {
                next = MinBudgetFraction;
            }
            return next;
        }

        /// <summary>Returns true when consumed instructions exceed the adapted per-tick budget.</summary>
        private bool BudgetExceeded()
        {
            return Runtime.CurrentInstructionCount >
                   Runtime.MaxInstructionCount * _effectiveBudgetFraction;
        }

        /// <summary>Top-level work loop iterating over each pipeline step indefinitely.</summary>
        private IEnumerator<YieldReason> StepRoot()
        {
            while (true)
            {
                ResetOneShotWarnings();
                for (int i = 0; i < StepLabels.Length; i++)
                {
                    _stepIndex = i;
                    _subStep = 0;
                    _stepLabel = StepLabels[i];
                    IEnumerator<YieldReason> step = StepFor(i);
                    // Explicit MoveNext pump (NOT foreach) — keeps the outer
                    // iterator state machine flat so exceptions in child
                    // iterators propagate cleanly to RunOneTick's catch.
                    while (step.MoveNext())
                    {
                        yield return step.Current;
                    }
                    yield return YieldReason.ChunkBoundary;
                }
            }
        }

        /// <summary>Returns the iterator for the step at index <paramref name="i"/>. While management is suspended (duplicate or deferral), the four inventory-mutating steps (6-9) are skipped so the script moves nothing; discovery, scan, and status rendering still run so displays stay live.</summary>
        private IEnumerator<YieldReason> StepFor(int i)
        {
            if (ManagementSuspended && i >= 6 && i <= 10)
            {
                return NoOpStep();
            }

            switch (i)
            {
                case 0:
                    return StepRebuildScopeIfDue();
                case 1:
                    return StepRescanIfDue();
                case 2:
                    return StepParseConfigIfDirty();
                case 3:
                    return StepCategorizeContainers();
                case 4:
                    return StepCategorizeConsumers();
                case 5:
                    return StepScanInventories();
                case 6:
                    return StepFulfillStockQuotas();
                case 7:
                    return StepSortGenericCargo();
                case 8:
                    return StepBalanceConsumers();
                case 9:
                    return StepBalanceSameRoleContainers();
                case 10:
                    return StepManageRefineries();
                case 11:
                    return StepRenderStatus();
                default:
                    return NoOpStep();
            }
        }

        /// <summary>Defensive no-op iterator returned by <see cref="StepFor"/> when the step index drifts out of range. Yields once and exits so the dispatcher's pump survives an off-by-one bug without throwing.</summary>
        private IEnumerator<YieldReason> NoOpStep()
        {
            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>Pumps the work iterator repeatedly within a single tick until <see cref="BudgetExceeded"/> trips or the iterator finishes, restarting the pipeline on completion or fault.</summary>
        private void RunOneTick()
        {
            try
            {
                do
                {
                    if (_workIterator == null || !_workIterator.MoveNext())
                    {
                        _workIterator = StepRoot();
                        break;
                    }
                } while (!BudgetExceeded());
            }
            catch (Exception ex)
            {
                LogError("step " + _stepIndex + "." + _subStep + " " + _stepLabel, ex);
                _workIterator = StepRoot();
            }
        }

        /// <summary>Resets the work iterator so the pipeline restarts from step 0 on the next tick.</summary>
        private void RestartWork()
        {
            _workIterator = StepRoot();
        }
    }
}
