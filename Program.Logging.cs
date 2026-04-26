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
        /// <summary>Recent action messages shown on the Echo display (capped by config).</summary>
        Queue<string> _actionLog = new Queue<string>();

        /// <summary>Insertion order of <see cref="_warnings"/> keys for FIFO eviction.</summary>
        List<string> _warningOrder = new List<string>();

        /// <summary>Active warning messages mapped to occurrence count.</summary>
        Dictionary<string, int> _warnings = new Dictionary<string, int>();

        /// <summary>Keys for one-shot warnings already logged this cycle.</summary>
        HashSet<string> _oneShotWarnedKeys = new HashSet<string>();

        /// <summary>Messages logged via <see cref="LogWarningOnce"/> this cycle, used for cleanup.</summary>
        List<string> _oneShotWarnedMessages = new List<string>();

        /// <summary>Appends an action message to the rolling action log.</summary>
        void LogAction(string msg) {
            _actionLog.Enqueue(msg);
            while (_actionLog.Count > _config.MaxActionLogEntries) _actionLog.Dequeue();
        }

        /// <summary>Records a warning, deduplicating repeats and evicting the oldest when the cap is hit.</summary>
        void LogWarning(string msg) {
            int count;
            if (_warnings.TryGetValue(msg, out count)) {
                _warnings[msg] = count + 1;
            } else {
                if (_warnings.Count >= _config.MaxWarningEntries) {
                    string oldest = _warningOrder[0];
                    _warningOrder.RemoveAt(0);
                    _warnings.Remove(oldest);
                }
                _warnings[msg] = 1;
                _warningOrder.Add(msg);
            }
        }

        /// <summary>Logs <paramref name="msg"/> the first time <paramref name="key"/> is seen this cycle.</summary>
        /// <param name="key">Stable identifier for the warning condition.</param>
        /// <param name="msg">Display message recorded on first occurrence.</param>
        void LogWarningOnce(string key, string msg) {
            if (_oneShotWarnedKeys.Add(key)) {
                _oneShotWarnedMessages.Add(msg);
                LogWarning(msg);
            }
        }

        /// <summary>
        /// Clears once-per-cycle warning state and removes their messages from the warning list,
        /// so resolved conditions stop appearing once their <see cref="LogWarningOnce"/> call site no longer fires.
        /// </summary>
        void ResetOneShotWarnings() {
            for (int i = 0; i < _oneShotWarnedMessages.Count; i++) {
                string msg = _oneShotWarnedMessages[i];
                if (_warnings.Remove(msg)) _warningOrder.Remove(msg);
            }
            _oneShotWarnedMessages.Clear();
            _oneShotWarnedKeys.Clear();
        }

        /// <summary>Records a caught exception as a warning tagged with the originating context.</summary>
        void LogError(string context, Exception ex) {
            LogWarning("[" + context + "] " + ex.GetType().Name + ": " + ex.Message);
        }

        /// <summary>Reusable buffer for <see cref="RenderEchoStatus"/> to avoid per-tick allocations.</summary>
        StringBuilder _echoBuffer = new StringBuilder();

        /// <summary>Renders the current pipeline state, runtime stats, last action, and active warnings to Echo.</summary>
        void RenderEchoStatus() {
            _echoBuffer.Clear();
            _echoBuffer.Append("Goose v1 ");
            _echoBuffer.Append(_paused ? "[PAUSED] " : "");
            _echoBuffer.Append("step ").Append(_stepIndex).Append('.').Append(_subStep)
                .Append(' ').Append(_stepLabel).Append('\n');
            _echoBuffer.Append("Instr: ").Append(Runtime.CurrentInstructionCount)
                .Append('/').Append(Runtime.MaxInstructionCount).Append('\n');
            _echoBuffer.Append("LastRunMs: ").Append(Runtime.LastRunTimeMs.ToString("F2")).Append('\n');
            if (_actionLog.Count > 0) {
                _echoBuffer.Append("Last: ");
                foreach (string s in _actionLog) { _echoBuffer.Append(s); }
                _echoBuffer.Append('\n');
            }
            if (_warnings.Count > 0) {
                _echoBuffer.Append("Warnings(").Append(_warnings.Count).Append("):\n");
                int n = 0;
                foreach (string key in _warningOrder) {
                    if (n++ >= 5) break;
                    _echoBuffer.Append("  ").Append(key).Append('\n');
                }
            }
            Echo(_echoBuffer.ToString());
        }
    }
}
