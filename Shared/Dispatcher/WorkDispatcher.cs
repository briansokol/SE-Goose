using System;
using System.Collections.Generic;

namespace IngameScript {
    /// <summary>Composable pipeline dispatcher. Caller registers step labels + factories; the dispatcher pumps each step iterator and yields on budget hits. Exception handling routes faults to a caller-supplied logger.</summary>
    public class WorkDispatcher {
        readonly Func<int, IEnumerator<YieldReason>> _stepFactory;
        readonly Func<bool> _budgetExceeded;
        readonly string[] _stepLabels;
        readonly Action _onCycleStart;
        readonly Action<string, Exception> _onError;

        IEnumerator<YieldReason> _workIterator;

        /// <summary>Index of the currently executing step.</summary>
        public int StepIndex { get; private set; }

        /// <summary>Sub-step counter (reserved for finer-grained progress reporting).</summary>
        public int SubStep { get; private set; }

        /// <summary>Human-readable label of the currently executing step.</summary>
        public string StepLabel { get; private set; }

        /// <summary>Creates a dispatcher.</summary>
        /// <param name="stepLabels">Labels indexed by step number; length defines the step count.</param>
        /// <param name="stepFactory">Returns the iterator for the step at the given index.</param>
        /// <param name="budgetExceeded">Returns true when the per-tick instruction budget has been exhausted.</param>
        /// <param name="onCycleStart">Invoked once at the top of each pipeline cycle (typically to reset one-shot warning state).</param>
        /// <param name="onError">Invoked when a step iterator throws.</param>
        public WorkDispatcher(string[] stepLabels, Func<int, IEnumerator<YieldReason>> stepFactory, Func<bool> budgetExceeded, Action onCycleStart, Action<string, Exception> onError) {
            _stepLabels = stepLabels ?? new string[0];
            _stepFactory = stepFactory;
            _budgetExceeded = budgetExceeded;
            _onCycleStart = onCycleStart;
            _onError = onError;
            StepLabel = _stepLabels.Length > 0 ? _stepLabels[0] : "init";
        }

        /// <summary>Pumps the work iterator repeatedly within a single tick until the budget trips or the iterator finishes, restarting the pipeline on completion or fault.</summary>
        public void RunOneTick() {
            try {
                do {
                    if (_workIterator == null || !_workIterator.MoveNext()) {
                        _workIterator = StepRoot();
                        break;
                    }
                } while (!(_budgetExceeded != null && _budgetExceeded()));
            } catch (Exception ex) {
                if (_onError != null) {
                    _onError("step " + StepIndex + "." + SubStep + " " + StepLabel, ex);
                }
                _workIterator = StepRoot();
            }
        }

        /// <summary>Resets the work iterator so the pipeline restarts on the next tick.</summary>
        public void Restart() {
            _workIterator = StepRoot();
        }

        IEnumerator<YieldReason> StepRoot() {
            while (true) {
                if (_onCycleStart != null) _onCycleStart();
                for (int i = 0; i < _stepLabels.Length; i++) {
                    StepIndex = i;
                    SubStep = 0;
                    StepLabel = _stepLabels[i];
                    IEnumerator<YieldReason> step = _stepFactory != null ? _stepFactory(i) : NoOpStep();
                    if (step == null) step = NoOpStep();
                    while (step.MoveNext()) {
                        yield return step.Current;
                    }
                    yield return YieldReason.ChunkBoundary;
                }
            }
        }

        static IEnumerator<YieldReason> NoOpStep() {
            yield return YieldReason.ChunkBoundary;
        }
    }
}
