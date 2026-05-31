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
        /// <summary>High-level item buckets used to route inventory between containers.</summary>
        public enum ItemCategory
        {
            Ingots, Ores, Components, Prototech, Tools, Weapons,
            Ammo, Consumables, Ingredients, Meals, Seeds, Misc
        }

        /// <summary>Bare tag tokens (no brackets) recognized in container names; index aligns with <see cref="ItemCategory"/>.</summary>
        private static readonly string[] CategoryTags = {
            "Ingots", "Ores", "Components", "Prototech", "Tools", "Weapons",
            "Ammo", "Consumables", "Ingredients", "Meals", "Seeds", "Misc"
        };

        /// <summary>Cached metadata for a single managed inventory block.</summary>
        public class ContainerEntry
        {
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

            /// <summary>Detected consumer class for the balancer. <see cref="ConsumerKind.None"/> for non-consumers and <c>[NoBalance]</c>-tagged blocks.</summary>
            public ConsumerKind ConsumerKind = ConsumerKind.None;

            /// <summary>Cached list of <see cref="MyItemType"/> ammo magazines this block accepts; <c>null</c> unless <see cref="ConsumerKind"/> is <see cref="ConsumerKind.Weapon"/>.</summary>
            public List<MyItemType> AcceptedAmmo;

            /// <summary>Per-block unit-count override parsed from the <c>[Balance=N]</c> name tag; <c>-1</c> means "no tag, use the class percent target." Only meaningful when <see cref="ConsumerKind"/> is non-None.</summary>
            public long BalanceTagCount = -1;
        }

        /// <summary>Routing buckets keyed by category, sorted by ascending priority.</summary>
        private readonly Dictionary<ItemCategory, List<ContainerEntry>> _containersByCategory =
            new Dictionary<ItemCategory, List<ContainerEntry>>();

        /// <summary>All <c>[Stock]</c>-tagged containers, sorted by ascending priority.</summary>
        private readonly List<ContainerEntry> _stockContainers = new List<ContainerEntry>();

        /// <summary>Reverse lookup from a block to its cached <see cref="ContainerEntry"/>.</summary>
        private readonly Dictionary<IMyTerminalBlock, ContainerEntry> _entryByBlock =
            new Dictionary<IMyTerminalBlock, ContainerEntry>();

        /// <summary>Manual classification overrides keyed by <c>TypeId/SubtypeId</c>.</summary>
        private readonly Dictionary<string, ItemCategory> _categoryOverrides = new Dictionary<string, ItemCategory>();

        /// <summary>Last <see cref="_catalogVersion"/> at which each stock container's CustomData
        /// template was rendered. Survives <see cref="StepCategorizeContainers"/> rebuilds so we
        /// only re-render when the catalog has actually grown since the previous render.</summary>
        private readonly Dictionary<IMyTerminalBlock, int> _stockTemplateVersion = new Dictionary<IMyTerminalBlock, int>();

        /// <summary>Total quantity of each item type observed during the most recent scan.</summary>
        private readonly Dictionary<MyItemType, long> _itemTotals = new Dictionary<MyItemType, long>();

        /// <summary>Scratch buffer reused by inventory enumeration to avoid per-call allocations.</summary>
        private readonly List<MyInventoryItem> _itemBuffer = new List<MyInventoryItem>();

        /// <summary>Reused per-type quantity snapshot of a single stock container, built once per container in <see cref="StepFulfillStockQuotas"/> to avoid re-scanning the inventory for every quota.</summary>
        private readonly Dictionary<MyItemType, long> _quotaSnapshot = new Dictionary<MyItemType, long>();

        /// <summary>Reused active-quota map for <see cref="SyncStockTemplate"/>; key = quota key, value = full <c>key=value</c> line.</summary>
        private readonly Dictionary<string, string> _stockActiveQuotas = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Reused builder for the <c>[Goose]</c> stock-template body.</summary>
        private readonly StringBuilder _stockTemplateSB = new StringBuilder(1024);

        /// <summary>Reused merged-key set (catalog ∪ active) for the stock template.</summary>
        private readonly HashSet<string> _stockMergedKeys = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Reused sorted-key list for the stock template render order.</summary>
        private readonly List<string> _stockSortedKeys = new List<string>();

        /// <summary>Extracts the priority value from a <c>[P:&lt;n&gt;]</c> tag in a block name.</summary>
        /// <returns>The parsed priority, or 100 when no tag is present.</returns>
        internal static int ParsePriorityFromName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return 100;
            }

            int idx = name.IndexOf("[P:", StringComparison.Ordinal);
            if (idx < 0)
            {
                return 100;
            }

            int end = name.IndexOf(']', idx + 3);
            if (end < 0)
            {
                return 100;
            }

            string raw = name.Substring(idx + 3, end - idx - 3);
            int p;
            if (int.TryParse(raw, out p))
            {
                return p;
            }

            return 100;
        }


        /// <summary>Parses an optional <c>[Balance=N]</c> name-tag into a non-negative unit count. Returns <c>-1</c> when the tag is absent or malformed; the balancer treats the absence sentinel as "use the class percent target instead."</summary>
        /// <param name="name">Block's CustomName.</param>
        /// <returns>Parsed non-negative count, or <c>-1</c> if no tag is present (or it failed to parse).</returns>
        internal static long ParseBalanceTagCount(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return -1;
            }

            int idx = name.IndexOf("[Balance=", StringComparison.Ordinal);
            if (idx < 0)
            {
                return -1;
            }

            int end = name.IndexOf(']', idx + 9);
            if (end < 0)
            {
                return -1;
            }

            string raw = name.Substring(idx + 9, end - idx - 9).Trim();
            long v;
            if (!long.TryParse(raw, out v))
            {
                return -1;
            }

            if (v < 0)
            {
                return -1;
            }

            return v;
        }

        /// <summary>
        /// Rebuilds <see cref="_containersByCategory"/>, <see cref="_stockContainers"/>, and
        /// <see cref="_entryByBlock"/> from name tags and CustomData on every managed block.
        /// </summary>
        private IEnumerator<YieldReason> StepCategorizeContainers()
        {
            foreach (KeyValuePair<ItemCategory, List<ContainerEntry>> kv in _containersByCategory)
            {
                kv.Value.Clear();
            }

            _stockContainers.Clear();
            _entryByBlock.Clear();

            int counter = 0;
            for (int b = 0; b < _allInventoryBlocks.Count; b++)
            {
                IMyTerminalBlock block = _allInventoryBlocks[b];
                if (!ValidateBlock(block))
                {
                    continue;
                }

                // The group (when configured) limits valid managed destinations only. Out-of-group
                // blocks get a plain entry (no categories, not stock) so they remain grid-wide sources
                // that are drained into in-group targets, but are never written into.
                bool isTarget = IsInGroup(block);

                var entry = new ContainerEntry
                {
                    Block = block,
                    Inventory = block.GetInventory(0),
                    Priority = ParsePriorityFromName(block.CustomName),
                    IsStock = isTarget && BlockNameTags.NameHasTag(block.CustomName, "[Stock]")
                };

                if (isTarget)
                {
                    for (int c = 0; c < CategoryTags.Length; c++)
                    {
                        if (BlockNameTags.NameHasTag(block.CustomName, CategoryTags[c]))
                        {
                            var cat = (ItemCategory)c;
                            entry.Categories.Add(cat);
                            List<ContainerEntry> bucket;
                            if (!_containersByCategory.TryGetValue(cat, out bucket))
                            {
                                bucket = new List<ContainerEntry>();
                                _containersByCategory[cat] = bucket;
                            }
                            bucket.Add(entry);
                        }
                    }
                }

                if (entry.IsStock)
                {
                    int lastRenderedVersion;
                    if (!_stockTemplateVersion.TryGetValue(block, out lastRenderedVersion) || lastRenderedVersion != _catalogVersion)
                    {
                        SyncStockTemplate(block);
                        _stockTemplateVersion[block] = _catalogVersion;
                    }
                    entry.Quotas = new Dictionary<MyItemType, StockQuota>();
                    ParseStockQuotas(block, entry);
                    ParseNameTagQuotas(entry);
                    _stockContainers.Add(entry);
                }
                else
                {
                    _stockTemplateVersion.Remove(block);
                }

                _entryByBlock[block] = entry;

                counter++;
                if (counter % 25 == 0)
                {
                    yield return YieldReason.ChunkBoundary;
                }

                if (BudgetExceeded())
                {
                    yield return YieldReason.BudgetHit;
                }
            }

            foreach (KeyValuePair<ItemCategory, List<ContainerEntry>> kv in _containersByCategory)
            {
                kv.Value.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            }
            _stockContainers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>Returns true when <paramref name="token"/> contains both a slash and a colon —
        /// the minimum shape for a name-tag quota override (e.g. <c>Ingot/Iron:100</c>). Tokens
        /// without both characters belong to other tag families (<c>[Stock]</c>, <c>[P:NN]</c>,
        /// bare category tags) and are skipped silently.</summary>
        internal static bool LooksLikeNameTagQuota(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            return token.IndexOf('/') >= 0 && token.IndexOf(':') >= 0;
        }

        /// <summary>Parses a single bracketed token shaped like <c>Type/Subtype:Value</c> into
        /// its constituent shape strings and quota value. Whitespace around the colon and
        /// inside the token is tolerated. Pure helper: no logging, no
        /// <see cref="MyItemType.Parse"/> calls, no exceptions escape.</summary>
        /// <param name="token">Token contents without the surrounding brackets.</param>
        /// <param name="typeIdWithPrefix">Resolved type id (with <c>MyObjectBuilder_</c> prefix) on success.</param>
        /// <param name="subtypeId">Resolved subtype on success.</param>
        /// <param name="amount">Parsed amount (0 when <paramref name="mode"/> is <see cref="QuotaMode.All"/>).</param>
        /// <param name="mode">Parsed quota mode.</param>
        /// <returns><c>true</c> when the token is a syntactically valid quota override.</returns>
        internal static bool TryParseNameTagQuotaShape(string token, out string typeIdWithPrefix, out string subtypeId, out long amount, out QuotaMode mode)
        {
            typeIdWithPrefix = null;
            subtypeId = null;
            amount = 0;
            mode = QuotaMode.Exact;
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            int colon = token.IndexOf(':');
            if (colon <= 0 || colon >= token.Length - 1)
            {
                return false;
            }

            string keyPart = token.Substring(0, colon).Trim();
            string valuePart = token.Substring(colon + 1).Trim();
            if (keyPart.Length == 0 || valuePart.Length == 0)
            {
                return false;
            }

            if (!TryParseQuotaKeyShape(keyPart, out typeIdWithPrefix, out subtypeId))
            {
                return false;
            }

            return TryParseQuotaValue(valuePart, out amount, out mode);
        }

        /// <summary>Walks <paramref name="name"/> and merges every parseable name-tag quota
        /// into <paramref name="destination"/>, overwriting on duplicate keys (last-write-wins).
        /// Returns the list of quota-shaped tokens that failed to parse, or <c>null</c> when
        /// none were malformed. Caller supplies <paramref name="typeResolver"/> so production
        /// can use <see cref="MyItemType.Parse"/> while tests substitute factory methods.</summary>
        /// <param name="name">Block display name to scan for <c>[Type/Subtype:Value]</c> tokens.</param>
        /// <param name="destination">Quota dictionary that receives parsed entries.</param>
        /// <param name="typeResolver">Maps a fully-qualified <c>Type/Subtype</c> string to a
        /// <see cref="MyItemType"/>, returning <c>null</c> on resolution failure.</param>
        /// <returns>List of malformed or unresolvable token strings, or <c>null</c> when all tokens parsed cleanly.</returns>
        internal static List<string> ExtractNameTagQuotas(
            string name,
            Dictionary<MyItemType, StockQuota> destination,
            Func<string, MyItemType?> typeResolver)
        {
            if (string.IsNullOrEmpty(name) || destination == null || typeResolver == null)
            {
                return null;
            }

            List<string> malformed = null;
            int pos = 0;
            while (pos < name.Length)
            {
                int open = name.IndexOf('[', pos);
                if (open < 0)
                {
                    break;
                }

                int close = name.IndexOf(']', open + 1);
                if (close < 0)
                {
                    break;
                }

                string token = name.Substring(open + 1, close - open - 1);
                pos = close + 1;
                if (!LooksLikeNameTagQuota(token))
                {
                    continue;
                }

                string typeIdWithPrefix, subtypeId;
                long amount;
                QuotaMode mode;
                if (!TryParseNameTagQuotaShape(token, out typeIdWithPrefix, out subtypeId, out amount, out mode))
                {
                    if (malformed == null)
                    {
                        malformed = new List<string>();
                    }

                    malformed.Add(token);
                    continue;
                }
                MyItemType? resolved = typeResolver(typeIdWithPrefix + "/" + subtypeId);
                if (!resolved.HasValue)
                {
                    if (malformed == null)
                    {
                        malformed = new List<string>();
                    }

                    malformed.Add(token);
                    continue;
                }
                destination[resolved.Value] = new StockQuota { Amount = amount, Mode = mode };
            }
            return malformed;
        }

        /// <summary>Merges name-tag quota overrides from a stock container's display name into
        /// its <see cref="ContainerEntry.Quotas"/>, overwriting any matching CustomData entry.
        /// Logs a one-shot warning for each malformed quota-shaped token.</summary>
        /// <param name="entry">Stock-tagged container entry whose name should be scanned.</param>
        private void ParseNameTagQuotas(ContainerEntry entry)
        {
            if (entry == null || entry.Block == null || entry.Quotas == null)
            {
                return;
            }

            List<string> malformed = ExtractNameTagQuotas(entry.Block.CustomName, entry.Quotas, ResolveItemTypeViaParse);
            if (malformed == null)
            {
                return;
            }

            for (int i = 0; i < malformed.Count; i++)
            {
                string token = malformed[i];
                LogWarningOnce("nametag:parse:" + entry.Block.CustomName + ":" + token,
                    "[Goose] Stock container '" + entry.Block.CustomName + "' name tag '[" + token + "]' did not parse as a quota override. Skipped.");
            }
        }

        /// <summary>Reads <c>[Goose]</c> CustomData entries on a stock container and populates
        /// <see cref="ContainerEntry.Quotas"/> with the parsed quotas.</summary>
        /// <param name="block">Stock-tagged terminal block whose CustomData holds the quotas.</param>
        /// <param name="entry">Owning entry to receive the parsed quota dictionary.</param>
        private void ParseStockQuotas(IMyTerminalBlock block, ContainerEntry entry)
        {
            MyIniParseResult res;
            if (!_ini.TryParse(block.CustomData, out res))
            {
                LogWarning("[Goose] Stock CustomData parse failed on '" + block.CustomName + "': " + res.ToString());
                return;
            }
            var keys = new List<MyIniKey>();
            _ini.GetKeys("Goose", keys);
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i].Name;
                string raw = _ini.Get(keys[i]).ToString();
                MyItemType type;
                StockQuota quota;
                if (TryReadStockQuota(key, raw, out type, out quota))
                {
                    entry.Quotas[type] = quota;
                }
            }
        }


        /// <summary>Refreshes the canonical Goose CustomData document on a stock container,
        /// preserving user-active quota lines and merging the live observed-item catalog.
        /// Writes only when the rendered document differs from the current CustomData.</summary>
        /// <param name="block">Stock-tagged terminal block whose CustomData to sync.</param>
        private void SyncStockTemplate(IMyTerminalBlock block)
        {
            string current = block.CustomData ?? string.Empty;

            _stockActiveQuotas.Clear();
            string[] lines = current.Split('\n');
            bool inGooseSection = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimEnd('\r').Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    inGooseSection = trimmed.Equals("[Goose]", StringComparison.Ordinal);
                    continue;
                }
                if (!inGooseSection)
                {
                    continue;
                }

                if (trimmed.StartsWith(";", StringComparison.Ordinal))
                {
                    continue;
                }

                int eq = trimmed.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = trimmed.Substring(0, eq).Trim();
                string val = trimmed.Substring(eq + 1).Trim();
                if (key.Length == 0 || val.Length == 0)
                {
                    continue;
                }

                if (!IsValidQuotaKey(key))
                {
                    continue;
                }

                _stockActiveQuotas[key] = key + "=" + val;
            }

            StringBuilder sb = _stockTemplateSB;
            sb.Length = 0;
            sb.Append("[Goose]\n");
            sb.Append(";Stock container quotas.\n");
            sb.Append(";Uncomment a line to enable management of an item (remove the ; at the start of the line)\n");
            sb.Append(";Format: <Type>/<Subtype>=<value>\n");
            sb.Append(";Suffixes:\n");
            sb.Append(";  M=minimum (pull-only)\n");
            sb.Append(";  L=limiter (push-only)\n");
            sb.Append(";  no suffix=exact (pull/push)\n");
            sb.Append(";  All=uncapped pull\n\n");
            sb.Append(";Examples:\n");
            sb.Append(";  Component/SteelPlate=100\n");
            sb.Append(";  Ingot/Iron=500M\n");
            sb.Append(";  Ore/Stone=1000L\n");
            sb.Append(";  Component/Construction=All\n");

            sb.Append("\n; --- Manage Items Below ---\n");

            _stockMergedKeys.Clear();
            foreach (string k in _knownItems.Keys)
            {
                _stockMergedKeys.Add(k);
            }
            foreach (string k in _stockActiveQuotas.Keys)
            {
                _stockMergedKeys.Add(k);
            }
            if (_stockMergedKeys.Count > 0)
            {
                _stockSortedKeys.Clear();
                foreach (string k in _stockMergedKeys)
                {
                    _stockSortedKeys.Add(k);
                }

                _stockSortedKeys.Sort(StringComparer.Ordinal);
                for (int i = 0; i < _stockSortedKeys.Count; i++)
                {
                    string mergedKey = _stockSortedKeys[i];
                    string activeLine;
                    if (_stockActiveQuotas.TryGetValue(mergedKey, out activeLine))
                    {
                        sb.Append(activeLine);
                        sb.Append("\n");
                    }
                    else
                    {
                        sb.Append("; ");
                        sb.Append(mergedKey);
                        sb.Append("=0\n");
                    }
                }
            }

            string desired = sb.ToString();
            if (!string.Equals(current, desired, StringComparison.Ordinal))
            {
                block.CustomData = desired;
            }
        }

        /// <summary>Returns true when <paramref name="key"/> matches the
        /// <c>[MyObjectBuilder_]Type/Subtype</c> shape used by stock quota entries.</summary>
        private bool IsValidQuotaKey(string key)
        {
            int slash = key.IndexOf('/');
            if (slash <= 0 || slash >= key.Length - 1)
            {
                return false;
            }

            string typeHalf = key.Substring(0, slash);
            string subHalf = key.Substring(slash + 1);
            return IsIdentifier(typeHalf) && IsIdentifier(subHalf);
        }

        /// <summary>Returns true when <paramref name="s"/> is a non-empty C-style identifier.</summary>
        internal static bool IsIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }

            char c = s[0];
            if (!(char.IsLetter(c) || c == '_'))
            {
                return false;
            }

            for (int i = 1; i < s.Length; i++)
            {
                c = s[i];
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Known component subtype IDs that route to <see cref="ItemCategory.Prototech"/>.</summary>
        private static readonly HashSet<string> PrototechSubtypes = new HashSet<string> {
            "PrototechCapacitor", "PrototechCircuitry", "PrototechCoolingUnit",
            "PrototechFrame", "PrototechMachinery", "PrototechPanel",
            "PrototechPropulsionUnit", "PrototechScanner"
        };

        /// <summary>Maps an item type to its routing category, honoring user overrides first.</summary>
        /// <param name="type">Item type to classify.</param>
        /// <returns>Best-fit category; falls back to <see cref="ItemCategory.Misc"/> for unknown TypeIds.</returns>
        internal ItemCategory Classify(MyItemType type)
        {
            string typeId = type.TypeId;
            string subId = type.SubtypeId ?? "";
            string fullId = typeId + "/" + subId;
            ItemCategory ovr;
            if (_categoryOverrides.TryGetValue(fullId, out ovr))
            {
                return ovr;
            }

            ItemCategory result = ClassifyByTypeId(typeId, subId);
            if (result == ItemCategory.Misc
                && typeId != "MyObjectBuilder_Datapad"
                && typeId != "MyObjectBuilder_PhysicalObject")
            {
                LogWarningOnce("unkType:" + typeId, "[Goose] Unknown TypeId '" + typeId + "' classified as Misc");
            }
            return result;
        }

        /// <summary>Pure rule-table lookup for an item's routing category, ignoring user overrides.</summary>
        /// <param name="typeId">Item TypeId (e.g., <c>MyObjectBuilder_Ore</c>).</param>
        /// <param name="subId">Item SubtypeId (may be empty but not null).</param>
        /// <returns>Category derived from the built-in rules; <see cref="ItemCategory.Misc"/> for unknown TypeIds.</returns>
        internal static ItemCategory ClassifyByTypeId(string typeId, string subId)
        {
            if (typeId == "MyObjectBuilder_Ore")
            {
                return ItemCategory.Ores;
            }

            if (typeId == "MyObjectBuilder_Ingot")
            {
                return ItemCategory.Ingots;
            }

            if (typeId == "MyObjectBuilder_AmmoMagazine")
            {
                return ItemCategory.Ammo;
            }

            if (typeId == "MyObjectBuilder_Datapad")
            {
                return ItemCategory.Misc;
            }

            if (typeId == "MyObjectBuilder_SeedItem")
            {
                return ItemCategory.Seeds;
            }

            if (typeId == "MyObjectBuilder_Component")
            {
                if (PrototechSubtypes.Contains(subId))
                {
                    return ItemCategory.Prototech;
                }

                if (subId.StartsWith("Prototech", StringComparison.Ordinal))
                {
                    return ItemCategory.Prototech;
                }

                return ItemCategory.Components;
            }

            if (typeId == "MyObjectBuilder_PhysicalGunObject")
            {
                if (subId.IndexOf("Welder", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ItemCategory.Tools;
                }

                if (subId.IndexOf("Grinder", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ItemCategory.Tools;
                }

                if (subId.IndexOf("Drill", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ItemCategory.Tools;
                }

                if (subId.IndexOf("HandDrill", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ItemCategory.Tools;
                }

                if (subId.IndexOf("Pistol", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ItemCategory.Weapons;
                }

                if (subId.IndexOf("Rifle", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ItemCategory.Weapons;
                }

                if (subId.IndexOf("Launcher", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ItemCategory.Weapons;
                }

                if (subId.IndexOf("FireArm", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ItemCategory.Weapons;
                }

                if (subId.IndexOf("Goggles", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ItemCategory.Weapons;
                }

                return ItemCategory.Weapons;
            }

            if (typeId == "MyObjectBuilder_OxygenContainerObject")
            {
                return ItemCategory.Tools;
            }

            if (typeId == "MyObjectBuilder_GasContainerObject")
            {
                return ItemCategory.Tools;
            }

            if (typeId == "MyObjectBuilder_ConsumableItem")
            {
                if (subId.StartsWith("Ingredient_", StringComparison.OrdinalIgnoreCase)
                    || subId.EndsWith("Ingredient", StringComparison.OrdinalIgnoreCase))
                {
                    return ItemCategory.Ingredients;
                }

                if (subId.StartsWith("Meal_", StringComparison.OrdinalIgnoreCase)
                    || subId.EndsWith("Meal", StringComparison.OrdinalIgnoreCase))
                {
                    return ItemCategory.Meals;
                }

                return ItemCategory.Consumables;
            }

            if (typeId == "MyObjectBuilder_PhysicalObject")
            {
                return ItemCategory.Misc;
            }

            return ItemCategory.Misc;
        }

        /// <summary>
        /// Walks every managed inventory and refreshes <see cref="_itemTotals"/> and the
        /// observed-item catalog (via <see cref="Catalog_RecordItem"/>) used by stock templating.
        /// </summary>
        private IEnumerator<YieldReason> StepScanInventories()
        {
            _itemTotals.Clear();
            _gridAmmoVolume = 0f;
            int counter = 0;
            for (int b = 0; b < _allInventoryBlocks.Count; b++)
            {
                IMyTerminalBlock block = _allInventoryBlocks[b];
                if (!ValidateBlock(block))
                {
                    continue;
                }

                for (int invIdx = 0; invIdx < block.InventoryCount; invIdx++)
                {
                    IMyInventory inv = block.GetInventory(invIdx);
                    if (inv == null)
                    {
                        continue;
                    }

                    _itemBuffer.Clear();
                    inv.GetItems(_itemBuffer);
                    for (int i = 0; i < _itemBuffer.Count; i++)
                    {
                        MyInventoryItem item = _itemBuffer[i];
                        long current;
                        _itemTotals.TryGetValue(item.Type, out current);
                        _itemTotals[item.Type] = current + (long)item.Amount;
                        if (Catalog_RecordItem(item.Type) && _bridge != null)
                        {
                            _bridge.AnnounceCatalogAdd(Catalog_BuildKey(item.Type));
                        }
                        if (item.Type.TypeId == "MyObjectBuilder_AmmoMagazine")
                        {
                            float volPerUnit;
                            if (_balanceVolumeCache.TryGetValue(item.Type, out volPerUnit) && volPerUnit > 0f)
                            {
                                _gridAmmoVolume += (float)item.Amount * volPerUnit;
                            }
                        }
                    }
                }
                counter++;
                if (counter % 10 == 0)
                {
                    yield return YieldReason.ChunkBoundary;
                }

                if (BudgetExceeded())
                {
                    yield return YieldReason.BudgetHit;
                }
            }
        }
    }
}
