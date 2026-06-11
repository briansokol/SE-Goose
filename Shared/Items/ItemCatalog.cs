using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.ModAPI.Ingame;

namespace IngameScript
{
    /// <summary>Self-contained observed-item catalog. Persistable blob form keyed by un-prefixed <c>Type/Subtype</c>. Bumps a monotonic version counter on each new entry so consumers can skip work when the catalog hasn't grown.</summary>
    public class ItemCatalog
    {
        private readonly ItemTypeByString _knownItems = new ItemTypeByString();
        private int _version;

        /// <summary>Map of un-prefixed <c>Type/Subtype</c> keys to their resolved <see cref="MyItemType"/>.</summary>
        public IReadOnlyDictionary<string, MyItemType> KnownItems { get { return _knownItems; } }

        /// <summary>Monotonic counter bumped whenever a new key is added.</summary>
        public int Version { get { return _version; } }

        /// <summary>Records an observed item type. Idempotent; no-op when the type is already known.</summary>
        /// <summary>Records an observed item type. Idempotent; no-op when the type is already known.</summary>
        /// <returns><c>true</c> when the key was newly added to the catalog; <c>false</c> when it was already present or the type was unusable.</returns>
        public bool RecordItem(MyItemType type)
        {
            if (string.IsNullOrEmpty(type.SubtypeId))
            {
                return false;
            }

            string key = CatalogKeyHelpers.BuildKey(type);
            if (_knownItems.ContainsKey(key))
            {
                return false;
            }

            _knownItems[key] = type;
            _version++;
            return true;
        }

        /// <summary>Restores the catalog from a serialized blob. One key per line. Malformed lines are silently skipped.</summary>
        public void LoadFromStorage(string blob)
        {
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
                    _version++;
                }
            }
        }

        /// <summary>Serializes the catalog to a newline-delimited blob suitable for <see cref="MyGridProgram.Storage"/>.</summary>
        public string BuildStorageBlob()
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
