using VRageMath;

namespace IngameScript
{
    /// <summary>Kind of primitive a <see cref="DrawCommand"/> represents.</summary>
    public enum DrawKind
    {
        /// <summary>A run of text.</summary>
        Text,

        /// <summary>A filled rectangle.</summary>
        Rect
    }

    /// <summary>A resolved draw primitive in surface pixel coordinates. Pure data so layout output is unit-testable without the SE runtime; <see cref="SurfaceRenderer"/> translates it to a sprite.</summary>
    public struct DrawCommand
    {
        /// <summary>Whether this command draws text or a filled rectangle.</summary>
        public DrawKind Kind;

        /// <summary>Left X in surface pixels. Alignment is already resolved into this value.</summary>
        public float X;

        /// <summary>Top Y in surface pixels.</summary>
        public float Y;

        /// <summary>Rectangle width in pixels (Rect only).</summary>
        public float Width;

        /// <summary>Rectangle height in pixels (Rect only).</summary>
        public float Height;

        /// <summary>Text payload (Text only).</summary>
        public string Text;

        /// <summary>Font name for text, e.g. "Debug" (Text only).</summary>
        public string Font;

        /// <summary>Font scale (Text only).</summary>
        public float Scale;

        /// <summary>Original alignment request, retained for reference (Text only).</summary>
        public DrawAlign Align;

        /// <summary>Sprite color.</summary>
        public Color Color;
    }
}
