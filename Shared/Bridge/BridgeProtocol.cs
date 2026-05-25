using System.Collections.Generic;

namespace IngameScript
{
    /// <summary>Role of the local script on the bridge channel.</summary>
    public enum BridgeRole
    {
        /// <summary>The inventory-management script.</summary>
        Goose,

        /// <summary>The autocrafting script.</summary>
        Crane,
    }

    /// <summary>Wire-protocol constants for the Goose-Crane bridge.</summary>
    public static class BridgeProtocol
    {
        /// <summary>Default IGC broadcast tag; carries the protocol version suffix so peers on a different version simply do not detect each other.</summary>
        public const string DefaultChannelTag = "se.goose.bridge.v1";

        /// <summary>Major protocol version advertised in <c>hello</c> and <c>heartbeat</c> messages.</summary>
        public const int ProtocolVersion = 1;

        /// <summary>Initial peer-discovery message kind.</summary>
        public const string KindHello = "hello";

        /// <summary>Keep-alive message kind, also carrying the sender's catalog count for drift detection.</summary>
        public const string KindHeartbeat = "heartbeat";

        /// <summary>Single-key catalog announcement (sent on local catalog growth).</summary>
        public const string KindCatalogAdd = "catalogAdd";

        /// <summary>Bulk catalog dump used on bootstrap or after detected drift.</summary>
        public const string KindCatalogSnapshot = "catalogSnapshot";

        /// <summary>Crane-to-Goose declaration that an assembler is actively crafting; suppresses Goose's balancer drain.</summary>
        public const string KindAssemblerHold = "assemblerHold";

        /// <summary>Envelope key that names the message kind.</summary>
        public const string KeyKind = "kind";

        /// <summary>Envelope key that names the sender's role (see <see cref="RoleGoose"/>, <see cref="RoleCrane"/>).</summary>
        public const string KeyRole = "role";

        /// <summary>Envelope key carrying the protocol version.</summary>
        public const string KeyVersion = "v";

        /// <summary>Envelope key carrying the sender's catalog key count.</summary>
        public const string KeyCatalogCount = "cat";

        /// <summary>Envelope key carrying a single catalog key on <c>catalogAdd</c>.</summary>
        public const string KeyKey = "key";

        /// <summary>Envelope key carrying a <see cref="KeysDelimiter"/>-separated list of catalog keys on <c>catalogSnapshot</c>.</summary>
        public const string KeyKeys = "keys";

        /// <summary>Envelope key carrying an assembler EntityId.</summary>
        public const string KeyEntityId = "id";

        /// <summary>Envelope key carrying a TTL expressed in main-loop ticks.</summary>
        public const string KeyTtl = "ttl";

        /// <summary>Envelope key carrying a <see cref="NeedDelimiter"/>-separated list of ingot needs (key:amount pairs).</summary>
        public const string KeyNeed = "need";

        /// <summary>Inner delimiter for the <c>keys=</c> field on <c>catalogSnapshot</c>.</summary>
        public const char KeysDelimiter = '|';

        /// <summary>Inner delimiter for the <c>need=</c> field on <c>assemblerHold</c>.</summary>
        public const char NeedDelimiter = ',';

        /// <summary>Field separator between <c>k=v</c> envelope pairs.</summary>
        public const char FieldSeparator = ';';

        /// <summary>Key/value separator inside an envelope field.</summary>
        public const char KeyValueSeparator = '=';

        /// <summary>Role value identifying the Goose script.</summary>
        public const string RoleGoose = "goose";

        /// <summary>Role value identifying the Crane script.</summary>
        public const string RoleCrane = "crane";

        /// <summary>Maximum serialized payload length for a single <c>catalogSnapshot</c> chunk; longer payloads are split.</summary>
        public const int SnapshotMaxPayloadChars = 2048;

        /// <summary>Returns the canonical role string for the given <see cref="BridgeRole"/>.</summary>
        public static string RoleString(BridgeRole role)
        {
            return role == BridgeRole.Goose ? RoleGoose : RoleCrane;
        }
    }

    /// <summary>Lightweight injection seam over <see cref="Sandbox.ModAPI.Ingame.IMyIntergridCommunicationSystem"/> so tests can drive the bridge without a live PB.</summary>
    public interface IBridgeTransport
    {
        /// <summary>Sends a serialized envelope on the bridge channel.</summary>
        void Send(string payload);

        /// <summary>Appends all pending inbound payloads to <paramref name="outBuf"/> and clears the underlying inbox.</summary>
        void DrainInbox(List<string> outBuf);
    }

    /// <summary>Snapshot of the local peer's status, returned by <see cref="Bridge.PeerStatus"/>.</summary>
    public class BridgePeerStatus
    {
        /// <summary>True when a peer message has been received within the staleness window.</summary>
        public bool Linked;

        /// <summary>Tick on which the most recent peer message arrived, or <c>-1</c> when none has been seen.</summary>
        public long LastSeenTick;

        /// <summary>Catalog key count reported by the peer in its last <c>heartbeat</c> or <c>hello</c>.</summary>
        public int PeerCatalogCount;
    }

    /// <summary>Active assembler-hold record stored on the receiver side.</summary>
    public class BridgeHoldHint
    {
        /// <summary>EntityId of the held assembler.</summary>
        public long EntityId;

        /// <summary>Tick at which this hold expires and is swept.</summary>
        public long ExpiryTick;

        /// <summary>Raw need list as transmitted (<c>Ingot/Iron:200,Ingot/Cobalt:50</c>). Hint only; never a commitment.</summary>
        public string NeedRaw;
    }
}
