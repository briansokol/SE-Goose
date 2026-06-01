namespace IngameScript
{
    /// <summary>Pure horizontal status-bar math: clamps percent and computes the proportional fill width within a track.</summary>
    public static class StatusBarGeometry
    {
        /// <summary>Fill width in pixels for a track of <paramref name="trackWidth"/> at <paramref name="percent"/> (clamped 0..100).</summary>
        public static float FillWidth(float trackWidth, int percent)
        {
            int p = percent;
            if (p < 0)
            {
                p = 0;
            }
            if (p > 100)
            {
                p = 100;
            }
            return trackWidth * (p / 100f);
        }
    }
}
