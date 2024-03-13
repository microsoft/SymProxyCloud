using System;
using System.Collections.Generic;

namespace SymProxyCloud
{
    internal class ProxyTokenCache
    {
        private Dictionary<string, DateTimeOffset> cache;

        public ProxyTokenCache()
        {
            cache = new Dictionary<string, DateTimeOffset>();
        }

        public void AddToken(string token, DateTimeOffset expiresOn)
        {
            cache[token] = expiresOn;
        }

        public bool IsTokenValid(string token)
        {
            if (cache.TryGetValue(token, out DateTimeOffset expiresOn))
            {
                // True if expiresOn is in the future
                return expiresOn > DateTimeOffset.UtcNow;
            }

            return false;
        }

        public int GetTokenCount()
        {
            return cache.Count;
        }

        public string GetTokenFromStoreIfExists()
        {
            if (cache.Count > 0)
            {
                foreach (var kvp in cache)
                {
                    if (kvp.Value > DateTimeOffset.UtcNow)
                    {
                        return kvp.Key;
                    }
                    else
                    {
                        cache.Remove(kvp.Key);
                    }
                }
            }

            return string.Empty;
        }
    }
}
