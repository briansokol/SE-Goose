using VRage;

namespace IngameScript
{
    /// <summary>Ceiling helpers for autocraft queue accounting. Fixes off-by-one when in-progress crafts round fractional amounts.</summary>
    public static class CeilHelpers
    {
        /// <summary>Ceiling-rounds a <see cref="MyFixedPoint"/> to a <see cref="long"/>. Whole values pass through; fractional values round up.</summary>
        public static long CeilToLong(MyFixedPoint v)
        {
            long whole = (long)v;
            var rebuilt = (MyFixedPoint)(double)whole;
            if (v > rebuilt)
            {
                return whole + 1;
            }

            return whole;
        }
    }
}
