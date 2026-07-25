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

        /// <summary>Opening of the federate tag, shared by the bare <c>[Federate]</c> and prioritized <c>[Federate P:n]</c> forms.</summary>
        public const string FederatePrefix = "[Federate";

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

        /// <summary>Parses the federation priority from a connector name. Lower numbers win; a bare <c>[Federate]</c> means the highest priority (<c>0</c>).</summary>
        /// <param name="name">Connector CustomName to inspect.</param>
        /// <returns>The parsed priority, <c>0</c> for a bare <c>[Federate]</c>, or <c>-1</c> when no federate tag is present.</returns>
        public static int ParseFederatePriority(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return -1;
            }

            int idx = name.IndexOf(FederatePrefix, StringComparison.Ordinal);
            if (idx < 0)
            {
                return -1;
            }

            int after = idx + FederatePrefix.Length;
            if (after >= name.Length)
            {
                return -1;
            }

            char next = name[after];
            if (next == ']')
            {
                return 0;
            }

            if (next != ' ')
            {
                return -1;
            }

            int p = after + 1;
            while (p < name.Length && name[p] == ' ')
            {
                p++;
            }

            if (p + 1 >= name.Length || name[p] != 'P' || name[p + 1] != ':')
            {
                return -1;
            }

            p += 2;
            int start = p;
            while (p < name.Length && name[p] >= '0' && name[p] <= '9')
            {
                p++;
            }

            if (p == start)
            {
                return -1;
            }

            int value;
            if (!int.TryParse(name.Substring(start, p - start), out value))
            {
                return -1;
            }

            return value;
        }
    }
}
