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
        /// <summary>High-level item buckets used to route inventory between containers.</summary>
        public enum ItemCategory {
            Ingots, Ores, Components, Prototech, Tools, Weapons,
            Ammo, Consumables, Ingredients, Meals, Misc
        }

        /// <summary>Bare tag tokens (no brackets) recognized in container names; index aligns with <see cref="ItemCategory"/>.</summary>
        static readonly string[] CategoryTags = {
            "Ingots", "Ores", "Components", "Prototech", "Tools", "Weapons",
            "Ammo", "Consumables", "Ingredients", "Meals", "Misc"
        };

        /// <summary>Cached metadata for a single managed inventory block.</summary>
        public class ContainerEntry {
            /// <summary>The terminal block this entry describes.</summary>
            public IMyTerminalBlock Block;

            /// <summary>Primary (index 0) inventory of <see cref="Block"/>.</summary>
            public IMyInventory Inventory;

            /// <summary>Routing priority; lower numbers win ties (default 100).</summary>
            public int Priority = 100;

            /// <summary>Categories this container accepts, parsed from its name tags.</summary>
            public List<ItemCategory> Categories = new List<ItemCategory>();

            /// <summary>True when the container is tagged <c>[Stock]</c>.</summary>
            public bool IsStock;

            /// <summary>Per-item stock quotas parsed from CustomData; <c>null</c> unless <see cref="IsStock"/> is true.</summary>
            public Dictionary<MyItemType, StockQuota> Quotas;
        }

        /// <summary>Routing buckets keyed by category, sorted by ascending priority.</summary>
        Dictionary<ItemCategory, List<ContainerEntry>> _containersByCategory =
            new Dictionary<ItemCategory, List<ContainerEntry>>();

        /// <summary>All <c>[Stock]</c>-tagged containers, sorted by ascending priority.</summary>
        List<ContainerEntry> _stockContainers = new List<ContainerEntry>();

        /// <summary>Reverse lookup from a block to its cached <see cref="ContainerEntry"/>.</summary>
        Dictionary<IMyTerminalBlock, ContainerEntry> _entryByBlock =
            new Dictionary<IMyTerminalBlock, ContainerEntry>();

        /// <summary>Manual classification overrides keyed by <c>TypeId/SubtypeId</c>.</summary>
        Dictionary<string, ItemCategory> _categoryOverrides = new Dictionary<string, ItemCategory>();

        /// <summary>Total quantity of each item type observed during the most recent scan.</summary>
        Dictionary<MyItemType, long> _itemTotals = new Dictionary<MyItemType, long>();

        /// <summary>Scratch buffer reused by inventory enumeration to avoid per-call allocations.</summary>
        List<MyInventoryItem> _itemBuffer = new List<MyInventoryItem>();

        /// <summary>Extracts the priority value from a <c>[P:&lt;n&gt;]</c> tag in a block name.</summary>
        /// <returns>The parsed priority, or 100 when no tag is present.</returns>
        int ParsePriorityFromName(string name) {
            if (string.IsNullOrEmpty(name)) return 100;
            int idx = name.IndexOf("[P:", StringComparison.Ordinal);
            if (idx < 0) return 100;
            int end = name.IndexOf(']', idx + 3);
            if (end < 0) return 100;
            string raw = name.Substring(idx + 3, end - idx - 3);
            int p;
            if (int.TryParse(raw, out p)) return p;
            return 100;
        }

        /// <summary>Returns true when <paramref name="name"/> contains <paramref name="tag"/> as a substring.</summary>
        bool NameHasTag(string name, string tag) {
            return !string.IsNullOrEmpty(name) && name.IndexOf(tag, StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Rebuilds <see cref="_containersByCategory"/>, <see cref="_stockContainers"/>, and
        /// <see cref="_entryByBlock"/> from name tags and CustomData on every managed block.
        /// </summary>
        IEnumerator<YieldReason> StepCategorizeContainers() {
            foreach (var kv in _containersByCategory) kv.Value.Clear();
            _stockContainers.Clear();
            _entryByBlock.Clear();

            int counter = 0;
            for (int b = 0; b < _allInventoryBlocks.Count; b++) {
                IMyTerminalBlock block = _allInventoryBlocks[b];
                if (!ValidateBlock(block)) continue;

                ContainerEntry entry = new ContainerEntry {
                    Block = block,
                    Inventory = block.GetInventory(0),
                    Priority = ParsePriorityFromName(block.CustomName),
                    IsStock = NameHasTag(block.CustomName, "[Stock]")
                };

                for (int c = 0; c < CategoryTags.Length; c++) {
                    if (NameHasTag(block.CustomName, CategoryTags[c])) {
                        ItemCategory cat = (ItemCategory)c;
                        entry.Categories.Add(cat);
                        List<ContainerEntry> bucket;
                        if (!_containersByCategory.TryGetValue(cat, out bucket)) {
                            bucket = new List<ContainerEntry>();
                            _containersByCategory[cat] = bucket;
                        }
                        bucket.Add(entry);
                    }
                }

                if (entry.IsStock) {
                    SyncStockTemplate(block);
                    entry.Quotas = new Dictionary<MyItemType, StockQuota>();
                    ParseStockQuotas(block, entry);
                    _stockContainers.Add(entry);
                }

                _entryByBlock[block] = entry;

                counter++;
                if (counter % 25 == 0) yield return YieldReason.ChunkBoundary;
                if (BudgetExceeded()) yield return YieldReason.BudgetHit;
            }

            foreach (var kv in _containersByCategory) {
                kv.Value.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            }
            _stockContainers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>Reads <c>[Goose]</c> CustomData entries on a stock container and populates
        /// <see cref="ContainerEntry.Quotas"/> with the parsed quotas.</summary>
        /// <param name="block">Stock-tagged terminal block whose CustomData holds the quotas.</param>
        /// <param name="entry">Owning entry to receive the parsed quota dictionary.</param>
        void ParseStockQuotas(IMyTerminalBlock block, ContainerEntry entry) {
            MyIniParseResult res;
            if (!_ini.TryParse(block.CustomData, out res)) {
                LogWarning("[Goose] Stock CustomData parse failed on '" + block.CustomName + "': " + res.ToString());
                return;
            }
            List<MyIniKey> keys = new List<MyIniKey>();
            _ini.GetKeys("Goose", keys);
            for (int i = 0; i < keys.Count; i++) {
                string key = keys[i].Name;
                string raw = _ini.Get(keys[i]).ToString();
                MyItemType type;
                StockQuota quota;
                if (TryReadStockQuota(key, raw, out type, out quota)) {
                    entry.Quotas[type] = quota;
                }
            }
        }


        /// <summary>Refreshes the canonical Goose CustomData document on a stock container,
        /// preserving user-active quota lines and merging the live observed-item catalog.
        /// Writes only when the rendered document differs from the current CustomData.</summary>
        /// <param name="block">Stock-tagged terminal block whose CustomData to sync.</param>
        void SyncStockTemplate(IMyTerminalBlock block) {
            string current = block.CustomData ?? string.Empty;

            List<string> userQuotas = new List<string>();
            string[] lines = current.Split('\n');
            bool inGooseSection = false;
            for (int i = 0; i < lines.Length; i++) {
                string trimmed = lines[i].TrimEnd('\r').Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal)) {
                    inGooseSection = trimmed.Equals("[Goose]", StringComparison.Ordinal);
                    continue;
                }
                if (!inGooseSection) continue;
                if (trimmed.StartsWith(";", StringComparison.Ordinal)) continue;
                int eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;
                string key = trimmed.Substring(0, eq).Trim();
                string val = trimmed.Substring(eq + 1).Trim();
                if (key.Length == 0 || val.Length == 0) continue;
                if (!IsValidQuotaKey(key)) continue;
                userQuotas.Add(key + "=" + val);
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("[Goose]\n");
            sb.Append("; Stock container quotas. Format: <Type>/<Subtype>=<value>\n");
            sb.Append(";   Suffixes: M=minimum (pull-only), L=limiter (push-only), no suffix=exact (pull/push), All=uncapped pull\n");
            sb.Append("; Examples:\n");
            sb.Append(";   Component/SteelPlate=100\n");
            sb.Append(";   Ingot/Iron=500M\n");
            sb.Append(";   Ore/Stone=1000L\n");
            sb.Append(";   Component/Construction=All\n");

            if (userQuotas.Count > 0) {
                sb.Append("\n");
                for (int i = 0; i < userQuotas.Count; i++) {
                    sb.Append(userQuotas[i]);
                    sb.Append("\n");
                }
            }

            sb.Append("\n; --- Observed items ---\n");

            if (_knownItems.Count > 0) {
                List<string> sortedKeys = new List<string>(_knownItems.Keys);
                sortedKeys.Sort(StringComparer.Ordinal);
                for (int i = 0; i < sortedKeys.Count; i++) {
                    sb.Append("; ");
                    sb.Append(sortedKeys[i]);
                    sb.Append("=100\n");
                }
            }

            string desired = sb.ToString();
            if (!string.Equals(current, desired, StringComparison.Ordinal)) {
                block.CustomData = desired;
            }
        }

        /// <summary>Returns true when <paramref name="key"/> matches the
        /// <c>[MyObjectBuilder_]Type/Subtype</c> shape used by stock quota entries.</summary>
        bool IsValidQuotaKey(string key) {
            int slash = key.IndexOf('/');
            if (slash <= 0 || slash >= key.Length - 1) return false;
            string typeHalf = key.Substring(0, slash);
            string subHalf = key.Substring(slash + 1);
            return IsIdentifier(typeHalf) && IsIdentifier(subHalf);
        }

        /// <summary>Returns true when <paramref name="s"/> is a non-empty C-style identifier.</summary>
        bool IsIdentifier(string s) {
            if (string.IsNullOrEmpty(s)) return false;
            char c = s[0];
            if (!(char.IsLetter(c) || c == '_')) return false;
            for (int i = 1; i < s.Length; i++) {
                c = s[i];
                if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            }
            return true;
        }

        /// <summary>Known component subtype IDs that route to <see cref="ItemCategory.Prototech"/>.</summary>
        static readonly HashSet<string> PrototechSubtypes = new HashSet<string> {
            "PrototechCapacitor", "PrototechCircuitry", "PrototechCoolingUnit",
            "PrototechFrame", "PrototechMachinery", "PrototechPanel",
            "PrototechPropulsionUnit", "PrototechScanner"
        };

        /// <summary>Maps an item type to its routing category, honoring user overrides first.</summary>
        /// <param name="type">Item type to classify.</param>
        /// <returns>Best-fit category; falls back to <see cref="ItemCategory.Misc"/> for unknown TypeIds.</returns>
        ItemCategory Classify(MyItemType type) {
            string fullId = type.TypeId + "/" + type.SubtypeId;
            ItemCategory ovr;
            if (_categoryOverrides.TryGetValue(fullId, out ovr)) return ovr;

            string typeId = type.TypeId;
            string subId = type.SubtypeId ?? "";

            if (typeId == "MyObjectBuilder_Ore") return ItemCategory.Ores;
            if (typeId == "MyObjectBuilder_Ingot") return ItemCategory.Ingots;
            if (typeId == "MyObjectBuilder_AmmoMagazine") return ItemCategory.Ammo;
            if (typeId == "MyObjectBuilder_Datapad") return ItemCategory.Misc;

            if (typeId == "MyObjectBuilder_Component") {
                if (PrototechSubtypes.Contains(subId)) return ItemCategory.Prototech;
                if (subId.StartsWith("Prototech", StringComparison.Ordinal)) return ItemCategory.Prototech;
                return ItemCategory.Components;
            }

            if (typeId == "MyObjectBuilder_PhysicalGunObject") {
                if (subId.IndexOf("Welder", StringComparison.OrdinalIgnoreCase) >= 0) return ItemCategory.Tools;
                if (subId.IndexOf("Grinder", StringComparison.OrdinalIgnoreCase) >= 0) return ItemCategory.Tools;
                if (subId.IndexOf("Drill", StringComparison.OrdinalIgnoreCase) >= 0) return ItemCategory.Tools;
                if (subId.IndexOf("HandDrill", StringComparison.OrdinalIgnoreCase) >= 0) return ItemCategory.Tools;
                if (subId.IndexOf("Pistol", StringComparison.OrdinalIgnoreCase) >= 0) return ItemCategory.Weapons;
                if (subId.IndexOf("Rifle", StringComparison.OrdinalIgnoreCase) >= 0) return ItemCategory.Weapons;
                if (subId.IndexOf("Launcher", StringComparison.OrdinalIgnoreCase) >= 0) return ItemCategory.Weapons;
                if (subId.IndexOf("FireArm", StringComparison.OrdinalIgnoreCase) >= 0) return ItemCategory.Weapons;
                if (subId.IndexOf("Goggles", StringComparison.OrdinalIgnoreCase) >= 0) return ItemCategory.Weapons;
                return ItemCategory.Weapons;
            }

            if (typeId == "MyObjectBuilder_OxygenContainerObject") return ItemCategory.Tools;
            if (typeId == "MyObjectBuilder_GasContainerObject") return ItemCategory.Tools;

            if (typeId == "MyObjectBuilder_ConsumableItem") {
                if (subId.StartsWith("Ingredient_", StringComparison.OrdinalIgnoreCase)
                    || subId.EndsWith("Ingredient", StringComparison.OrdinalIgnoreCase))
                    return ItemCategory.Ingredients;
                if (subId.StartsWith("Meal_", StringComparison.OrdinalIgnoreCase)
                    || subId.EndsWith("Meal", StringComparison.OrdinalIgnoreCase))
                    return ItemCategory.Meals;
                return ItemCategory.Consumables;
            }

            if (typeId == "MyObjectBuilder_PhysicalObject") return ItemCategory.Misc;

            LogWarningOnce("unkType:" + typeId, "[Goose] Unknown TypeId '" + typeId + "' classified as Misc");
            return ItemCategory.Misc;
        }

        /// <summary>
        /// Walks every managed inventory and refreshes <see cref="_itemTotals"/> and the
        /// observed-item catalog (via <see cref="Catalog_RecordItem"/>) used by stock templating.
        /// </summary>
        IEnumerator<YieldReason> StepScanInventories() {
            _itemTotals.Clear();
            int counter = 0;
            for (int b = 0; b < _allInventoryBlocks.Count; b++) {
                IMyTerminalBlock block = _allInventoryBlocks[b];
                if (!ValidateBlock(block)) continue;
                for (int invIdx = 0; invIdx < block.InventoryCount; invIdx++) {
                    IMyInventory inv = block.GetInventory(invIdx);
                    if (inv == null) continue;
                    _itemBuffer.Clear();
                    inv.GetItems(_itemBuffer);
                    for (int i = 0; i < _itemBuffer.Count; i++) {
                        MyInventoryItem item = _itemBuffer[i];
                        long current;
                        _itemTotals.TryGetValue(item.Type, out current);
                        _itemTotals[item.Type] = current + (long)item.Amount;
                        Catalog_RecordItem(item.Type);
                    }
                }
                counter++;
                if (counter % 10 == 0) yield return YieldReason.ChunkBoundary;
                if (BudgetExceeded()) yield return YieldReason.BudgetHit;
            }
        }
    }
}
