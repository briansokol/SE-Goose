namespace IngameScript
{
    /// <summary>A per-ingot min/max threshold parsed from the <c>[CRefine]</c> section.</summary>
    public class RefineThreshold
    {
        /// <summary>Ingot count below which the matching ore is bumped to the front of the feed order. <c>0</c> disables the bump.</summary>
        public long Min;

        /// <summary>Ingot count at or above which the matching ore is dropped from feeding (a cap). <c>0</c> disables the cap.</summary>
        public long Max;
    }
}
