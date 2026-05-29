using System;
using System.Collections.Generic;
using System.Text;

namespace IngameScript
{
    /// <summary>Parsed bridge envelope: a string of <c>k=v</c> pairs separated by <see cref="BridgeProtocol.FieldSeparator"/>. Values containing the separator are not supported (the only multi-valued fields use inner delimiters).</summary>
    public class BridgeMessage
    {
        private readonly Dictionary<string, string> _fields = new Dictionary<string, string>();

        /// <summary>Initializes an empty message with the given <c>kind</c> field.</summary>
        public BridgeMessage(string kind)
        {
            _fields[BridgeProtocol.KeyKind] = kind;
        }

        /// <summary>Message kind (the value of the <c>kind=</c> field). <c>null</c> if missing.</summary>
        public string Kind
        {
            get
            {
                string k;
                return _fields.TryGetValue(BridgeProtocol.KeyKind, out k) ? k : null;
            }
        }

        /// <summary>Sets a string field on the envelope. Overwrites any prior value.</summary>
        public BridgeMessage Set(string key, string value)
        {
            _fields[key] = value ?? string.Empty;
            return this;
        }

        /// <summary>Sets an integer field on the envelope.</summary>
        public BridgeMessage Set(string key, int value)
        {
            _fields[key] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return this;
        }

        /// <summary>Sets a long field on the envelope.</summary>
        public BridgeMessage Set(string key, long value)
        {
            _fields[key] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return this;
        }

        /// <summary>Fetches a string field; returns <paramref name="fallback"/> when the key is absent.</summary>
        public string GetString(string key, string fallback)
        {
            string v;
            return _fields.TryGetValue(key, out v) ? v : fallback;
        }

