#if FANTASY_NET
using System.Text;

namespace Entities.Redis
{
    /// <summary>
    /// Helper for building Redis keys using a consistent naming convention.
    /// </summary>
    public sealed class RedisKeyBuilder
    {
        private readonly string _prefix;
        private readonly string _separator;

        /// <summary>
        /// Default key prefix.
        /// </summary>
        public const string DefaultPrefix = "entities";

        /// <summary>
        /// Default key separator.
        /// </summary>
        public const string DefaultSeparator = ":";

        /// <summary>
        /// Creates a Redis key builder.
        /// </summary>
        public RedisKeyBuilder(string prefix = DefaultPrefix, string separator = DefaultSeparator)
        {
            _prefix = prefix;
            _separator = separator;
        }

        /// <summary>
        /// Builds a full Redis key from the provided parts.
        /// </summary>
        public string Build(params string[] parts)
        {
            if (parts == null || parts.Length == 0)
            {
                return _prefix;
            }

            var sb = new StringBuilder(_prefix);

            foreach (var part in parts)
            {
                if (!string.IsNullOrEmpty(part))
                {
                    sb.Append(_separator).Append(Sanitize(part));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Removes invalid characters from a key segment.
        /// </summary>
        private string Sanitize(string part)
        {
            // Redis keys should not contain spaces, newlines, or other control characters.
            return part.Replace(" ", "_")
                       .Replace("\n", "")
                       .Replace("\r", "")
                       .Replace("\t", "");
        }

        #region Predefined Key Patterns

        /// <summary>
        /// Entity cache key: entities:entity:{EntityType}:{EntityId}
        /// </summary>
        public string Entity(string entityType, long entityId)
        {
            return Build("entity", entityType, entityId.ToString());
        }

        /// <summary>
        /// Entity cache key: entities:entity:{EntityType}:{EntityId}
        /// </summary>
        public string Entity<T>(long entityId) where T : class
        {
            return Entity(typeof(T).Name, entityId);
        }

        /// <summary>
        /// User session key: entities:session:{UserId}
        /// </summary>
        public string Session(long userId)
        {
            return Build("session", userId.ToString());
        }

        /// <summary>
        /// Online user set key: entities:online:users
        /// </summary>
        public string OnlineUsers()
        {
            return Build("online", "users");
        }

        /// <summary>
        /// Online user set key for a scene: entities:online:scene:{SceneId}
        /// </summary>
        public string OnlineScene(uint sceneId)
        {
            return Build("online", "scene", sceneId.ToString());
        }

        /// <summary>
        /// Distributed lock key: entities:lock:{LockName}
        /// </summary>
        public string Lock(string lockName)
        {
            return Build("lock", lockName);
        }

        /// <summary>
        /// Entity lock key: entities:lock:entity:{EntityType}:{EntityId}
        /// </summary>
        public string EntityLock<T>(long entityId) where T : class
        {
            return Build("lock", "entity", typeof(T).Name, entityId.ToString());
        }

        /// <summary>
        /// Resource lock key: entities:lock:resource:{ResourceType}:{ResourceId}
        /// </summary>
        public string ResourceLock(string resourceType, string resourceId)
        {
            return Build("lock", "resource", resourceType, resourceId);
        }

        /// <summary>
        /// Transfer lock key: entities:lock:transfer:{FromUserId}:{ToUserId}:{ItemId}
        /// </summary>
        public string TransferLock(long fromUserId, long toUserId, long itemId)
        {
            return Build("lock", "transfer", fromUserId.ToString(), toUserId.ToString(), itemId.ToString());
        }

        /// <summary>
        /// Counter key: entities:counter:{CounterName}
        /// </summary>
        public string Counter(string counterName)
        {
            return Build("counter", counterName);
        }

        /// <summary>
        /// Daily counter key: entities:counter:daily:{CounterName}:{Date}
        /// </summary>
        public string DailyCounter(string counterName, DateTime date)
        {
            return Build("counter", "daily", counterName, date.ToString("yyyyMMdd"));
        }

        /// <summary>
        /// Ranking key: entities:rank:{RankName}
        /// </summary>
        public string Rank(string rankName)
        {
            return Build("rank", rankName);
        }

        /// <summary>
        /// Ranking key with type: entities:rank:{RankType}:{RankName}
        /// </summary>
        public string Rank(string rankType, string rankName)
        {
            return Build("rank", rankType, rankName);
        }

        /// <summary>
        /// List key: entities:list:{ListName}
        /// </summary>
        public string List(string listName)
        {
            return Build("list", listName);
        }

        /// <summary>
        /// Set key: entities:set:{SetName}
        /// </summary>
        public string Set(string setName)
        {
            return Build("set", setName);
        }

        /// <summary>
        /// Hash key: entities:hash:{HashName}
        /// </summary>
        public string Hash(string hashName)
        {
            return Build("hash", hashName);
        }

        /// <summary>
        /// Player hash key: entities:hash:player:{PlayerId}
        /// </summary>
        public string PlayerHash(long playerId)
        {
            return Build("hash", "player", playerId.ToString());
        }

        /// <summary>
        /// Pub/Sub channel key: entities:channel:{ChannelName}
        /// </summary>
        public string Channel(string channelName)
        {
            return Build("channel", channelName);
        }

        /// <summary>
        /// Inter-world channel key: entities:channel:world:{FromWorldId}:{ToWorldId}
        /// </summary>
        public string WorldChannel(byte fromWorldId, byte toWorldId)
        {
            return Build("channel", "world", fromWorldId.ToString(), toWorldId.ToString());
        }

        /// <summary>
        /// Inter-scene channel key: entities:channel:scene:{FromSceneId}:{ToSceneId}
        /// </summary>
        public string SceneChannel(uint fromSceneId, uint toSceneId)
        {
            return Build("channel", "scene", fromSceneId.ToString(), toSceneId.ToString());
        }

        /// <summary>
        /// Event channel key: entities:event:{EventName}
        /// </summary>
        public string Event(string eventName)
        {
            return Build("event", eventName);
        }

        /// <summary>
        /// Rate-limit key: entities:ratelimit:{ResourceName}:{Identifier}
        /// </summary>
        public string RateLimit(string resourceName, string identifier)
        {
            return Build("ratelimit", resourceName, identifier);
        }

        /// <summary>
        /// User rate-limit key: entities:ratelimit:user:{UserId}:{Action}
        /// </summary>
        public string UserRateLimit(long userId, string action)
        {
            return Build("ratelimit", "user", userId.ToString(), action);
        }

        /// <summary>
        /// IP rate-limit key: entities:ratelimit:ip:{IpAddress}:{Action}
        /// </summary>
        public string IpRateLimit(string ipAddress, string action)
        {
            return Build("ratelimit", "ip", ipAddress.Replace(".", "_"), action);
        }

        /// <summary>
        /// Cache tag key: entities:tags:{TagName}
        /// </summary>
        public string Tag(string tagName)
        {
            return Build("tags", tagName);
        }

        /// <summary>
        /// TTL tracking key: entities:ttl:{EntityType}:{EntityId}
        /// </summary>
        public string Ttl(string entityType, long entityId)
        {
            return Build("ttl", entityType, entityId.ToString());
        }

        /// <summary>
        /// Cache warmup key: entities:warmup:{EntityType}
        /// </summary>
        public string Warmup(string entityType)
        {
            return Build("warmup", entityType);
        }

        /// <summary>
        /// Custom key: entities:custom:{CustomKey}
        /// </summary>
        public string Custom(string customKey)
        {
            return Build("custom", customKey);
        }

        #endregion

        #region Pattern Matching

        /// <summary>
        /// Pattern for all entity keys: entities:entity:*
        /// </summary>
        public string EntityPattern()
        {
            return Build("entity", "*");
        }

        /// <summary>
        /// Pattern for entity keys of a specific type: entities:entity:{EntityType}:*
        /// </summary>
        public string EntityPattern<T>() where T : class
        {
            return Build("entity", typeof(T).Name, "*");
        }

        /// <summary>
        /// Pattern for all lock keys: entities:lock:*
        /// </summary>
        public string LockPattern()
        {
            return Build("lock", "*");
        }

        /// <summary>
        /// Pattern for all session keys: entities:session:*
        /// </summary>
        public string SessionPattern()
        {
            return Build("session", "*");
        }

        /// <summary>
        /// Pattern for all channel keys: entities:channel:*
        /// </summary>
        public string ChannelPattern()
        {
            return Build("channel", "*");
        }

        /// <summary>
        /// Custom pattern.
        /// </summary>
        public string Pattern(params string[] parts)
        {
            var newParts = new string[parts.Length + 1];
            Array.Copy(parts, 0, newParts, 0, parts.Length);
            newParts[parts.Length] = "*";
            return Build(newParts);
        }

        #endregion
    }

    /// <summary>
    /// Extension methods for appending metadata to Redis keys.
    /// </summary>
    public static class RedisKeyExtensions
    {
        /// <summary>
        /// Adds an expiration suffix, typically for bookkeeping keys.
        /// </summary>
        public static string WithExpiry(this string key, TimeSpan expiry)
        {
            return $"{key}:expiry:{expiry.TotalSeconds}";
        }

        /// <summary>
        /// Adds a timestamp suffix.
        /// </summary>
        public static string WithTimestamp(this string key, DateTime timestamp)
        {
            return $"{key}:ts:{timestamp.Ticks}";
        }

        /// <summary>
        /// Adds a version suffix.
        /// </summary>
        public static string WithVersion(this string key, int version)
        {
            return $"{key}:v:{version}";
        }

        /// <summary>
        /// Adds a shard suffix for sharded storage.
        /// </summary>
        public static string WithShard(this string key, int shardId)
        {
            return $"{key}:shard:{shardId}";
        }

        /// <summary>
        /// Calculates a stable shard identifier from the key.
        /// </summary>
        /// <remarks>
        /// Uses a deterministic hash so keys map consistently across processes and restarts.
        /// </remarks>
        public static int GetShardId(this string key, int shardCount)
        {
            if (shardCount <= 0)
            {
                return 0;
            }

            var hash = GetStableHash(key);
            return Math.Abs(hash) % shardCount;
        }

        private static int GetStableHash(string key)
        {
            // FNV-1a hash — deterministic across .NET runtimes and processes.
            const uint fnvPrime = 16777619;
            const uint offsetBasis = 2166136261;

            uint hash = offsetBasis;
            foreach (char c in key)
            {
                hash ^= (byte)(c & 0xFF);
                hash *= fnvPrime;
                hash ^= (byte)(c >> 8);
                hash *= fnvPrime;
            }

            return unchecked((int)hash);
        }

        /// <summary>
        /// Builds a sharded key.
        /// </summary>
        public static string WithSharding(this string key, int shardCount)
        {
            var shardId = key.GetShardId(shardCount);
            return key.WithShard(shardId);
        }
    }
}
#endif
