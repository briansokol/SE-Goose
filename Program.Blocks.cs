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
        public enum ItemCategory {
            Ingots, Ores, Components, Prototech, Tools, Weapons,
            Ammo, Consumables, Ingredients, Meals, Misc
        }

        static readonly string[] CategoryTags = {
            "Ingots", "Ores", "Components", "Prototech", "Tools", "Weapons",
            "Ammo", "Consumables", "Ingredients", "Meals", "Misc"
        };

        public class ContainerEntry {
            public IMyTerminalBlock Block;
            public IMyInventory Inventory;
            public int Priority = 100;
            public List<ItemCategory> Categories = new List<ItemCategory>();
            public bool IsStock;
            public Dictionary<MyItemType, StockQuota> Quotas;       // null unless IsStock
        }

        Dictionary<ItemCategory, List<ContainerEntry>> _containersByCategory =
            new Dictionary<ItemCategory, List<ContainerEntry>>();
        List<ContainerEntry> _stockContainers = new List<ContainerEntry>();
        Dictionary<IMyTerminalBlock, ContainerEntry> _entryByBlock =
            new Dictionary<IMyTerminalBlock, ContainerEntry>();
        Dictionary<string, ItemCategory> _categoryOverrides = new Dictionary<string, ItemCategory>();
        Dictionary<string, MyItemType> _knownSubtypes = new Dictionary<string, MyItemType>();
        Dictionary<MyItemType, long> _itemTotals = new Dictionary<MyItemType, long>();
        List<MyInventoryItem> _itemBuffer = new List<MyInventoryItem>();

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

        bool NameHasTag(string name, string tag) {
            return !string.IsNullOrEmpty(name) && name.IndexOf(tag, StringComparison.Ordinal) >= 0;
        }

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

                // Category tags
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

                // Stock parsing
                if (entry.IsStock) {
                    entry.Quotas = new Dictionary<MyItemType, StockQuota>();
                    ParseStockQuotas(block, entry);
                    _stockContainers.Add(entry);
                }

                _entryByBlock[block] = entry;

                counter++;
                if (counter % 25 == 0) yield return YieldReason.ChunkBoundary;
                if (BudgetExceeded()) yield return YieldReason.BudgetHit;
            }

            // Sort each category bucket by priority ascending (lower = higher priority)
            foreach (var kv in _containersByCategory) {
                kv.Value.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            }
            _stockContainers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

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
                } else {
                    // Defer: item not yet seen, or malformed.
                    LogWarningOnce("stockq:" + block.EntityId + ":" + key,
                        "[Goose] Stock quota '" + key + "' on '" + block.CustomName + "' deferred (item unknown or malformed)");
                }
            }
        }

        static readonly HashSet<string> PrototechSubtypes = new HashSet<string> {
            "PrototechCapacitor", "PrototechCircuitry", "PrototechCoolingUnit",
            "PrototechFrame", "PrototechMachinery", "PrototechPanel",
            "PrototechPropulsionUnit", "PrototechScanner"
        };

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
                return ItemCategory.Weapons;     // safer default; user can override
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
                        // Track subtype for stock-quota reverse lookup.
                        if (!string.IsNullOrEmpty(item.Type.SubtypeId)
                            && !_knownSubtypes.ContainsKey(item.Type.SubtypeId)) {
                            _knownSubtypes[item.Type.SubtypeId] = item.Type;
                        }
                    }
                }
                counter++;
                if (counter % 10 == 0) yield return YieldReason.ChunkBoundary;
                if (BudgetExceeded()) yield return YieldReason.BudgetHit;
            }
        }
    }
}
