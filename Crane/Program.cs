using Sandbox.ModAPI.Ingame;
using System;

namespace IngameScript {
    /// <summary>
    /// Crane: companion script to Goose. Handles autocrafting and refinery (production) management
    /// across a configured block group. Designed to run alongside Goose on a separate programmable block.
    /// </summary>
    public partial class Program : MyGridProgram {
        /// <summary>Sets the update cadence and prepares the script for first tick.</summary>
        public Program() {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            Echo("Crane v0 initialized");
        }

        /// <summary>Persists state between recompiles. Wire up once persistent state exists.</summary>
        public void Save() {
        }

        /// <summary>Entry point invoked by the programmable block on every update tick.</summary>
        /// <param name="argument">Optional command argument from a terminal action or trigger.</param>
        /// <param name="updateSource">Flags describing why this tick fired.</param>
        public void Main(string argument, UpdateType updateSource) {
            try {
                if ((updateSource & UpdateType.Update100) != 0) {
                    // Production loop lands here.
                }
            } catch (Exception ex) {
                Echo("Crane error: " + ex.Message);
            }
        }
    }
}
