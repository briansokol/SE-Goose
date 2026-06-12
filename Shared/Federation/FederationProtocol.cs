namespace IngameScript
{
    /// <summary>Wire-protocol constants for the Goose-to-Goose presence beacon. Distinct channel from the Goose-Crane bridge so the two protocols never cross-talk.</summary>
    public static class FederationProtocol
    {
        /// <summary>Default IGC broadcast tag; the version suffix keeps mismatched versions from detecting each other.</summary>
        public const string DefaultChannelTag = "se.goose.fed.v1";

        /// <summary>Major protocol version advertised in every announce.</summary>
        public const int ProtocolVersion = 1;

        /// <summary>Periodic presence announcement carrying PB identity, construct signature, and construct grid set.</summary>
        public const string KindAnnounce = "fedAnnounce";

        /// <summary>Envelope key carrying the sender's programmable-block EntityId.</summary>
        public const string KeyPbId = "pb";

        /// <summary>Envelope key carrying the sender's mechanical-only construct signature (unsigned 64-bit, base-10).</summary>
        public const string KeySignature = "sig";

        /// <summary>Envelope key carrying the sender's construct grid EntityIds, joined by <see cref="GridsDelimiter"/>.</summary>
        public const string KeyGrids = "grids";

        /// <summary>Inner delimiter for the <c>grids=</c> field.</summary>
        public const char GridsDelimiter = '|';
    }
}
