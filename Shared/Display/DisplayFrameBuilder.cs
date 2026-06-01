using System.Collections.Generic;
using VRageMath;

namespace IngameScript
{
    /// <summary>Accumulates positioned <see cref="DrawCommand"/>s in a viewport's pixel space. Pure: no SE types; uses an injected <see cref="IStringMeasurer"/> for alignment so it is fully unit-testable.</summary>
    public class DisplayFrameBuilder
    {
        private readonly List<DrawCommand> _commands = new List<DrawCommand>();
        private readonly DisplayViewport _viewport;
        private readonly IStringMeasurer _measurer;

        /// <summary>Creates a builder bound to a resolved viewport and a measurer.</summary>
        public DisplayFrameBuilder(DisplayViewport viewport, IStringMeasurer measurer)
        {
            _viewport = viewport;
            _measurer = measurer;
        }

        /// <summary>The accumulated commands, in insertion order.</summary>
        public IReadOnlyList<DrawCommand> Commands => _commands;

        /// <summary>Clears all accumulated commands so the builder can be reused next frame.</summary>
        public void Reset()
        {
            _commands.Clear();
        }

        /// <summary>Adds aligned text at a column reference X (viewport-relative pixels). Measures the text to resolve right/center anchoring.</summary>
        public void AddText(string text, float columnX, float y, DrawAlign align, string font, float scale, Color color)
        {
            float width = _measurer.Measure(text, font, scale).Width;
            var cmd = new DrawCommand();
            cmd.Kind = DrawKind.Text;
            cmd.Text = text;
            cmd.X = _viewport.Origin.X + ColumnLayout.ResolveTextX(columnX, width, align);
            cmd.Y = _viewport.Origin.Y + y;
            cmd.Font = font;
            cmd.Scale = scale;
            cmd.Align = align;
            cmd.Color = color;
            _commands.Add(cmd);
        }

        /// <summary>Adds a filled rectangle (viewport-relative pixels).</summary>
        public void AddRect(float x, float y, float width, float height, Color color)
        {
            var cmd = new DrawCommand();
            cmd.Kind = DrawKind.Rect;
            cmd.X = _viewport.Origin.X + x;
            cmd.Y = _viewport.Origin.Y + y;
            cmd.Width = width;
            cmd.Height = height;
            cmd.Color = color;
            _commands.Add(cmd);
        }

        /// <summary>Adds a horizontal status bar: a track rectangle plus a proportional fill rectangle for <paramref name="percent"/> (0..100). Fill is omitted at 0%.</summary>
        public void AddBar(float x, float y, float width, float height, int percent, Color trackColor, Color fillColor)
        {
            AddRect(x, y, width, height, trackColor);
            float fillWidth = StatusBarGeometry.FillWidth(width, percent);
            if (fillWidth > 0f)
            {
                AddRect(x, y, fillWidth, height, fillColor);
            }
        }
    }
}
