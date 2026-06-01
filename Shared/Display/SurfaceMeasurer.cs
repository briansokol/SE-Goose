using System.Text;
using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    /// <summary>Live-surface implementation of <see cref="IStringMeasurer"/>. Wraps <c>MeasureStringInPixels</c> so the surface owns font metrics.</summary>
    public class SurfaceMeasurer : IStringMeasurer
    {
        private readonly IMyTextSurface _surface;
        private readonly StringBuilder _sb = new StringBuilder();

        /// <summary>Binds the measurer to a live text surface.</summary>
        public SurfaceMeasurer(IMyTextSurface surface)
        {
            _surface = surface;
        }

        /// <summary>Measures text by delegating to the SE surface and copying its result into a <see cref="MeasuredSize"/>.</summary>
        public MeasuredSize Measure(string text, string font, float scale)
        {
            _sb.Clear();
            _sb.Append(text);
            VRageMath.Vector2 size = _surface.MeasureStringInPixels(_sb, font, scale);
            return new MeasuredSize(size.X, size.Y);
        }
    }
}
