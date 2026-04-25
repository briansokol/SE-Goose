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
        bool _paused;
        MyCommandLine _cmd = new MyCommandLine();
        Dictionary<string, Action<MyCommandLine>> _commands;

        public Program() {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            InitCommands();
            _workIterator = StepRoot();
            LogAction("Goose v1 initialized");
        }

        public void Save() {
            // v1: no learned state to persist.
        }

        public void Main(string argument, UpdateType updateSource) {
            try {
                if (!string.IsNullOrEmpty(argument)) {
                    DispatchCommand(argument);
                }
                if ((updateSource & UpdateType.Update10) != 0 && !_paused) {
                    RunOneTick();
                }
            } catch (Exception ex) {
                LogError("Main", ex);
            }
            RenderEchoStatus();
        }

        void InitCommands() {
            _commands = new Dictionary<string, Action<MyCommandLine>>(StringComparer.OrdinalIgnoreCase) {
                { "rescan", c => { _rescanRequested = true; _configDirty = true; LogAction("cmd: rescan"); } },
                { "pause",  c => { _paused = true;  LogAction("cmd: pause"); } },
                { "resume", c => { _paused = false; LogAction("cmd: resume"); } },
                { "debug",  c => {
                    if (c.ArgumentCount > 1) {
                        bool on = string.Equals(c.Argument(1), "on", StringComparison.OrdinalIgnoreCase);
                        _config.DebugLogging = on;
                        LogAction("cmd: debug " + (on ? "on" : "off"));
                    }
                } }
            };
        }

        void DispatchCommand(string argument) {
            if (!_cmd.TryParse(argument)) {
                LogWarning("Unparseable argument: " + argument);
                return;
            }
            if (_cmd.ArgumentCount == 0) return;
            string verb = _cmd.Argument(0);
            Action<MyCommandLine> handler;
            if (_commands.TryGetValue(verb, out handler)) {
                handler(_cmd);
            } else {
                LogWarning("Unknown command: " + verb);
            }
        }
    }
}