        /// <summary>Fetches an integer field; returns <paramref name="fallback"/> when the key is missing or unparseable.</summary>
        public int GetInt(string key, int fallback)
        {
            string v;
            if (!_fields.TryGetValue(key, out v))
            {
                return fallback;
            }
            int parsed;
            return int.TryParse(v, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        /// <summary>Fetches a long field; returns <paramref name="fallback"/> when the key is missing or unparseable.</summary>
        public long GetLong(string key, long fallback)
        {
            string v;
            if (!_fields.TryGetValue(key, out v))
            {
                return fallback;
            }
            long parsed;
            return long.TryParse(v, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        /// <summary>Serializes the envelope to the wire string. The <c>kind</c> field is always emitted first.</summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append(BridgeProtocol.KeyKind);
            sb.Append(BridgeProtocol.KeyValueSeparator);
            sb.Append(Kind ?? string.Empty);
            foreach (KeyValuePair<string, string> kv in _fields)
            {
                if (kv.Key == BridgeProtocol.KeyKind)
                {
                    continue;
                }
                sb.Append(BridgeProtocol.FieldSeparator);
                sb.Append(kv.Key);
                sb.Append(BridgeProtocol.KeyValueSeparator);
                sb.Append(kv.Value);
            }
            return sb.ToString();
        }

        /// <summary>Parses a wire payload into a <see cref="BridgeMessage"/>. Returns <c>null</c> when the payload is empty, malformed, or missing a <c>kind</c> field.</summary>
        public static BridgeMessage Parse(string payload)
        {
            if (string.IsNullOrEmpty(payload))
            {
                return null;
            }

            var msg = new BridgeMessage(string.Empty);
            msg._fields.Clear();

            string[] pairs = payload.Split(BridgeProtocol.FieldSeparator);
            for (int i = 0; i < pairs.Length; i++)
            {
                string pair = pairs[i];
                if (pair.Length == 0)
                {
                    continue;
                }

                int eq = pair.IndexOf(BridgeProtocol.KeyValueSeparator);
                if (eq <= 0)
                {
                    return null;
                }

                string key = pair.Substring(0, eq);
                string value = pair.Substring(eq + 1);
                msg._fields[key] = value;
            }

            return msg._fields.ContainsKey(BridgeProtocol.KeyKind) ? msg : null;
        }

        /// <summary>Builds a <c>hello</c> envelope.</summary>
        public static BridgeMessage Hello(BridgeRole role)
        {
            return new BridgeMessage(BridgeProtocol.KindHello)
                .Set(BridgeProtocol.KeyRole, BridgeProtocol.RoleString(role))
                .Set(BridgeProtocol.KeyVersion, BridgeProtocol.ProtocolVersion);
        }

        /// <summary>Builds a <c>heartbeat</c> envelope.</summary>
        public static BridgeMessage Heartbeat(BridgeRole role, int catalogCount)
        {
            return new BridgeMessage(BridgeProtocol.KindHeartbeat)
                .Set(BridgeProtocol.KeyRole, BridgeProtocol.RoleString(role))
                .Set(BridgeProtocol.KeyVersion, BridgeProtocol.ProtocolVersion)
                .Set(BridgeProtocol.KeyCatalogCount, catalogCount);
        }

        /// <summary>Builds a <c>catalogAdd</c> envelope carrying a single key.</summary>
        public static BridgeMessage CatalogAdd(string key)
        {
            return new BridgeMessage(BridgeProtocol.KindCatalogAdd)
                .Set(BridgeProtocol.KeyKey, key);
        }

        /// <summary>Builds an <c>assemblerHold</c> envelope.</summary>
        public static BridgeMessage AssemblerHold(long entityId, int ttlTicks, string needCsv)
        {
            return new BridgeMessage(BridgeProtocol.KindAssemblerHold)
                .Set(BridgeProtocol.KeyEntityId, entityId)
                .Set(BridgeProtocol.KeyTtl, ttlTicks)
                .Set(BridgeProtocol.KeyNeed, needCsv ?? string.Empty);
        }

        /// <summary>Builds an <c>itemCountReq</c> envelope carrying a <see cref="BridgeProtocol.KeysDelimiter"/>-separated list of item keys to count.</summary>
        public static BridgeMessage ItemCountRequest(IEnumerable<string> keys)
        {
            var sb = new StringBuilder();
            if (keys != null)
            {
                foreach (string key in keys)
                {
                    if (string.IsNullOrEmpty(key))
                    {
                        continue;
                    }
                    if (sb.Length > 0)
                    {
                        sb.Append(BridgeProtocol.KeysDelimiter);
                    }
                    sb.Append(key);
                }
            }
            return new BridgeMessage(BridgeProtocol.KindItemCountRequest)
                .Set(BridgeProtocol.KeyKeys, sb.ToString());
        }

        /// <summary>Builds an <c>itemCountRes</c> envelope carrying <c>key:amount</c> pairs for the supplied counts. Stops appending once the serialized payload would exceed <paramref name="maxChars"/>, invoking <paramref name="onTruncated"/> once if anything was dropped.</summary>
        public static BridgeMessage ItemCountResponse(IEnumerable<KeyValuePair<string, long>> counts, int maxChars, Action onTruncated)
        {
            string envelopePrefix = BridgeProtocol.KeyKind + "=" + BridgeProtocol.KindItemCountResponse
                + BridgeProtocol.FieldSeparator + BridgeProtocol.KeyCounts + "=";
            int baseLen = envelopePrefix.Length;

            var sb = new StringBuilder();
            bool truncated = false;
            if (counts != null)
            {
                foreach (KeyValuePair<string, long> kv in counts)
                {
                    if (string.IsNullOrEmpty(kv.Key))
                    {
                        continue;
                    }
                    string entry = kv.Key + BridgeProtocol.CountPairSeparator
                        + kv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    int addCost = sb.Length == 0 ? entry.Length : 1 + entry.Length;
                    if (baseLen + sb.Length + addCost > maxChars)
                    {
                        truncated = true;
                        break;
                    }
                    if (sb.Length > 0)
                    {
                        sb.Append(BridgeProtocol.KeysDelimiter);
                    }
                    sb.Append(entry);
                }
            }

            if (truncated && onTruncated != null)
            {
                onTruncated();
            }

            return new BridgeMessage(BridgeProtocol.KindItemCountResponse)
                .Set(BridgeProtocol.KeyCounts, sb.ToString());
        }

        /// <summary>Splits <paramref name="keys"/> into <c>catalogSnapshot</c> envelopes whose serialized length does not exceed <paramref name="maxChars"/>. Output preserves the input order; concatenating each chunk's <c>keys</c> field reconstructs the full set.</summary>
        public static IEnumerable<BridgeMessage> CatalogSnapshotChunks(IEnumerable<string> keys, int maxChars)
        {
            var chunks = new List<BridgeMessage>();
            if (keys == null)
            {
                return chunks;
            }

            string envelopePrefix = BridgeProtocol.KeyKind + "=" + BridgeProtocol.KindCatalogSnapshot
                + BridgeProtocol.FieldSeparator + BridgeProtocol.KeyKeys + "=";
            int baseLen = envelopePrefix.Length;

            var current = new StringBuilder();
            foreach (string key in keys)
            {
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }
                int addCost = current.Length == 0 ? key.Length : 1 + key.Length;
                if (current.Length > 0 && baseLen + current.Length + addCost > maxChars)
                {
                    chunks.Add(new BridgeMessage(BridgeProtocol.KindCatalogSnapshot)
                        .Set(BridgeProtocol.KeyKeys, current.ToString()));
                    current.Length = 0;
                    addCost = key.Length;
                }
                if (current.Length > 0)
                {
                    current.Append(BridgeProtocol.KeysDelimiter);
                }
                current.Append(key);
            }

            if (current.Length > 0)
            {
                chunks.Add(new BridgeMessage(BridgeProtocol.KindCatalogSnapshot)
                    .Set(BridgeProtocol.KeyKeys, current.ToString()));
            }

            return chunks;
        }
    }
}
