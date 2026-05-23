using System;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    /// <summary>Pure-static helpers for the observed-item catalog key format (<c>Type/Subtype</c>, prefix stripped).</summary>
    public static class CatalogKeyHelpers
    {
        /// <summary>Strips the <c>MyObjectBuilder_</c> prefix from a TypeId, if present.</summary>
        public static string StripObjectBuilderPrefix(string typeId)
        {
            const string prefix = "MyObjectBuilder_";
            if (!string.IsNullOrEmpty(typeId) && typeId.StartsWith(prefix, StringComparison.Ordinal))
            {
                return typeId.Substring(prefix.Length);
            }
            return typeId ?? string.Empty;
        }

        /// <summary>Builds the un-prefixed <c>Type/Subtype</c> key used as the catalog dictionary key.</summary>
        public static string BuildKey(MyItemType type)
        {
            return StripObjectBuilderPrefix(type.TypeId) + "/" + (type.SubtypeId ?? string.Empty);
        }
    }
}
