using VRageMath;

namespace IngameScript
{
    /// <summary>Resolved drawing coordinate space for a single surface: an origin offset and a drawable size. Pure data plus a pure factory.</summary>
    public struct DisplayViewport
    {
        /// <summary>Pixel offset of the drawable area's top-left within the texture.</summary>
        public Vector2 Origin;

        /// <summary>Width and height of the drawable area in pixels.</summary>
        public Vector2 Size;

        /// <summary>Builds a viewport from raw surface metrics, encapsulating SE's texture/padding model so callers work in a clean [0..Size] space.</summary>
        /// <param name="surfaceSize">surface.SurfaceSize.</param>
        /// <param name="textureSize">surface.TextureSize.</param>
        /// <param name="textPadding">surface.TextPadding (percent, 0..100); applied to both edges.</param>
        public static DisplayViewport FromSurfaceMetrics(Vector2 surfaceSize, Vector2 textureSize, float textPadding)
        {
            float pad = textPadding / 100f;
            var padPx = new Vector2(surfaceSize.X * pad, surfaceSize.Y * pad);
            var v = new DisplayViewport();
            v.Origin = (textureSize - surfaceSize) / 2f + padPx;
            v.Size = surfaceSize - padPx * 2f;
            return v;
        }
    }
}
