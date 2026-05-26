using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        /// <summary>Observed item catalog keyed by the un-prefixed <c>Type/Subtype</c> form
        /// (e.g. <c>Component/SteelPlate</c>). Persisted to PB Storage between recompiles.</summary>
        private readonly Dictionary<string, MyItemType> _knownItems = new Dictionary<string, MyItemType>();

        /// <summary>Monotonic counter bumped whenever a new key is added to <see cref="_knownItems"/>.
        /// Consumers (e.g. stock-template renderer) compare against a stored snapshot to skip work
        /// when the catalog hasn't changed.</summary>
        private int _catalogVersion;

        /// <summary>Strips the <c>MyObjectBuilder_</c> prefix from a TypeId, if present.</summary>
        internal static string Catalog_StripObjectBuilderPrefix(string typeId)
        {
            const string prefix = "MyObjectBuilder_";
            if (!string.IsNullOrEmpty(typeId) && typeId.StartsWith(prefix, StringComparison.Ordinal))
            {
                return typeId.Substring(prefix.Length);
            }
            return typeId ?? string.Empty;
        }

        /// <summary>Builds the un-prefixed <c>Type/Subtype</c> key used as the catalog dictionary key.</summary>
        internal static string Catalog_BuildKey(MyItemType type)
        {
            return Catalog_StripObjectBuilderPrefix(type.TypeId) + "/" + (type.SubtypeId ?? string.Empty);
        }

        /// <summary>Records an observed item type. Idempotent; no-op when the type is already known.</summary>
        /// <param name="type">Item type observed during an inventory scan.</param>
        /// <summary>Records an observed item type. Idempotent; no-op when the type is already known.</summary>
        /// <returns><c>true</c> when the key was newly added; <c>false</c> when it was already known or the type was unusable.</returns>
        private bool Catalog_RecordItem(MyItemType type)
        {
            if (string.IsNullOrEmpty(type.SubtypeId))
            {
                return false;
            }

            string key = Catalog_BuildKey(type);
            if (_knownItems.ContainsKey(key))
            {
                return false;
            }

            _knownItems[key] = type;
            _catalogVersion++;
            return true;
        }


        /// <summary>Merges a peer-supplied catalog key (<c>Type/Subtype</c>) into the local catalog. Silently no-ops on malformed input.</summary>
        private void Catalog_HandlePeerKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            int slash = key.IndexOf('/');
            if (slash <= 0 || slash >= key.Length - 1)
            {
                return;
            }

            if (_knownItems.ContainsKey(key))
            {
                return;
            }

            string fullyQualified = "MyObjectBuilder_" + key;
            MyItemType type;
            try
            {
                type = MyItemType.Parse(fullyQualified);
            }
            catch (Exception)
            {
                return;
            }
            _knownItems[key] = type;
            _catalogVersion++;
        }

        /// <summary>Restores the catalog from <see cref="MyGridProgram.Storage"/>. One key per line.
        /// Malformed lines are silently skipped — Storage is best-effort cache, not authoritative state.</summary>
        private void Catalog_LoadFromStorage()
        {
            string blob = Storage;
            if (string.IsNullOrEmpty(blob))
            {
                return;
            }

            string[] lines = blob.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                int slash = line.IndexOf('/');
                if (slash <= 0 || slash >= line.Length - 1)
                {
                    continue;
                }

                string fullyQualified = "MyObjectBuilder_" + line;
                MyItemType type;
                try
                {
                    type = MyItemType.Parse(fullyQualified);
                }
                catch (Exception)
                {
                    continue;
                }
                if (!_knownItems.ContainsKey(line))
                {
                    _knownItems[line] = type;
                    _catalogVersion++;
                }
            }
        }

        /// <summary>Serializes the catalog to a newline-delimited blob suitable for <see cref="MyGridProgram.Storage"/>.</summary>
        /// <returns>One un-prefixed <c>Type/Subtype</c> key per line, in dictionary iteration order.</returns>
        private string Catalog_BuildStorageBlob()
        {
            if (_knownItems.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            bool first = true;
            foreach (KeyValuePair<string, MyItemType> kv in _knownItems)
            {
                if (!first)
                {
                    sb.Append('\n');
                }

                sb.Append(kv.Key);
                first = false;
            }
            return sb.ToString();
        }
    }
}
