using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript {
    public partial class Program : MyGridProgram {
        /// <summary>Resolved blueprint mapping: catalog item key (<c>Type/Subtype</c>) to the
        /// <see cref="MyDefinitionId"/> of the blueprint that produces it. Populated by the curated
        /// map, auto-probe, persisted CustomData, and passive queue observation.</summary>
        Dictionary<string, MyDefinitionId> _blueprintMap = new Dictionary<string, MyDefinitionId>(StringComparer.Ordinal);

        /// <summary>Item keys whose blueprint mapping was supplied by the user (typed manually into
        /// the <c>[GCraftBlueprints]</c> CustomData section). Manual entries are immune to
        /// auto-overwrite by curated/probe/learning paths.</summary>
        HashSet<string> _blueprintMapManual = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Item keys the resolver could not satisfy — surfaced on the status LCD as
        /// <c>Blocked: NeedsLearn</c> and deduped to <c>_warnings</c>. Cleared when learning catches
        /// the blueprint on a user-queued probe item.</summary>
        HashSet<string> _blueprintMapNeedsLearn = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Reusable buffer for rendering the <c>[GCraft]</c> LCD CustomData.</summary>
        StringBuilder _gcraftCustomDataSB = new StringBuilder();

        /// <summary>Section header used in the <c>[GCraft]</c> LCD CustomData for the blueprint map.</summary>
        internal const string AutocraftBlueprintSectionHeader = "[GCraftBlueprints]";

        /// <summary>Section header used in the <c>[GCraft]</c> LCD CustomData for autocraft quotas.</summary>
        internal const string AutocraftQuotaSectionHeader = "[Goose]";

        /// <summary>Object-builder type prefix the SE blueprint API expects.</summary>
        internal const string AutocraftBlueprintTypePrefix = "MyObjectBuilder_BlueprintDefinition/";

        /// <summary>Curated mismatch map from vanilla item subtype to blueprint subtype. Covers items
        /// whose blueprint subtype is not a 1:1 match of the item subtype (e.g. <c>Construction</c> →
        /// <c>ConstructionComponent</c>). Resolution checks this map before falling back to
        /// auto-probe. Modded items not covered here are still resolved automatically when the
        /// blueprint subtype matches the item subtype directly.</summary>
        internal static readonly Dictionary<string, string> AutocraftCuratedBlueprintSubtype = new Dictionary<string, string>(StringComparer.Ordinal) {
            { "Construction",       "ConstructionComponent" },
            { "Computer",           "ComputerComponent" },
            { "Detector",           "DetectorComponent" },
            { "Explosives",         "ExplosivesComponent" },
            { "Girder",             "GirderComponent" },
            { "Medical",            "MedicalComponent" },
            { "Motor",              "MotorComponent" },
            { "RadioCommunication", "RadioCommunicationComponent" },
            { "Reactor",            "ReactorComponent" },
            { "Thrust",             "ThrustComponent" },
        };

        /// <summary>Extracts the subtype portion of a catalog item key produced by
        /// <see cref="Catalog_BuildKey"/> (e.g. <c>Component/SteelPlate</c> → <c>SteelPlate</c>).
        /// Returns an empty string for malformed keys.</summary>
        /// <param name="itemKey">The catalog item key (un-prefixed <c>Type/Subtype</c>).</param>
        /// <returns>The subtype, or empty string when the key is null/empty/has no separator.</returns>
        internal static string Autocraft_SubtypeFromKey(string itemKey) {
            if (string.IsNullOrEmpty(itemKey)) return string.Empty;
            int slash = itemKey.IndexOf('/');
            if (slash <= 0 || slash >= itemKey.Length - 1) return string.Empty;
            return itemKey.Substring(slash + 1);
        }

        /// <summary>Pure resolution chain (no SE-runtime dependency). Operates on candidate blueprint
        /// definition-id strings and a caller-supplied <paramref name="canUseBlueprint"/> predicate
        /// so the chain is fully unit-testable. Resolution order: cache → curated mismatch map →
        /// auto-probe (<c>&lt;S&gt;</c>, then <c>&lt;S&gt;Component</c> safety net).</summary>
        /// <param name="itemKey">Catalog item key (un-prefixed <c>Type/Subtype</c>).</param>
        /// <param name="cached">Previously resolved mappings keyed by <paramref name="itemKey"/>.</param>
        /// <param name="curated">Subtype-to-blueprint-subtype overrides for vanilla mismatches.</param>
        /// <param name="canUseBlueprint">Predicate invoked with each candidate definition-id string;
        /// returns <c>true</c> when at least one assembler can produce that blueprint.</param>
        /// <returns>The first candidate that resolves, or <c>null</c> when nothing matches
        /// (signals <c>NeedsLearn</c>).</returns>
        internal static string Autocraft_ResolveBlueprintIdString(
            string itemKey,
            IDictionary<string, string> cached,
            IDictionary<string, string> curated,
            Func<string, bool> canUseBlueprint) {
            if (cached != null) {
                string cachedHit;
                if (cached.TryGetValue(itemKey, out cachedHit) && !string.IsNullOrEmpty(cachedHit)) {
                    return cachedHit;
                }
            }
            string subtype = Autocraft_SubtypeFromKey(itemKey);
            if (subtype.Length == 0) return null;
            if (canUseBlueprint == null) return null;

            if (curated != null) {
                string overrideSub;
                if (curated.TryGetValue(subtype, out overrideSub) && !string.IsNullOrEmpty(overrideSub)) {
                    string id = AutocraftBlueprintTypePrefix + overrideSub;
                    if (canUseBlueprint(id)) return id;
                }
            }

            string idDirect = AutocraftBlueprintTypePrefix + subtype;
            if (canUseBlueprint(idDirect)) return idDirect;

            string idSafety = AutocraftBlueprintTypePrefix + subtype + "Component";
            if (canUseBlueprint(idSafety)) return idSafety;

            return null;
        }

        /// <summary>Parses the <c>[GCraftBlueprints]</c> section of a CustomData string into a
        /// dictionary of user-typed mappings (<c>ItemKey=DefinitionId</c>). Ignores comments,
        /// whitespace, and entries outside the section. Idempotent on malformed input.</summary>
        /// <param name="customData">Raw CustomData blob (may be empty or null).</param>
        /// <param name="outUserEntries">Caller-allocated dictionary; cleared and populated with
        /// every uncommented <c>key=value</c> line found inside the section.</param>
        internal static void Autocraft_ParseGCraftBlueprintsSection(
            string customData,
            IDictionary<string, string> outUserEntries) {
            if (outUserEntries == null) return;
            outUserEntries.Clear();
            if (string.IsNullOrEmpty(customData)) return;

            string[] lines = customData.Split('\n');
            bool inSection = false;
            for (int i = 0; i < lines.Length; i++) {
                string trimmed = lines[i].TrimEnd('\r').Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal)) {
                    inSection = trimmed.Equals(AutocraftBlueprintSectionHeader, StringComparison.Ordinal);
                    continue;
                }
                if (!inSection) continue;
                if (trimmed.StartsWith(";", StringComparison.Ordinal)) continue;
                if (trimmed.StartsWith("#", StringComparison.Ordinal)) continue;
                int eq = trimmed.IndexOf('=');
                if (eq <= 0 || eq >= trimmed.Length - 1) continue;
                string key = trimmed.Substring(0, eq).Trim();
                string val = trimmed.Substring(eq + 1).Trim();
                if (key.Length == 0 || val.Length == 0) continue;
                outUserEntries[key] = val;
            }
        }

        /// <summary>Parses the <c>[Goose]</c> section of a CustomData string and returns each
        /// user-active quota line keyed by its catalog item key. Active means uncommented and
        /// well-formed. The value preserves the original <c>key=value</c> text verbatim so the
        /// regenerator can round-trip without reformatting.</summary>
        /// <param name="customData">Raw CustomData blob (may be empty or null).</param>
        /// <param name="outActiveQuotaLines">Caller-allocated dictionary; cleared and populated.</param>
        internal static void Autocraft_ParseGooseQuotaSection(
            string customData,
            IDictionary<string, string> outActiveQuotaLines) {
            if (outActiveQuotaLines == null) return;
            outActiveQuotaLines.Clear();
            if (string.IsNullOrEmpty(customData)) return;

            string[] lines = customData.Split('\n');
            bool inSection = false;
            for (int i = 0; i < lines.Length; i++) {
                string trimmed = lines[i].TrimEnd('\r').Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal)) {
                    inSection = trimmed.Equals(AutocraftQuotaSectionHeader, StringComparison.Ordinal);
                    continue;
                }
                if (!inSection) continue;
                if (trimmed.StartsWith(";", StringComparison.Ordinal)) continue;
                if (trimmed.StartsWith("#", StringComparison.Ordinal)) continue;
                int eq = trimmed.IndexOf('=');
                if (eq <= 0 || eq >= trimmed.Length - 1) continue;
                string key = trimmed.Substring(0, eq).Trim();
                string val = trimmed.Substring(eq + 1).Trim();
                if (key.Length == 0 || val.Length == 0) continue;
                outActiveQuotaLines[key] = key + "=" + val;
            }
        }

        /// <summary>Builds the desired CustomData blob for the <c>[GCraft]</c> LCD by merging
        /// <list type="bullet">
        /// <item>active user quotas (preserved verbatim),</item>
        /// <item>ignore-default lines (<c>key=x</c>) for catalog items the user hasn't managed,</item>
        /// <item>resolved blueprint mappings (auto + user-typed) under
        /// <c>[GCraftBlueprints]</c>.</item>
        /// </list>
        /// User-typed blueprint entries take precedence over <paramref name="autoBlueprintEntries"/>
        /// when both supply a value for the same key.</summary>
        /// <param name="autoBlueprintEntries">Auto-resolved blueprint map (<c>itemKey → defId</c>).</param>
        /// <param name="userBlueprintEntries">User-typed overrides parsed from CustomData.</param>
        /// <param name="activeQuotaLines">Active <c>[Goose]</c> quota lines keyed by item key.</param>
        /// <param name="catalogKeys">Every known item key (for hint generation in the quota section).</param>
        /// <param name="reusableSB">Optional scratch <see cref="StringBuilder"/>; allocated if null.</param>
        /// <returns>The fully rendered CustomData blob.</returns>
        internal static string Autocraft_BuildGCraftCustomData(
            IDictionary<string, string> autoBlueprintEntries,
            IDictionary<string, string> userBlueprintEntries,
            IDictionary<string, string> activeQuotaLines,
            IEnumerable<string> catalogKeys,
            StringBuilder reusableSB) {
            StringBuilder sb = reusableSB ?? new StringBuilder();
            sb.Length = 0;

            sb.Append(AutocraftQuotaSectionHeader).Append('\n');
            sb.Append(";Quotas: N=minimum, NE=exact, x=ignore. See wiki.\n");

            HashSet<string> mergedKeys = new HashSet<string>(StringComparer.Ordinal);
            if (catalogKeys != null) {
                foreach (string k in catalogKeys) {
                    if (!string.IsNullOrEmpty(k)) mergedKeys.Add(k);
                }
            }
            if (activeQuotaLines != null) {
                foreach (string k in activeQuotaLines.Keys) {
                    if (!string.IsNullOrEmpty(k)) mergedKeys.Add(k);
                }
            }
            if (mergedKeys.Count > 0) {
                List<string> sortedKeys = new List<string>(mergedKeys);
                sortedKeys.Sort(StringComparer.Ordinal);
                for (int i = 0; i < sortedKeys.Count; i++) {
                    string key = sortedKeys[i];
                    string active;
                    if (activeQuotaLines != null && activeQuotaLines.TryGetValue(key, out active)) {
                        sb.Append(active).Append('\n');
                    } else {
                        sb.Append(key).Append("=x\n");
                    }
                }
            }

            sb.Append('\n').Append(AutocraftBlueprintSectionHeader).Append('\n');
            sb.Append(";Auto-generated; user entries win. See wiki.\n");

            HashSet<string> bpKeys = new HashSet<string>(StringComparer.Ordinal);
            if (autoBlueprintEntries != null) {
                foreach (string k in autoBlueprintEntries.Keys) {
                    if (!string.IsNullOrEmpty(k)) bpKeys.Add(k);
                }
            }
            if (userBlueprintEntries != null) {
                foreach (string k in userBlueprintEntries.Keys) {
                    if (!string.IsNullOrEmpty(k)) bpKeys.Add(k);
                }
            }
            if (bpKeys.Count > 0) {
                List<string> sortedBpKeys = new List<string>(bpKeys);
                sortedBpKeys.Sort(StringComparer.Ordinal);
                for (int i = 0; i < sortedBpKeys.Count; i++) {
                    string key = sortedBpKeys[i];
                    string value;
                    if (userBlueprintEntries != null && userBlueprintEntries.TryGetValue(key, out value)) {
                        sb.Append(key).Append('=').Append(value).Append('\n');
                        continue;
                    }
                    if (autoBlueprintEntries != null && autoBlueprintEntries.TryGetValue(key, out value)) {
                        sb.Append(key).Append('=').Append(value).Append('\n');
                    }
                }
            }

            return sb.ToString();
        }

        /// <summary>Attempts to resolve a blueprint for <paramref name="itemKey"/> against the
        /// supplied assembler pool. Runs the static resolution chain through the SE-runtime
        /// <see cref="MyDefinitionId.TryParse(string, out MyDefinitionId)"/> +
        /// <see cref="IMyAssembler.CanUseBlueprint(MyDefinitionId)"/> probe. On success, the result
        /// is cached in <see cref="_blueprintMap"/> and the key is removed from
        /// <see cref="_blueprintMapNeedsLearn"/>. On failure, the key is added to the
        /// <c>NeedsLearn</c> set.</summary>
        /// <param name="itemKey">Catalog item key to resolve.</param>
        /// <param name="assemblers">Pool used for <c>CanUseBlueprint</c> probing.</param>
        /// <param name="blueprintId">Resolved blueprint id, or <c>default</c> on failure.</param>
        /// <returns><c>true</c> when a blueprint was resolved.</returns>
        bool Autocraft_TryResolve(string itemKey, IList<IMyAssembler> assemblers, out MyDefinitionId blueprintId) {
            blueprintId = default(MyDefinitionId);
            if (string.IsNullOrEmpty(itemKey)) return false;

            MyDefinitionId cached;
            if (_blueprintMap.TryGetValue(itemKey, out cached)) {
                blueprintId = cached;
                _blueprintMapNeedsLearn.Remove(itemKey);
                return true;
            }
            if (assemblers == null || assemblers.Count == 0) {
                _blueprintMapNeedsLearn.Add(itemKey);
                return false;
            }

            Dictionary<string, string> emptyCache = null;
            string resolved = Autocraft_ResolveBlueprintIdString(
                itemKey,
                emptyCache,
                AutocraftCuratedBlueprintSubtype,
                candidateId => Autocraft_AnyAssemblerCanUse(candidateId, assemblers));

            if (string.IsNullOrEmpty(resolved)) {
                _blueprintMapNeedsLearn.Add(itemKey);
                return false;
            }

            MyDefinitionId parsed;
            if (!MyDefinitionId.TryParse(resolved, out parsed)) {
                _blueprintMapNeedsLearn.Add(itemKey);
                return false;
            }
            _blueprintMap[itemKey] = parsed;
            _blueprintMapNeedsLearn.Remove(itemKey);
            blueprintId = parsed;
            return true;
        }

        /// <summary>Returns <c>true</c> when <paramref name="candidateId"/> parses to a valid
        /// definition and any assembler in <paramref name="assemblers"/> reports
        /// <see cref="IMyAssembler.CanUseBlueprint(MyDefinitionId)"/>.</summary>
        bool Autocraft_AnyAssemblerCanUse(string candidateId, IList<IMyAssembler> assemblers) {
            if (string.IsNullOrEmpty(candidateId)) return false;
            MyDefinitionId parsed;
            if (!MyDefinitionId.TryParse(candidateId, out parsed)) return false;
            for (int i = 0; i < assemblers.Count; i++) {
                IMyAssembler a = assemblers[i];
                if (a == null) continue;
                try {
                    if (a.CanUseBlueprint(parsed)) return true;
                } catch (Exception) {
                    // Modded assemblers may throw on unknown definitions; treat as "no".
                }
            }
            return false;
        }

        /// <summary>Restores the blueprint map from the <c>[GCraftBlueprints]</c> section of an
        /// LCD's CustomData. User-typed entries (anything parsed from the section) are flagged in
        /// <see cref="_blueprintMapManual"/> so subsequent merges preserve them verbatim.</summary>
        /// <param name="customData">Raw CustomData of the <c>[GCraft]</c> LCD.</param>
        /// <param name="parseScratch">Caller-supplied scratch dictionary (reused across ticks).</param>
        void Autocraft_LoadBlueprintMapFromCustomData(string customData, Dictionary<string, string> parseScratch) {
            Autocraft_ParseGCraftBlueprintsSection(customData, parseScratch);
            _blueprintMapManual.Clear();
            foreach (var kv in parseScratch) {
                MyDefinitionId parsed;
                if (!MyDefinitionId.TryParse(kv.Value, out parsed)) continue;
                _blueprintMap[kv.Key] = parsed;
                _blueprintMapManual.Add(kv.Key);
                _blueprintMapNeedsLearn.Remove(kv.Key);
            }
        }
    }
}
