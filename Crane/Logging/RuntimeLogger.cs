using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    /// <summary>Self-contained logging helper for Goose-style scripts. Maintains a rolling action log, a deduplicated warning list, and one-shot warning suppression across cycles. Renders a status block to the PB Echo surface.</summary>
    public class RuntimeLogger
    {
        private readonly Action<string> _echo;
        private readonly string _scriptLabel;

        /// <summary>Maximum number of recent actions retained for the Echo display.</summary>
        public int MaxActionLogEntries = 48;

        /// <summary>Maximum number of distinct warnings retained before eviction.</summary>
        public int MaxWarningEntries = 32;

        private readonly Queue<string> _actionLog = new Queue<string>();
        private string _lastRescanSummary = "";
        private readonly List<string> _warningOrder = new List<string>();
        private readonly Dictionary<string, int> _warnings = new Dictionary<string, int>();
        private readonly HashSet<string> _oneShotWarnedKeys = new HashSet<string>();
        private readonly List<string> _oneShotWarnedMessages = new List<string>();
        private readonly HashSet<string> _actionOnceKeys = new HashSet<string>();
        private readonly StringBuilder _echoBuffer = new StringBuilder();

        /// <summary>Creates a logger that emits status via <paramref name="echo"/>.</summary>
        /// <param name="echo">Sink for <see cref="RenderEchoStatus"/> output (typically <see cref="MyGridProgram.Echo"/>).</param>
        /// <param name="scriptLabel">Short script identifier prefixed to the status block (e.g. <c>"Goose v1"</c>).</param>
        public RuntimeLogger(Action<string> echo, string scriptLabel)
        {
            _echo = echo;
            _scriptLabel = scriptLabel ?? string.Empty;
        }

        /// <summary>Appends an action message to the rolling action log.</summary>
        public void LogAction(string msg)
        {
            _actionLog.Enqueue(msg);
            while (_actionLog.Count > MaxActionLogEntries)
            {
                _actionLog.Dequeue();
            }
        }

        /// <summary>Records the most recent rescan summary, shown as a single in-place status line instead of accumulating in the action log.</summary>
        /// <param name="msg">Rescan summary text to display.</param>
        public void SetRescanSummary(string msg)
        {
            _lastRescanSummary = msg ?? "";
        }

        /// <summary>Records a warning, deduplicating repeats and evicting the oldest when the cap is hit.</summary>
        public void LogWarning(string msg)
        {
            int count;
            if (_warnings.TryGetValue(msg, out count))
            {
                _warnings[msg] = count + 1;
            }
            else
            {
                if (_warnings.Count >= MaxWarningEntries)
                {
                    string oldest = _warningOrder[0];
                    _warningOrder.RemoveAt(0);
                    _warnings.Remove(oldest);
                }
                _warnings[msg] = 1;
                _warningOrder.Add(msg);
            }
        }

        /// <summary>Logs <paramref name="msg"/> the first time <paramref name="key"/> is seen this cycle. Cleared by <see cref="ResetOneShotWarnings"/>.</summary>
        public void LogWarningOnce(string key, string msg)
        {
            if (_oneShotWarnedKeys.Add(key))
            {
                _oneShotWarnedMessages.Add(msg);
                LogWarning(msg);
            }
        }

        /// <summary>Logs <paramref name="msg"/> the first time <paramref name="key"/> is seen across the lifetime of the script run.</summary>
        public void LogActionOnce(string key, string msg)
        {
            if (_actionOnceKeys.Add(key))
            {
                LogAction(msg);
            }
        }

        /// <summary>Clears once-per-cycle warning state and removes their messages from the warning list so resolved conditions stop appearing.</summary>
        public void ResetOneShotWarnings()
        {
            for (int i = 0; i < _oneShotWarnedMessages.Count; i++)
            {
                string msg = _oneShotWarnedMessages[i];
                if (_warnings.Remove(msg))
                {
                    _warningOrder.Remove(msg);
                }
            }
            _oneShotWarnedMessages.Clear();
            _oneShotWarnedKeys.Clear();
        }

        /// <summary>Records a caught exception as a warning tagged with the originating context.</summary>
        public void LogError(string context, Exception ex)
        {
            LogWarning("[" + context + "] " + ex.GetType().Name + ": " + ex.Message);
        }

        /// <summary>Renders pipeline state, runtime stats, last actions, and active warnings to the Echo sink.</summary>
        /// <param name="paused">True when the host script is in a paused state.</param>
        /// <param name="stepIndex">Current pipeline step index.</param>
        /// <param name="subStep">Current sub-step counter.</param>
        /// <param name="stepLabel">Human-readable label of the current step.</param>
        /// <param name="currentInstr">Live <c>Runtime.CurrentInstructionCount</c>.</param>
        /// <param name="maxInstr">Live <c>Runtime.MaxInstructionCount</c>.</param>
        /// <param name="lastRunMs">Live <c>Runtime.LastRunTimeMs</c>.</param>
        public void RenderEchoStatus(bool paused, int stepIndex, int subStep, string stepLabel, int currentInstr, int maxInstr, double lastRunMs)
        {
            if (_echo == null)
            {
                return;
            }

            _echoBuffer.Clear();
            _echoBuffer.Append(_scriptLabel).Append(' ');
            _echoBuffer.Append(paused ? "[PAUSED] " : "");
            _echoBuffer.Append("step ").Append(stepIndex).Append('.').Append(subStep)
                .Append(' ').Append(stepLabel ?? "").Append('\n');
            _echoBuffer.Append("Instr: ").Append(currentInstr)
                .Append('/').Append(maxInstr).Append('\n');
            _echoBuffer.Append("LastRunMs: ").Append(lastRunMs.ToString("F2")).Append('\n');
            if (_lastRescanSummary.Length > 0)
            {
                _echoBuffer.Append(_lastRescanSummary).Append('\n');
            }
            if (_actionLog.Count > 0)
            {
                _echoBuffer.Append("Last:\n");
                foreach (string s in _actionLog)
                { _echoBuffer.Append("  ").Append(s).Append('\n'); }
            }
            if (_warnings.Count > 0)
            {
                _echoBuffer.Append("Warnings(").Append(_warnings.Count).Append("):\n");
                int n = 0;
                foreach (string key in _warningOrder)
                {
                    if (n++ >= 5)
                    {
                        break;
                    }

                    _echoBuffer.Append("  ").Append(key).Append('\n');
                }
            }
            _echo(_echoBuffer.ToString());
        }

        /// <summary>Exposes the action log for tests and host-script status surfaces.</summary>
        public IEnumerable<string> ActionLog { get { return _actionLog; } }

        /// <summary>Exposes the warnings map for tests and host-script status surfaces.</summary>
        public IReadOnlyDictionary<string, int> Warnings { get { return _warnings; } }
    }
}
