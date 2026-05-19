using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript {
    /// <summary>
    /// Crane: companion script to Goose. Handles autocrafting across a configured block group.
    /// Designed to run alongside Goose on a separate programmable block.
    /// </summary>
    public partial class Program : MyGridProgram {
        /// <summary>Shared runtime logger (action log, warnings, Echo status).</summary>
        RuntimeLogger _logger;

        /// <summary>Shared observed-item catalog.</summary>
        readonly ItemCatalog _catalog = new ItemCatalog();

        /// <summary>Shared pipeline dispatcher.</summary>
        WorkDispatcher _dispatcher;

        /// <summary>True while the script is paused via the <c>pause</c> command.</summary>
        bool _paused;

        /// <summary>Reusable parser for arguments passed to <see cref="Main"/>.</summary>
        readonly MyCommandLine _cmd = new MyCommandLine();

        /// <summary>Map of command verb to handler, populated by <see cref="InitCommands"/>.</summary>
        Dictionary<string, Action<MyCommandLine>> _commands;

        /// <summary>Sets the update cadence, wires up commands, and primes the work iterator.</summary>
        public Program() {
            _logger = new RuntimeLogger(Echo, "Crane v1");
            _catalog.LoadFromStorage(Storage);
            _dispatcher = new WorkDispatcher(CraneStepLabels, StepFor, BudgetExceeded,
                () => _logger.ResetOneShotWarnings(),
                (ctx, ex) => _logger.LogError(ctx, ex));
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            InitCommands();
            _logger.LogAction("Crane v1 initialized");
        }

        /// <summary>Persists the observed-item catalog so quota templates survive recompile.</summary>
        public void Save() {
            Storage = _catalog.BuildStorageBlob();
        }

        /// <summary>Entry point invoked by the programmable block on every update tick.</summary>
        public void Main(string argument, UpdateType updateSource) {
            try {
                if (!string.IsNullOrEmpty(argument)) {
                    DispatchCommand(argument);
                }
                if ((updateSource & UpdateType.Update100) != 0 && !_paused) {
                    _dispatcher.RunOneTick();
                }
            } catch (Exception ex) {
                _logger.LogError("Main", ex);
            }
            _logger.RenderEchoStatus(_paused, _dispatcher.StepIndex, _dispatcher.SubStep,
                _dispatcher.StepLabel, Runtime.CurrentInstructionCount, Runtime.MaxInstructionCount,
                Runtime.LastRunTimeMs);
        }

        /// <summary>Builds the command verb dispatch table.</summary>
        void InitCommands() {
            _commands = new Dictionary<string, Action<MyCommandLine>>(StringComparer.OrdinalIgnoreCase) {
                { "rescan", c => { _rescanRequested = true; _configDirty = true; _logger.LogAction("cmd: rescan"); } },
                { "pause",  c => { _paused = true;  _logger.LogAction("cmd: pause"); } },
                { "resume", c => { _paused = false; _logger.LogAction("cmd: resume"); } },
                { "debug",  c => {
                    if (c.ArgumentCount > 1) {
                        bool on = string.Equals(c.Argument(1), "on", StringComparison.OrdinalIgnoreCase);
                        _config.DebugLogging = on;
                        _logger.LogAction("cmd: debug " + (on ? "on" : "off"));
                    }
                } },
                { "reset-scope", c => {
                    _scopeGrids.Clear();
                    _rescanRequested = true;
                    _logger.LogAction("cmd: reset-scope");
                } }
            };
        }

        /// <summary>Parses <paramref name="argument"/> and routes its verb to the matching handler.</summary>
        void DispatchCommand(string argument) {
            if (!_cmd.TryParse(argument)) {
                _logger.LogWarning("Unparseable argument: " + argument);
                return;
            }
            if (_cmd.ArgumentCount == 0) return;
            string verb = _cmd.Argument(0);
            Action<MyCommandLine> handler;
            if (_commands.TryGetValue(verb, out handler)) {
                handler(_cmd);
            } else {
                _logger.LogWarning("Unknown command: " + verb);
            }
        }
    }
}
