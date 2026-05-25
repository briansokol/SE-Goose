using System;

namespace IngameScript
{
    /// <summary>Pure-static helpers for parsing block-name tags (<c>[Stock]</c>, <c>[P:NN]</c>, <c>[Balance=N]</c>, <c>[Type/Subtype:Value]</c>, etc.).</summary>
    public static class BlockNameTags
    {
        /// <summary>Name-tag on a rotor/piston/hinge base that excludes its TopGrid from scope.</summary>
        public const string NoSubgridTag = "[NoSubgrid]";

        /// <summary>Name-tag on a PB-side connector that admits the currently-locked remote ship into scope.</summary>
        public const string FederateTag = "[Federate]";

        /// <summary>Name-tag on a block that opts it out of management entirely.</summary>
        public const string IgnoreTag = "[Ignore]";

        /// <summary>Semantic alias for <see cref="IgnoreTag"/>.</summary>
        public const string ManualTag = "[Manual]";

        /// <summary>Legacy alias for <see cref="IgnoreTag"/>.</summary>
        public const string LockedTag = "[Locked]";

        /// <summary>Returns true when <paramref name="name"/> contains <paramref name="tag"/> as a substring.</summary>
        public static bool NameHasTag(string name, string tag)
        {
            return !string.IsNullOrEmpty(name) && name.IndexOf(tag, StringComparison.Ordinal) >= 0;
        }

        /// <summary>Returns true when <paramref name="name"/> carries an ignore-style tag (<c>[Ignore]</c>, <c>[Manual]</c>, <c>[Locked]</c>).</summary>
        public static bool HasIgnoreTag(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            return name.IndexOf(IgnoreTag, StringComparison.Ordinal) >= 0
                || name.IndexOf(ManualTag, StringComparison.Ordinal) >= 0
                || name.IndexOf(LockedTag, StringComparison.Ordinal) >= 0;
        }
    }
}
