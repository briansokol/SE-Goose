using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        /// <summary>Width of the fill bar drawn on <c>[GStatus]</c> rows.</summary>
        private const int StatusBarWidth = 10;

        /// <summary>Width of the category label column on <c>[GStatus]</c> rows.</summary>
        private const int StatusLabelWidth = 9;

        /// <summary>Reusable buffer for status/error rendering to avoid per-tick allocations.</summary>
        private readonly StringBuilder _statusBuffer = new StringBuilder(512);

        /// <summary>Categories with at least one item present on the grid, rebuilt each render cycle.</summary>
        private readonly HashSet<ItemCategory> _presentCategories = new HashSet<ItemCategory>();

        /// <summary>Refreshes coverage warnings, then redraws the <c>[GError]</c> and <c>[GStatus]</c> surfaces.</summary>
        private IEnumerator<YieldReason> StepRenderStatus()
        {
            BuildPresentCategories();
            DetectUncoveredCategories();

            if (_gerrorLcds.Count == 0 && _gstatusLcds.Count == 0)
            {
                yield return YieldReason.ChunkBoundary;
                yield break;
            }

            RenderGErrorStatus();
            RenderGStatusStatus();
            yield return YieldReason.ChunkBoundary;
        }

        /// <summary>Rebuilds <see cref="_presentCategories"/> from the most recent inventory scan.</summary>
        private void BuildPresentCategories()
        {
            _presentCategories.Clear();
            foreach (KeyValuePair<MyItemType, long> kv in _itemTotals)
            {
                if (kv.Value > 0)
                {
                    _presentCategories.Add(Classify(kv.Key));
                }
            }
        }

        /// <summary>Warns once per cycle for any category present on the grid that has no tagged container.</summary>
        private void DetectUncoveredCategories()
        {
            foreach (ItemCategory category in _presentCategories)
            {
                if (!CategoryHasContainers(category))
                {
                    LogWarningOnce("status:no-container:" + category,
                        $"[Goose] {category} present but no container tagged. Tag a container [{category}].");
                }
            }
        }

        /// <summary>True when at least one container is tagged for the given category.</summary>
        private bool CategoryHasContainers(ItemCategory category)
        {
            List<ContainerEntry> entries;
            return _containersByCategory.TryGetValue(category, out entries) && entries.Count > 0;
        }

        /// <summary>Writes the warning log to every <c>[GError]</c> surface.</summary>
        private void RenderGErrorStatus()
        {
            if (_gerrorLcds.Count == 0)
            {
                return;
            }

            _statusBuffer.Clear();
            _statusBuffer.Append("=== Goose Errors ===\n");
            if (_warnings.Count == 0)
            {
                _statusBuffer.Append("(no errors)\n");
            }
            else
            {
                foreach (KeyValuePair<string, int> kv in _warnings)
                {
                    _statusBuffer.Append(kv.Key);
                    if (kv.Value > 1)
                    {
                        _statusBuffer.Append(" (x").Append(kv.Value).Append(')');
                    }

                    _statusBuffer.Append('\n');
                }
            }

            WriteToSurfaces(_gerrorLcds, _statusBuffer.ToString());
        }

        /// <summary>Writes per-category fill levels to every <c>[GStatus]</c> surface.</summary>
        private void RenderGStatusStatus()
        {
            if (_gstatusLcds.Count == 0)
            {
                return;
            }

            _statusBuffer.Clear();
            _statusBuffer.Append("=== Goose Status ===\n");
            for (int c = 0; c <= (int)ItemCategory.Misc; c++)
            {
                var category = (ItemCategory)c;
                bool hasContainers = CategoryHasContainers(category);
                if (!hasContainers && !_presentCategories.Contains(category))
                {
                    continue;
                }

                _statusBuffer.Append(PadRight(AbbreviateCategory(category), StatusLabelWidth));
                if (hasContainers)
                {
                    int percent = ComputeCategoryFillPercent(category);
                    _statusBuffer.Append(RenderFillBar(percent, StatusBarWidth))
                        .Append("  ").Append(percent).Append("%\n");
                }
                else
                {
                    _statusBuffer.Append("NO CONTAINER\n");
                }
            }

            WriteToSurfaces(_gstatusLcds, _statusBuffer.ToString());
        }

        /// <summary>Sums a category's container volumes and returns the combined fill percentage.</summary>
        private int ComputeCategoryFillPercent(ItemCategory category)
        {
            List<ContainerEntry> entries;
            if (!_containersByCategory.TryGetValue(category, out entries))
            {
                return 0;
            }

            double used = 0.0;
            double max = 0.0;
            foreach (ContainerEntry entry in entries)
            {
                if (entry != null && entry.Inventory != null)
                {
                    used += (double)entry.Inventory.CurrentVolume;
                    max += (double)entry.Inventory.MaxVolume;
                }
            }

            return ComputeFillPercent(used, max);
        }

        /// <summary>Applies the standard text-surface settings and writes the given text to each surface.</summary>
        private static void WriteToSurfaces(List<IMyTextSurface> surfaces, string text)
        {
            for (int i = 0; i < surfaces.Count; i++)
            {
                IMyTextSurface surface = surfaces[i];
                if (surface != null)
                {
                    surface.ContentType = ContentType.TEXT_AND_IMAGE;
                    surface.Font = "Debug";
                    surface.WriteText(text, false);
                }
            }
        }

        /// <summary>Combined fill percentage (0-100) for a used/max volume pair; 0 when capacity is not positive.</summary>
        internal static int ComputeFillPercent(double used, double max)
        {
            if (max <= 0.0)
            {
                return 0;
            }

            int percent = (int)Math.Round(used / max * 100.0);
            if (percent < 0)
            {
                return 0;
            }
            if (percent > 100)
            {
                return 100;
            }

            return percent;
        }

        /// <summary>Renders a fixed-width fill bar, e.g. <c>[######----]</c> for 60% at width 10.</summary>
        internal static string RenderFillBar(int percent, int width)
        {
            int filled = (int)Math.Round(percent / 100.0 * width);
            if (filled < 0)
            {
                filled = 0;
            }
            if (filled > width)
            {
                filled = width;
            }

            return "[" + new string('#', filled) + new string('-', width - filled) + "]";
        }

        /// <summary>Returns a short fixed-width-friendly label for a category.</summary>
        internal static string AbbreviateCategory(ItemCategory category)
        {
            switch (category)
            {
                case ItemCategory.Components:
                    return "Comps";
                case ItemCategory.Prototech:
                    return "Proto";
                case ItemCategory.Consumables:
                    return "Consum";
                case ItemCategory.Ingredients:
                    return "Ingred";
                default:
                    return category.ToString();
            }
        }

        /// <summary>Right-pads a string to <paramref name="width"/> with spaces; truncates when longer.</summary>
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
    }
}
