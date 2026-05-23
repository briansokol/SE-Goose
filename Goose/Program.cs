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
        /// <summary>True while the script is paused via the <c>pause</c> command.</summary>
        private bool _paused;

        /// <summary>Reusable parser for arguments passed to <see cref="Main"/>.</summary>
        private readonly MyCommandLine _cmd = new MyCommandLine();

        /// <summary>Map of command verb to handler, populated by <see cref="InitCommands"/>.</summary>
        private Dictionary<string, Action<MyCommandLine>> _commands;

        /// <summary>Sets the update cadence, wires up commands, and primes the work iterator.</summary>
        public Program()
        {
            Catalog_LoadFromStorage();
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            InitCommands();
            _workIterator = StepRoot();
            LogAction("Goose v1 initialized");
        }

        /// <summary>Persists the observed-item catalog so quota templates survive recompile.</summary>
        public void Save()
        {
            Storage = Catalog_BuildStorageBlob();
        }

        /// <summary>Entry point invoked by the programmable block on every update tick.</summary>
        /// <param name="argument">Optional command argument from a terminal action or trigger.</param>
        /// <param name="updateSource">Flags describing why this tick fired.</param>
        public void Main(string argument, UpdateType updateSource)
        {
            try
            {
                if (!string.IsNullOrEmpty(argument))
                {
                    DispatchCommand(argument);
                }
                if ((updateSource & UpdateType.Update10) != 0 && !_paused)
                {
                    RunOneTick();
                }
            }
            catch (Exception ex)
            {
                LogError("Main", ex);
            }
            RenderEchoStatus();
        }

        /// <summary>Builds the command verb dispatch table.</summary>
        private void InitCommands()
        {
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
                } },
                { "reset-scope", c => {
                    _scopeGrids.Clear();
                    _rescanRequested = true;
                    LogAction("cmd: reset-scope");
                } }
            };
        }

        /// <summary>Parses <paramref name="argument"/> and routes its verb to the matching handler.</summary>
        /// <param name="argument">Raw argument string (e.g. <c>"debug on"</c>).</param>
        private void DispatchCommand(string argument)
        {
            if (!_cmd.TryParse(argument))
            {
                LogWarning("Unparseable argument: " + argument);
                return;
            }
            if (_cmd.ArgumentCount == 0)
            {
                return;
            }

            string verb = _cmd.Argument(0);
            Action<MyCommandLine> handler;
            if (_commands.TryGetValue(verb, out handler))
            {
                handler(_cmd);
            }
            else
            {
                LogWarning("Unknown command: " + verb);
            }
        }
    }
}
