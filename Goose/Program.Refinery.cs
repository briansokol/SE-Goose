using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program
    {
        /// <summary>Refineries in scope, refreshed each rescan.</summary>
        private readonly List<Sandbox.ModAPI.Ingame.IMyRefinery> _refineries = new List<Sandbox.ModAPI.Ingame.IMyRefinery>();

        /// <summary>Scratch list for a refinery's active ore order; reused per refinery to avoid GC.</summary>
        private readonly List<string> _refineryOrderScratch = new List<string>();

        /// <summary>Vanilla ores in default refinery feed priority (highest first).</summary>
        internal static readonly string[] DefaultRefineryOres =
        {
            "Stone", "Platinum", "Uranium", "Gold", "Silver",
            "Magnesium", "Cobalt", "Silicon", "Nickel", "Iron"
        };

        /// <summary>Object-builder type id that identifies an ore item.</summary>
        private const string OreTypeId = "MyObjectBuilder_Ore";

        /// <summary>Determines whether the given subtype is a known vanilla ore.</summary>
        /// <param name="subtype">Item subtype id to test.</param>
        /// <returns><c>true</c> if it is one of <see cref="DefaultRefineryOres"/>.</returns>
        private static bool IsKnownOre(string subtype)
        {
            for (int i = 0; i < DefaultRefineryOres.Length; i++)
            {
                if (string.Equals(DefaultRefineryOres[i], subtype, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Fills <paramref name="result"/> with the active (uncommented) ore subtypes in the
        /// block's <c>[Goose]</c> section, in file order (priority). Unknown tokens and duplicates are dropped.</summary>
        /// <param name="customData">The block's CustomData text.</param>
        /// <param name="result">List to populate with ordered ore subtypes.</param>
        internal static void ParseRefineryOrder(string customData, List<string> result)
        {
            result.Clear();
            if (string.IsNullOrEmpty(customData))
            {
                return;
            }
            string[] lines = customData.Split('\n');
            bool inGoose = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimEnd('\r').Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }
                if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    inGoose = trimmed.Equals("[Goose]", StringComparison.Ordinal);
                    continue;
                }
                if (!inGoose || trimmed.StartsWith(";", StringComparison.Ordinal))
                {
                    continue;
                }
                if (!IsKnownOre(trimmed) || result.Contains(trimmed))
                {
                    continue;
                }
                result.Add(trimmed);
            }
        }

        /// <summary>Indicates whether the block needs the default ore-priority template injected.</summary>
        /// <param name="customData">The block's CustomData text.</param>
        /// <returns><c>true</c> when no <c>[Goose]</c> section exists yet.</returns>
        internal static bool NeedsRefineryTemplate(string customData)
        {
            if (string.IsNullOrEmpty(customData))
            {
                return true;
            }
            return customData.IndexOf("[Goose]", StringComparison.Ordinal) < 0;
        }

        /// <summary>Returns <paramref name="existing"/> with a fully-commented default ore-priority
        /// <c>[Goose]</c> section appended.</summary>
        /// <param name="existing">Existing CustomData to preserve ahead of the new section.</param>
        /// <returns>The combined CustomData text.</returns>
        internal static string BuildRefineryTemplate(string existing)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(existing))
            {
                sb.Append(existing);
                if (!existing.EndsWith("\n", StringComparison.Ordinal))
                {
                    sb.Append('\n');
                }
                sb.Append('\n');
            }
            sb.Append("[Goose]\n");
            sb.Append("; Refinery ore priority (top = highest). Uncomment ores to feed this refinery.\n");
            sb.Append("; Highest available ore fills first; when it is gone grid-wide, the next fills in.\n");
            sb.Append("; Commented-out ores are returned to storage. Leave all commented to ignore this refinery.\n");
            for (int i = 0; i < DefaultRefineryOres.Length; i++)
            {
                sb.Append("; ");
                sb.Append(DefaultRefineryOres[i]);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Determines whether Crane is live on the Goose-Crane bridge.</summary>
        /// <returns><c>true</c> when a linked Crane peer is present, so Goose stands down on refineries.</returns>
        private bool CraneDetected()
        {
            return _bridge != null && _bridge.Enabled && _bridge.PeerStatus.Linked;
        }

        /// <summary>Injects the ore-priority template, then (for activated refineries) disables the
        /// conveyor, evacuates unlisted ore, and tops up the input with priority ore. No-op when the
        /// feature is disabled or Crane is present.</summary>
        private IEnumerator<YieldReason> StepManageRefineries()
        {
            if (!_config.EnableRefineryManagement || CraneDetected())
            {
                yield break;
            }

            for (int b = 0; b < _refineries.Count; b++)
            {
                IMyRefinery r = _refineries[b];
                if (r == null || r.Closed)
                {
                    continue;
                }

                string cd = r.CustomData ?? string.Empty;
                if (NeedsRefineryTemplate(cd))
                {
                    cd = BuildRefineryTemplate(cd);
                    r.CustomData = cd;
                }

                ParseRefineryOrder(cd, _refineryOrderScratch);
                if (_refineryOrderScratch.Count == 0)
                {
                    continue;
                }

                if (r.UseConveyorSystem)
                {
                    r.UseConveyorSystem = false;
                }

                IMyInventory input = r.InputInventory;
                if (input == null)
                {
                    continue;
                }

                EvacuateUnlistedOre(input, _refineryOrderScratch);
                if (BudgetExceeded())
                {
                    yield return YieldReason.BudgetHit;
                }

                FeedRefinery(input, _refineryOrderScratch);
                if (BudgetExceeded())
                {
                    yield return YieldReason.BudgetHit;
                }

                yield return YieldReason.ChunkBoundary;
            }
        }

        /// <summary>Fills <paramref name="input"/> toward the configured percent of its max volume,
        /// feeding ores in priority order: each ore fills as much as it can before the next is tried.</summary>
        /// <param name="input">The refinery's input inventory.</param>
        /// <param name="order">Active ore subtypes in priority order.</param>
        private void FeedRefinery(IMyInventory input, List<string> order)
        {
            double maxVol = (double)input.MaxVolume;
            double target = maxVol * _config.RefineryInputFillPercent / 100.0;
            if (target <= 0.0)
            {
                return;
            }
            for (int o = 0; o < order.Count; o++)
            {
                if ((double)input.CurrentVolume >= target)
                {
                    return;
                }
                var type = MyItemType.MakeOre(order[o]);
                PullOreInto(input, type, target);
                if (BudgetExceeded())
                {
                    return;
                }
            }
        }

        /// <summary>Pulls the given ore from every in-scope inventory except refineries into
        /// <paramref name="input"/>, stopping when the input reaches <paramref name="target"/> volume
        /// or the grid runs out.</summary>
        /// <param name="input">Destination refinery input inventory.</param>
        /// <param name="type">Ore item type to pull.</param>
        /// <param name="target">Volume at which to stop filling.</param>
        private void PullOreInto(IMyInventory input, MyItemType type, double target)
        {
            for (int b = 0; b < _allInventoryBlocks.Count; b++)
            {
                if ((double)input.CurrentVolume >= target)
                {
                    return;
                }
                IMyTerminalBlock block = _allInventoryBlocks[b];
                if (block == null || block.Closed || block is IMyRefinery)
                {
                    continue;
                }
                IMyInventory src = block.GetInventory(0);
                if (src == null || src == input)
                {
                    continue;
                }
                bool canTransfer;
                try
                { canTransfer = src.CanTransferItemTo(input, type); }
                catch { continue; }
                if (!canTransfer)
                {
                    continue;
                }
                _itemBuffer.Clear();
                try
                { src.GetItems(_itemBuffer); }
                catch { continue; }
                for (int i = _itemBuffer.Count - 1; i >= 0; i--)
                {
                    MyInventoryItem item = _itemBuffer[i];
                    if (item.Type != type)
                    {
                        continue;
                    }
                    MyFixedPoint amt = item.Amount;
                    bool canAdd;
                    try
                    { canAdd = input.CanItemsBeAdded(amt, type); }
                    catch { canAdd = false; }
                    if (!canAdd)
                    {
                        break;
                    }
                    try
                    {
                        if (src.TransferItemTo(input, i, null, true, amt) && (double)input.CurrentVolume >= target)
                        {
                            return;
                        }
                    }
                    catch { }
                    if (BudgetExceeded())
                    {
                        return;
                    }
                }
                if (BudgetExceeded())
                {
                    return;
                }
            }
        }

        /// <summary>Moves any ore in <paramref name="input"/> whose subtype is not in <paramref name="order"/>
        /// back to <c>[Ores]</c> storage. Handles both off-list ores and ores the user just removed.</summary>
        /// <param name="input">The refinery's input inventory.</param>
        /// <param name="order">Active ore subtypes that should remain.</param>
        private void EvacuateUnlistedOre(IMyInventory input, List<string> order)
        {
            ContainerList routes;
            if (!_containersByCategory.TryGetValue(ItemCategory.Ores, out routes) || routes.Count == 0)
            {
                LogWarningOnce("norefore", "[Goose] No [Ores] container to return de-prioritized refinery ore.");
                return;
            }
            _itemBuffer.Clear();
            try
            { input.GetItems(_itemBuffer); }
            catch { return; }
            for (int i = _itemBuffer.Count - 1; i >= 0; i--)
            {
                MyInventoryItem item = _itemBuffer[i];
                if (!string.Equals(item.Type.TypeId, OreTypeId, StringComparison.Ordinal))
                {
                    continue;
                }
                if (order.Contains(item.Type.SubtypeId))
                {
                    continue;
                }
                for (int rIdx = 0; rIdx < routes.Count; rIdx++)
                {
                    ContainerEntry dst = routes[rIdx];
                    if (!ValidEntry(dst) || dst.Inventory == input)
                    {
                        continue;
                    }
                    if (!input.CanTransferItemTo(dst.Inventory, item.Type))
                    {
                        continue;
                    }
                    if (!dst.Inventory.CanItemsBeAdded(item.Amount, item.Type))
                    {
                        continue;
                    }
                    if (input.TransferItemTo(dst.Inventory, i, null, true, item.Amount))
                    {
                        break;
                    }
                }
                if (BudgetExceeded())
                {
                    return;
                }
            }
        }
    }
}
