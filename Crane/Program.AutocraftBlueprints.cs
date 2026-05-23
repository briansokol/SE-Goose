using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;
using VRage.Game;
using VRage.Game.GUI.TextPanel;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        /// <summary>Resolved blueprint subtype map keyed by catalog key (un-prefixed <c>Type/Subtype</c>). Survives recompile via the <c>[CCraftBlueprints]</c> section.</summary>
        private readonly Dictionary<string, string> _blueprintMap = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Catalog keys flagged as needing operator-driven manual seeding.</summary>
        private readonly HashSet<string> _needsLearn = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>3-tier blueprint resolver. Cache → curated → direct probe → NeedsLearn.</summary>
        /// <param name="asm">Assembler used as the probe target. Must support <c>CanUseBlueprint</c>.</param>
        /// <param name="t">Quota line being resolved.</param>
        /// <returns>Resolved blueprint definition id, or <c>null</c> when the blueprint cannot be determined.</returns>
        private MyDefinitionId? ResolveBlueprintForTarget(IMyAssembler asm, AutocraftTarget t)
        {
            string cached;
            if (_blueprintMap.TryGetValue(t.Key, out cached))
            {
                return TryMakeBlueprintId(cached);
            }
            string itemSubtype = t.ItemType.SubtypeId ?? string.Empty;
            string curated;
            if (BlueprintMisses.CuratedMap.TryGetValue(itemSubtype, out curated))
            {
                MyDefinitionId? bp = TryMakeBlueprintId(curated);
                if (bp.HasValue && asm != null)
                {
                    try
                    {
                        if (asm.CanUseBlueprint(bp.Value))
                        {
                            _blueprintMap[t.Key] = curated;
                            return bp;
                        }
                    }
                    catch { }
                }
            }
            MyDefinitionId? direct = TryMakeBlueprintId(itemSubtype);
            if (direct.HasValue && asm != null)
            {
                try
                {
                    if (asm.CanUseBlueprint(direct.Value))
                    {
                        _blueprintMap[t.Key] = itemSubtype;
                        return direct;
                    }
                }
                catch { }
            }
            if (_needsLearn.Add(t.Key))
            {
                _logger.LogWarningOnce("bp:needs-learn:" + t.Key,
                    "[Crane] Blueprint for '" + t.Key + "' not resolved. Manually queue one to seed.");
            }
            return null;
        }

        /// <summary>Constructs a <see cref="MyDefinitionId"/> for a blueprint subtype, returning null on parse failure.</summary>
        private static MyDefinitionId? TryMakeBlueprintId(string subtype)
        {
            if (string.IsNullOrEmpty(subtype))
            {
                return null;
            }

            MyDefinitionId id;
            if (MyDefinitionId.TryParse("MyObjectBuilder_BlueprintDefinition/" + subtype, out id))
            {
                return id;
            }

            return null;
        }

        /// <summary>Passive learning pass. Walks every assembler's queue and, for any blueprint subtype that matches a known <see cref="_needsLearn"/> catalog key (by item subtype or curated map), records the mapping into <see cref="_blueprintMap"/> and drops the NeedsLearn flag.</summary>
        private void PassiveLearnFromQueues()
        {
            if (_needsLearn.Count == 0)
            {
                return;
            }

            for (int a = 0; a < _assemblers.Count; a++)
            {
                IMyAssembler asm = _assemblers[a];
                if (asm == null)
                {
                    continue;
                }

                _queueBuffer.Clear();
                try
                { asm.GetQueue(_queueBuffer); }
                catch { continue; }
                for (int q = 0; q < _queueBuffer.Count; q++)
                {
                    string bpSub = _queueBuffer[q].BlueprintId.SubtypeName ?? string.Empty;
                    foreach (string key in _needsLearn)
                    {
                        if (_blueprintMap.ContainsKey(key))
                        {
                            continue;
                        }

                        string itemSub;
                        if (!TryGetItemSubtypeForKey(key, out itemSub))
                        {
                            continue;
                        }

                        if (bpSub == itemSub)
                        {
                            _blueprintMap[key] = bpSub;
                        }
                        else
                        {
                            string curated;
                            if (BlueprintMisses.CuratedMap.TryGetValue(itemSub, out curated) && curated == bpSub)
                            {
                                _blueprintMap[key] = bpSub;
                            }
                        }
                    }
                }
            }
            List<string> learned = null;
            foreach (string key in _needsLearn)
            {
                if (_blueprintMap.ContainsKey(key))
                {
                    if (learned == null)
                    {
                        learned = new List<string>();
                    }

                    learned.Add(key);
                }
            }
            if (learned != null)
            {
                for (int i = 0; i < learned.Count; i++)
                {
                    _needsLearn.Remove(learned[i]);
                    _logger.LogAction("[Crane] Learned blueprint for " + learned[i]);
                }
            }
        }

        /// <summary>Returns the item subtype portion of a catalog key (e.g. <c>Component/SteelPlate</c> → <c>SteelPlate</c>).</summary>
        private static bool TryGetItemSubtypeForKey(string key, out string subtype)
        {
            subtype = null;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            int slash = key.IndexOf('/');
            if (slash < 0 || slash >= key.Length - 1)
            {
                return false;
            }

            subtype = key.Substring(slash + 1);
            return true;
        }

        /// <summary>Renders the <c>[CCraft]</c> status surface and the <c>[CError]</c> warning surface. Also runs passive blueprint learning and persists the <c>[CCraftBlueprints]</c> section on diff.</summary>
        private IEnumerator<YieldReason> StepRenderStatus()
        {
            PassiveLearnFromQueues();
            RenderCCraftStatus();
            RenderCErrorStatus();
            PersistBlueprintsSection();
            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>Writes the autocraft status block to every <c>[CCraft]</c> LCD surface.</summary>
        private void RenderCCraftStatus()
        {
            StringBuilder sb = _statusBuffer;
            sb.Length = 0;
            sb.Append("=== Crane Autocraft ===\n");
            sb.Append("Pool: ").Append(_assemblers.Count).Append(" assembler(s)\n");
            if (_reservedAsmEntityId != 0)
            {
                sb.Append("Reserved (Disassemble): ").Append(FindAssemblerName(_reservedAsmEntityId)).Append('\n');
            }
            sb.Append("ITEM                  TARGET    ACTUAL    STATUS\n");
            for (int i = 0; i < _autocraftTargets.Count; i++)
            {
                AutocraftTarget t = _autocraftTargets[i];
                long actual;
                _itemTotals.TryGetValue(t.ItemType, out actual);
                AutocraftLineStatus status;
                _quotaStatus.TryGetValue(t.Key, out status);
                long queued;
                _quotaQueued.TryGetValue(t.Key, out queued);
                int asmCount;
                _quotaAsmCount.TryGetValue(t.Key, out asmCount);

                sb.Append(PadRight(ShortenKey(t.Key), 22));
                string targetStr = (t.Mode == AutocraftMode.Exact ? "E " : "") + t.Target.ToString();
                sb.Append(PadRight(targetStr, 10));
                sb.Append(PadRight(actual.ToString(), 10));
                sb.Append(status.ToString());
                if (queued > 0)
                {
                    sb.Append(" (").Append(queued).Append(" queued, ").Append(asmCount).Append(" asm)");
                }

                sb.Append('\n');
            }
            string text = sb.ToString();
            for (int i = 0; i < _ccraftLcds.Count; i++)
            {
                IMyTextSurface surface = _ccraftLcds[i];
                if (surface != null)
                {
                    surface.ContentType = ContentType.TEXT_AND_IMAGE;
                    surface.WriteText(text, false);
                }
            }
        }

        /// <summary>Writes the warning log to every <c>[CError]</c> LCD surface.</summary>
        private void RenderCErrorStatus()
        {
            if (_cerrorLcds.Count == 0)
            {
                return;
            }

            var sb = new StringBuilder();
            sb.Append("=== Crane Errors ===\n");
            if (_logger.Warnings.Count == 0)
            {
                sb.Append("(no errors)\n");
            }
            else
            {
                foreach (KeyValuePair<string, int> kv in _logger.Warnings)
                {
                    sb.Append(kv.Key);
                    if (kv.Value > 1)
                    {
                        sb.Append(" (x").Append(kv.Value).Append(')');
                    }

                    sb.Append('\n');
                }
            }
            string text = sb.ToString();
            for (int i = 0; i < _cerrorLcds.Count; i++)
            {
                IMyTextSurface surface = _cerrorLcds[i];
                if (surface != null)
                {
                    surface.ContentType = ContentType.TEXT_AND_IMAGE;
                    surface.WriteText(text, false);
                }
            }
        }

        /// <summary>Persists the <c>[CCraftBlueprints]</c> section into the <c>[CCraft]</c> LCD CustomData. Writes only on diff. Section is sorted by catalog key.</summary>
        private void PersistBlueprintsSection()
        {
            if (_ccraftConfigHost == null)
            {
                return;
            }

            string current = _ccraftConfigHost.CustomData ?? string.Empty;
            string existing = ExtractSectionVerbatim(current, "[CCraftBlueprints]");

            var sb = new StringBuilder();
            sb.Append("[CCraftBlueprints]\n");
            var keys = new List<string>();
            foreach (KeyValuePair<string, string> kv in _blueprintMap)
            {
                keys.Add(kv.Key);
            }

            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                sb.Append(keys[i]).Append('=').Append(_blueprintMap[keys[i]]).Append('\n');
            }
            string desired = sb.ToString();
            if (string.Equals(existing.TrimEnd('\n', '\r', ' '), desired.TrimEnd('\n', '\r', ' '), StringComparison.Ordinal))
            {
                return;
            }

            // Splice: keep everything except the existing [CCraftBlueprints] section, then append desired.
            string keepBody = StripSectionVerbatim(current, "[CCraftBlueprints]");
            string updated;
            if (string.IsNullOrEmpty(keepBody))
            {
                updated = desired;
            }
            else if (keepBody.EndsWith("\n", StringComparison.Ordinal))
            {
                updated = keepBody + desired;
            }
            else
            {
                updated = keepBody + "\n" + desired;
            }
            _ccraftConfigHost.CustomData = updated;
        }

        /// <summary>Loads <c>[CCraftBlueprints]</c> entries into <see cref="_blueprintMap"/>.</summary>
        private void LoadBlueprintsSection(string customData)
        {
            if (string.IsNullOrEmpty(customData))
            {
                return;
            }

            string[] lines = customData.Split('\n');
            bool inSection = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimEnd('\r').Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    inSection = trimmed.Equals("[CCraftBlueprints]", StringComparison.Ordinal);
                    continue;
                }
                if (!inSection)
                {
                    continue;
                }

                if (trimmed.StartsWith(";", StringComparison.Ordinal))
                {
                    continue;
                }

                int eq = trimmed.IndexOf('=');
                if (eq <= 0 || eq >= trimmed.Length - 1)
                {
                    continue;
                }

                string key = trimmed.Substring(0, eq).Trim();
                string val = trimmed.Substring(eq + 1).Trim();
                if (key.Length == 0 || val.Length == 0)
                {
                    continue;
                }

                _blueprintMap[key] = val;
            }
        }

        /// <summary>Removes the named section (header line + body up to next section header) from <paramref name="customData"/>.</summary>
        private static string StripSectionVerbatim(string customData, string sectionHeader)
        {
            if (string.IsNullOrEmpty(customData) || string.IsNullOrEmpty(sectionHeader))
            {
                return customData ?? string.Empty;
            }

            string[] lines = customData.Split('\n');
            int start = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimEnd('\r').Trim();
                if (trimmed.Equals(sectionHeader, StringComparison.Ordinal))
                {
                    start = i;
                    break;
                }
            }
            if (start < 0)
            {
                return customData;
            }

            int end = lines.Length;
            for (int i = start + 1; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimEnd('\r').Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    end = i;
                    break;
                }
            }
            var sb = new StringBuilder();
            for (int i = 0; i < start; i++)
            {
                sb.Append(lines[i]).Append('\n');
            }

            for (int i = end; i < lines.Length; i++)
            {
                sb.Append(lines[i]).Append(i < lines.Length - 1 ? "\n" : "");
            }

            return sb.ToString();
        }

        /// <summary>Right-pads a string to <paramref name="width"/> with spaces. Truncates when longer.</summary>
        private static string PadRight(string s, int width)
        {
            if (s == null)
            {
                s = string.Empty;
            }

            if (s.Length >= width)
            {
                return s.Substring(0, width);
            }

            return s + new string(' ', width - s.Length);
        }

        /// <summary>Shortens a catalog key for display by dropping the <c>Type/</c> prefix when present.</summary>
        private static string ShortenKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            int slash = key.IndexOf('/');
            if (slash < 0 || slash >= key.Length - 1)
            {
                return key;
            }

            return key.Substring(slash + 1);
        }

        /// <summary>Looks up an assembler's CustomName by EntityId. Returns the EntityId as a string when not found.</summary>
        private string FindAssemblerName(long entityId)
        {
            for (int i = 0; i < _assemblers.Count; i++)
            {
                if (_assemblers[i] != null && _assemblers[i].EntityId == entityId)
                {
                    return _assemblers[i].CustomName;
                }
            }
            return entityId.ToString();
        }
    }
}
