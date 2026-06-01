namespace IngameScript
{
    /// <summary>Pixel dimensions of a measured text string. Plain data so layout math stays free of the SE runtime.</summary>
    public struct MeasuredSize
    {
        /// <summary>Measured width in pixels.</summary>
        public float Width;

        /// <summary>Measured height in pixels.</summary>
        public float Height;

        /// <summary>Creates a measured size.</summary>
        public MeasuredSize(float width, float height)
        {
            Width = width;
            Height = height;
        }
    }
}
