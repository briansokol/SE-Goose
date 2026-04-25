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
        Queue<string> _actionLog = new Queue<string>();
        List<string> _warningOrder = new List<string>();
        Dictionary<string, int> _warnings = new Dictionary<string, int>();
        HashSet<string> _oneShotWarnedKeys = new HashSet<string>();

        void LogAction(string msg) {
            _actionLog.Enqueue(msg);
            while (_actionLog.Count > _config.MaxActionLogEntries) _actionLog.Dequeue();
        }

        void LogWarning(string msg) {
            int count;
            if (_warnings.TryGetValue(msg, out count)) {
                _warnings[msg] = count + 1;
            } else {
                if (_warnings.Count >= _config.MaxWarningEntries) {
                    // Evict oldest by insertion order.
                    string oldest = _warningOrder[0];
                    _warningOrder.RemoveAt(0);
                    _warnings.Remove(oldest);
                }
                _warnings[msg] = 1;
                _warningOrder.Add(msg);
            }
        }

        void LogWarningOnce(string key, string msg) {
            if (_oneShotWarnedKeys.Add(key)) LogWarning(msg);
        }

        void ResetOneShotWarnings() { _oneShotWarnedKeys.Clear(); }

        void LogError(string context, Exception ex) {
            LogWarning("[" + context + "] " + ex.GetType().Name + ": " + ex.Message);
        }

        StringBuilder _echoBuffer = new StringBuilder();
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
                // Last item only — keeps Echo readable.
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
