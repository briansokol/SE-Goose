namespace IngameScript
{
    /// <summary>Pure vertical-fit scaling so a fixed set of rows fills any LCD without per-screen configuration.</summary>
    public static class LayoutScaler
    {
        /// <summary>Font scale that fits <paramref name="rowCount"/> rows into <paramref name="availableHeight"/>, clamped to [<paramref name="minScale"/>, <paramref name="maxScale"/>].</summary>
        /// <param name="availableHeight">Drawable viewport height in pixels.</param>
        /// <param name="rowCount">Number of stacked rows to fit (including any title row).</param>
        /// <param name="lineHeightAtScale1">Measured line height at scale 1.0.</param>
        /// <param name="lineSpacing">Multiplier giving inter-row breathing room (1.0 = tight).</param>
        /// <param name="minScale">Lower clamp for the result.</param>
        /// <param name="maxScale">Upper clamp for the result.</param>
        public static float FitScale(float availableHeight, int rowCount, float lineHeightAtScale1, float lineSpacing, float minScale, float maxScale)
        {
            float denom = rowCount * lineHeightAtScale1 * lineSpacing;
            if (denom <= 0f)
            {
                return maxScale;
            }
            float raw = availableHeight / denom;
            if (raw < minScale)
            {
                return minScale;
            }
            if (raw > maxScale)
            {
                return maxScale;
            }
            return raw;
        }

        /// <summary>Row height in pixels for a chosen scale: <c>lineHeightAtScale1 * scale * lineSpacing</c>.</summary>
        public static float RowHeight(float lineHeightAtScale1, float scale, float lineSpacing)
        {
            return lineHeightAtScale1 * scale * lineSpacing;
        }
    }
}
