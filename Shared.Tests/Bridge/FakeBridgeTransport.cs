using System.Collections.Generic;
using IngameScript;

namespace Shared.Tests
{
    /// <summary>In-memory transport that records every sent payload and exposes an inbox the test can fill to simulate peer traffic.</summary>
    public class FakeBridgeTransport : IBridgeTransport
    {
        /// <summary>All payloads that the bridge has sent, in order.</summary>
        public List<string> Sent { get; } = new List<string>();

        /// <summary>Payloads to deliver on the next <see cref="DrainInbox"/> call. The bridge consumes (and clears) the entire queue per drain.</summary>
        public List<string> Inbox { get; } = new List<string>();

        /// <summary>Records <paramref name="payload"/> on <see cref="Sent"/>.</summary>
        public void Send(string payload)
        {
            Sent.Add(payload);
        }

        /// <summary>Transfers the current contents of <see cref="Inbox"/> into <paramref name="outBuf"/> and clears the inbox.</summary>
        public void DrainInbox(StringList outBuf)
        {
            for (int i = 0; i < Inbox.Count; i++)
            {
                outBuf.Add(Inbox[i]);
            }
            Inbox.Clear();
        }

        /// <summary>Returns the index of the first sent payload whose serialized form starts with <c>kind=<paramref name="kind"/></c>, or <c>-1</c> when none exists.</summary>
        public int FindFirstKindIndex(string kind)
        {
            string prefix = "kind=" + kind;
            for (int i = 0; i < Sent.Count; i++)
            {
                string s = Sent[i];
                if (s != null && s.StartsWith(prefix) && (s.Length == prefix.Length || s[prefix.Length] == ';'))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Returns the count of sent payloads whose <c>kind=</c> matches <paramref name="kind"/>.</summary>
        public int CountKind(string kind)
        {
            string prefix = "kind=" + kind;
            int n = 0;
            for (int i = 0; i < Sent.Count; i++)
            {
                string s = Sent[i];
                if (s != null && s.StartsWith(prefix) && (s.Length == prefix.Length || s[prefix.Length] == ';'))
                {
                    n++;
                }
            }
            return n;
        }
    }
}
