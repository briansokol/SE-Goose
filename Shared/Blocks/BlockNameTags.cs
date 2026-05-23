using System;
using System.Collections.Generic;
using VRage.Game.ModAPI.Ingame;

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

        /// <summary>Extracts the priority value from a <c>[P:&lt;n&gt;]</c> tag in a block name.</summary>
        /// <returns>Parsed priority, or 100 when no tag is present.</returns>
        public static int ParsePriorityFromName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return 100;
            }

            int idx = name.IndexOf("[P:", StringComparison.Ordinal);
            if (idx < 0)
            {
                return 100;
            }

            int end = name.IndexOf(']', idx + 3);
            if (end < 0)
            {
                return 100;
            }

            string raw = name.Substring(idx + 3, end - idx - 3);
            int p;
            if (int.TryParse(raw, out p))
            {
                return p;
            }

            return 100;
        }

        /// <summary>Parses an optional <c>[Balance=N]</c> name-tag into a non-negative unit count.</summary>
        /// <returns>Parsed count, or <c>-1</c> if no tag is present.</returns>
        public static long ParseBalanceTagCount(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return -1;
            }

            int idx = name.IndexOf("[Balance=", StringComparison.Ordinal);
            if (idx < 0)
            {
                return -1;
            }

            int end = name.IndexOf(']', idx + 9);
            if (end < 0)
            {
                return -1;
            }

            string raw = name.Substring(idx + 9, end - idx - 9).Trim();
            long v;
            if (!long.TryParse(raw, out v))
            {
                return -1;
            }

            if (v < 0)
            {
                return -1;
            }

            return v;
        }

        /// <summary>Returns true when <paramref name="token"/> contains both a slash and a colon (minimum shape for a name-tag quota override).</summary>
        public static bool LooksLikeNameTagQuota(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            return token.IndexOf('/') >= 0 && token.IndexOf(':') >= 0;
        }

        /// <summary>Parses a single bracketed token shaped like <c>Type/Subtype:Value</c> into its constituent shape strings and quota value.</summary>
        public static bool TryParseNameTagQuotaShape(string token, out string typeIdWithPrefix, out string subtypeId, out long amount, out QuotaMode mode)
        {
            typeIdWithPrefix = null;
            subtypeId = null;
            amount = 0;
            mode = QuotaMode.Exact;
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            int colon = token.IndexOf(':');
            if (colon <= 0 || colon >= token.Length - 1)
            {
                return false;
            }

            string keyPart = token.Substring(0, colon).Trim();
            string valuePart = token.Substring(colon + 1).Trim();
            if (keyPart.Length == 0 || valuePart.Length == 0)
            {
                return false;
            }

            if (!QuotaParsing.TryParseQuotaKeyShape(keyPart, out typeIdWithPrefix, out subtypeId))
            {
                return false;
            }

            return QuotaParsing.TryParseQuotaValue(valuePart, out amount, out mode);
        }

        /// <summary>Walks <paramref name="name"/> and merges every parseable name-tag quota into <paramref name="destination"/>.</summary>
        /// <param name="name">Block display name to scan for <c>[Type/Subtype:Value]</c> tokens.</param>
        /// <param name="destination">Quota dictionary that receives parsed entries.</param>
        /// <param name="typeResolver">Maps a fully-qualified <c>Type/Subtype</c> string to a <see cref="MyItemType"/>, returning <c>null</c> on failure.</param>
        /// <returns>List of malformed or unresolvable token strings, or <c>null</c> when all tokens parsed cleanly.</returns>
        public static List<string> ExtractNameTagQuotas(
            string name,
            Dictionary<MyItemType, StockQuota> destination,
            Func<string, MyItemType?> typeResolver)
        {
            if (string.IsNullOrEmpty(name) || destination == null || typeResolver == null)
            {
                return null;
            }

            List<string> malformed = null;
            int pos = 0;
            while (pos < name.Length)
            {
                int open = name.IndexOf('[', pos);
                if (open < 0)
                {
                    break;
                }

                int close = name.IndexOf(']', open + 1);
                if (close < 0)
                {
                    break;
                }

                string token = name.Substring(open + 1, close - open - 1);
                pos = close + 1;
                if (!LooksLikeNameTagQuota(token))
                {
                    continue;
                }

                string typeIdWithPrefix, subtypeId;
                long amount;
                QuotaMode mode;
                if (!TryParseNameTagQuotaShape(token, out typeIdWithPrefix, out subtypeId, out amount, out mode))
                {
                    if (malformed == null)
                    {
                        malformed = new List<string>();
                    }

                    malformed.Add(token);
                    continue;
                }
                MyItemType? resolved = typeResolver(typeIdWithPrefix + "/" + subtypeId);
                if (!resolved.HasValue)
                {
                    if (malformed == null)
                    {
                        malformed = new List<string>();
                    }

                    malformed.Add(token);
                    continue;
                }
                destination[resolved.Value] = new StockQuota { Amount = amount, Mode = mode };
            }
            return malformed;
        }
    }
}
