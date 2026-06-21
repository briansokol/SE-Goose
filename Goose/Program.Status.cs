using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        /// <summary>Font used for all status/error surfaces; proportional, so columns are aligned via sprite pixel positions.</summary>
        private const string StatusFont = "Debug";

        /// <summary>Inter-row spacing multiplier applied on top of measured line height.</summary>
        private const float StatusLineSpacing = 1.3f;

        /// <summary>Lower clamp for the auto-fit font scale.</summary>
        private const float StatusMinScale = 0.4f;

        /// <summary>Upper clamp for the auto-fit font scale.</summary>
        private const float StatusMaxScale = 1.0f;

        /// <summary>Left edge of the fill bar, as a fraction of drawable width.</summary>
        private const float StatusBarXFraction = 0.34f;

        /// <summary>Fill bar width, as a fraction of drawable width.</summary>
        private const float StatusBarWidthFraction = 0.44f;

        /// <summary>Fill bar height, as a fraction of a row's text height.</summary>
        private const float StatusBarHeightFraction = 0.55f;

        /// <summary>Title text color.</summary>
        private static readonly Color StatusTitleColor = Color.White;

        /// <summary>Category label and percent text color.</summary>
        private static readonly Color StatusLabelColor = Color.White;

        /// <summary>Empty-track color behind a fill bar.</summary>
        private static readonly Color StatusBarTrackColor = new Color(40, 40, 40);

        /// <summary>Filled portion color of a fill bar.</summary>
        private static readonly Color StatusBarFillColor = new Color(60, 160, 220);

        /// <summary>Color for warnings and uncovered-category notices.</summary>
        private static readonly Color StatusWarningColor = Color.Orange;

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
                    string name = CategoryName(category);
                    LogWarningOnce("status:no-container:" + category,
                        $"{name} present but no container tagged. Tag a container with the {name} tag.");
                }
            }
        }

        /// <summary>True when at least one container is tagged for the given category.</summary>
        private bool CategoryHasContainers(ItemCategory category)
        {
            ContainerList entries;
            return _containersByCategory.TryGetValue(category, out entries) && entries.Count > 0;
        }

        /// <summary>Draws the warning log to every <c>[GError]</c> surface.</summary>
        private void RenderGErrorStatus()
        {
            for (int i = 0; i < _gerrorLcds.Count; i++)
            {
                IMyTextSurface surface = _gerrorLcds[i];
                if (surface != null)
                {
                    RenderGErrorSurface(surface);
                }
            }
        }

        /// <summary>Draws per-category fill levels to every <c>[GStatus]</c> surface.</summary>
        private void RenderGStatusStatus()
        {
            for (int i = 0; i < _gstatusLcds.Count; i++)
            {
                IMyTextSurface surface = _gstatusLcds[i];
                if (surface != null)
                {
                    RenderGStatusSurface(surface);
                }
            }
        }

        /// <summary>Renders one <c>[GError]</c> surface: a title plus one row per active warning, auto-scaled to fit.</summary>
        private void RenderGErrorSurface(IMyTextSurface surface)
        {
            var measurer = new SurfaceMeasurer(surface);
            DisplayViewport vp = SurfaceRenderer.CreateViewport(surface);

            int haltRows = ManagementSuspended ? 1 : 0;
            int rowCount = 1 + haltRows + Math.Max(1, _warnings.Count);
            float lineHeight = measurer.Measure("Ag", StatusFont, 1f).Height;
            float scale = LayoutScaler.FitScale(vp.Size.Y, rowCount, lineHeight, StatusLineSpacing, StatusMinScale, StatusMaxScale);
            float rowHeight = LayoutScaler.RowHeight(lineHeight, scale, StatusLineSpacing);

            var builder = new DisplayFrameBuilder(vp, measurer);
            builder.AddText("Goose Errors", vp.Size.X / 2f, 0f, DrawAlign.Center, StatusFont, scale, StatusTitleColor);

            int row = 1;
            if (ManagementSuspended)
            {
                builder.AddText(_haltMessage, 0f, ColumnLayout.RowY(0f, rowHeight, row), DrawAlign.Left, StatusFont, scale, StatusWarningColor);
                row++;
            }

            if (_warnings.Count == 0)
            {
                builder.AddText("(no errors)", 0f, ColumnLayout.RowY(0f, rowHeight, row), DrawAlign.Left, StatusFont, scale, StatusLabelColor);
            }
            else
            {
                foreach (KeyValuePair<string, int> kv in _warnings)
                {
                    string line = kv.Value > 1 ? $"{kv.Key} (x{kv.Value})" : kv.Key;
                    builder.AddText(line, 0f, ColumnLayout.RowY(0f, rowHeight, row), DrawAlign.Left, StatusFont, scale, StatusWarningColor);
                    row++;
                }
            }

            SurfaceRenderer.Render(surface, builder.Commands);
        }

        /// <summary>Renders one <c>[GStatus]</c> surface: a title plus one fill-bar row per visible category, auto-scaled to fit.</summary>
        private void RenderGStatusSurface(IMyTextSurface surface)
        {
            var measurer = new SurfaceMeasurer(surface);
            DisplayViewport vp = SurfaceRenderer.CreateViewport(surface);

            int rowCount = 1 + CountVisibleStatusRows();
            float lineHeight = measurer.Measure("Ag", StatusFont, 1f).Height;
            float scale = LayoutScaler.FitScale(vp.Size.Y, rowCount, lineHeight, StatusLineSpacing, StatusMinScale, StatusMaxScale);
            float rowHeight = LayoutScaler.RowHeight(lineHeight, scale, StatusLineSpacing);
            float textHeight = lineHeight * scale;

            float barX = vp.Size.X * StatusBarXFraction;
            float barWidth = vp.Size.X * StatusBarWidthFraction;
            float barHeight = textHeight * StatusBarHeightFraction;
            float percentX = vp.Size.X;

            var builder = new DisplayFrameBuilder(vp, measurer);
            builder.AddText("Goose Status", vp.Size.X / 2f, 0f, DrawAlign.Center, StatusFont, scale, StatusTitleColor);

            int row = 1;
            for (int c = 0; c <= (int)ItemCategory.Misc; c++)
            {
                var category = (ItemCategory)c;
                bool hasContainers = CategoryHasContainers(category);
                if (!hasContainers && !_presentCategories.Contains(category))
                {
                    continue;
                }

                float y = ColumnLayout.RowY(0f, rowHeight, row);
                builder.AddText(AbbreviateCategory(category), 0f, y, DrawAlign.Left, StatusFont, scale, StatusLabelColor);
                if (hasContainers)
                {
                    int percent = ComputeCategoryFillPercent(category);
                    float barY = y + (textHeight - barHeight) / 2f;
                    builder.AddBar(barX, barY, barWidth, barHeight, percent, StatusBarTrackColor, StatusBarFillColor);
                    builder.AddText($"{percent}%", percentX, y, DrawAlign.Right, StatusFont, scale, StatusLabelColor);
                }
                else
                {
                    builder.AddText("NO CONTAINER", barX, y, DrawAlign.Left, StatusFont, scale, StatusWarningColor);
                }

                row++;
            }

            SurfaceRenderer.Render(surface, builder.Commands);
        }

        /// <summary>Counts categories that should appear on the status screen: any with containers tagged or items present.</summary>
        private int CountVisibleStatusRows()
        {
            int count = 0;
            for (int c = 0; c <= (int)ItemCategory.Misc; c++)
            {
                var category = (ItemCategory)c;
                if (CategoryHasContainers(category) || _presentCategories.Contains(category))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Sums a category's container volumes and returns the combined fill percentage.</summary>
        private int ComputeCategoryFillPercent(ItemCategory category)
        {
            ContainerList entries;
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

        /// <summary>Returns a short label for a category to keep status rows compact.</summary>
        internal static string AbbreviateCategory(ItemCategory category)
        {
            switch (category)
            {
                case ItemCategory.Ingots:
                    return "Ingots";
                case ItemCategory.Ores:
                    return "Ores";
                case ItemCategory.Components:
                    return "Comps";
                case ItemCategory.Prototech:
                    return "Proto";
                case ItemCategory.Tools:
                    return "Tools";
                case ItemCategory.Weapons:
                    return "Weapons";
                case ItemCategory.Ammo:
                    return "Ammo";
                case ItemCategory.Consumables:
                    return "Consum";
                case ItemCategory.Ingredients:
                    return "Ingred";
                case ItemCategory.Meals:
                    return "Meals";
                case ItemCategory.Seeds:
                    return "Seeds";
                default:
                    return "Misc";
            }
        }
    }
}
