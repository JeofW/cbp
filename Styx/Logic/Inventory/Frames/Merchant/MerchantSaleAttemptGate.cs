using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace Styx.Logic.Inventory.Frames.Merchant
{
    // A local submission is not a server acknowledgement. Suppress the same
    // observed player/merchant/slot/link/count for a bounded interval, even if
    // another item was sold. No item is deleted or permanently blacklisted.
    internal sealed class MerchantSaleAttemptGate
    {
        private const int MaximumKeys = 256;
        private const long RetryMilliseconds = 120000;
        private readonly Dictionary<string, long> attempts = new(StringComparer.Ordinal);
        private int executing;
        private string prefix;

        internal int Execute(string script, Func<string, List<string>> query, long now)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (now < 0 || now > long.MaxValue - RetryMilliseconds) return -1;
            if (Interlocked.CompareExchange(ref executing, 1, 0) != 0) return 2;
            try
            {
                foreach (string key in attempts.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
                {
                    attempts.Remove(key);
                    prefix = null;
                }
                // Backpressure is not an empty bag or a confirmed sale.
                if (attempts.Count >= MaximumKeys) return 4;
                if (prefix == null)
                {
                    var text = new StringBuilder("local blockedSaleStacks={");
                    foreach (string key in attempts.Keys.OrderBy(value => value, StringComparer.Ordinal))
                    {
                        text.Append("[\"");
                        foreach (byte value in Encoding.UTF8.GetBytes(key))
                            text.Append('\\').Append(value.ToString("D3", CultureInfo.InvariantCulture));
                        text.Append("\"]=true,");
                    }
                    prefix = text.Append("};").ToString();
                }
                // Bound generated source as well as the number of retained keys.
                if (prefix.Length > 65536) return 4;
                List<string> values = query(prefix + script);
                if (values == null || values.Count < 2 || values[0] != "ok" ||
                    !int.TryParse(values[1], NumberStyles.None, CultureInfo.InvariantCulture, out int result) ||
                    result < 0 || result > 3)
                    return -1;
                if (result != 1) return values.Count == 2 ? result : -1;
                if (values.Count != 3 || string.IsNullOrEmpty(values[2]) ||
                    Encoding.UTF8.GetByteCount(values[2]) > 2048)
                    return -1;

                attempts[values[2]] = now + RetryMilliseconds;
                prefix = null;
                return 1;
            }
            finally { Volatile.Write(ref executing, 0); }
        }
    }
}
