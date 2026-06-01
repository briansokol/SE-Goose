using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    /// <summary>Renders resolved <see cref="DrawCommand"/>s to a live surface and derives its viewport. The only piece that touches DrawFrame/MySprite.</summary>
    public static class SurfaceRenderer
    {
        /// <summary>Derives the drawing viewport from a live surface's metrics.</summary>
        public static DisplayViewport CreateViewport(IMyTextSurface surface)
        {
            return DisplayViewport.FromSurfaceMetrics(surface.SurfaceSize, surface.TextureSize, surface.TextPadding);
        }

        /// <summary>Switches the surface to script mode and draws every command in a single frame.</summary>
        public static void Render(IMyTextSurface surface, IReadOnlyList<DrawCommand> commands)
        {
            surface.ContentType = ContentType.SCRIPT;
            surface.Script = "";
            MySpriteDrawFrame frame = surface.DrawFrame();
            for (int i = 0; i < commands.Count; i++)
            {
                DrawCommand c = commands[i];
                if (c.Kind == DrawKind.Text)
                {
                    frame.Add(new MySprite(SpriteType.TEXT, c.Text, new Vector2(c.X, c.Y), null, c.Color, c.Font, TextAlignment.LEFT, c.Scale));
                }
                else
                {
                    var center = new Vector2(c.X + c.Width / 2f, c.Y + c.Height / 2f);
                    frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", center, new Vector2(c.Width, c.Height), c.Color, null, TextAlignment.CENTER, 0f));
                }
            }
            frame.Dispose();
        }
    }
}
