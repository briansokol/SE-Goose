using System;

namespace IngameScript
{
    /// <summary>Guarded invocation helper for optional host callbacks.</summary>
    internal static class SafeCall
    {
        /// <summary>Invokes <paramref name="fn"/>, returning <paramref name="fallback"/> when it is null or throws.</summary>
        /// <param name="context">Label prefixed to the warning on failure.</param>
        /// <param name="warn">Warning sink; may be null.</param>
        public static T Run<T>(Func<T> fn, T fallback, string context, Action<string> warn)
        {
            if (fn == null)
            {
                return fallback;
            }
            try
            {
                return fn();
            }
            catch (Exception ex)
            {
                if (warn != null)
                {
                    warn(context + ": " + ex.Message);
                }
                return fallback;
            }
        }
    }
}
