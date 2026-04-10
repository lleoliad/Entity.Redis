#if FANTASY_NET
using System.Text;

namespace Entities.Redis
{
    /// <summary>
    /// Redis 键命名规范辅助类
    /// </summary>
    public sealed class RedisKeyBuilder
    {
        private readonly string _prefix;
        private readonly string _separator;

        /// <summary>
        /// 默认键前缀
        /// </summary>
        public const string DefaultPrefix = "entities";

        /// <summary>
        /// 默认分隔符
        /// </summary>
        public const string DefaultSeparator = ":";

        /// <summary>
        /// 创建 Redis 键构建器
        /// </summary>
        public RedisKeyBuilder(string prefix = DefaultPrefix, string separator = DefaultSeparator)
        {
            _prefix = prefix;
            _separator = separator;
        }

        /// <summary>
        /// 构建完整的键名
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
        /// 清理键名中的非法字符
        /// </summary>
        private string Sanitize(string part)
        {
            // Redis 键名不应包含空格、换行等特殊字符
            return part.Replace(" ", "_")
                       .Replace("\n", "")
                       .Replace("\r", "")
                       .Replace("\t", "");
        }

        #region 预定义键名模式

        /// <summary>
        /// 实体缓存键: fantasy:entity:{EntityType}:{EntityId}
        /// </summary>
        public string Entity(string entityType, long entityId)
        {
            return Build("entity", entityType, entityId.ToString());
        }

        /// <summary>
        /// 实体缓存键: fantasy:entity:{EntityType}:{EntityId}
        /// </summary>
        public string Entity<T>(long entityId) where T : class
        {
            return Entity(typeof(T).Name, entityId);
        }

        /// <summary>
        /// 用户会话缓存键: fantasy:session:{UserId}
        /// </summary>
        public string Session(long userId)
        {
            return Build("session", userId.ToString());
        }

        /// <summary>
        /// 在线用户集合键: fantasy:online:users
        /// </summary>
        public string OnlineUsers()
        {
            return Build("online", "users");
        }

        /// <summary>
        /// 在线用户集合键（按场景）: fantasy:online:scene:{SceneId}
        /// </summary>
        public string OnlineScene(uint sceneId)
        {
            return Build("online", "scene", sceneId.ToString());
        }

        /// <summary>
        /// 分布式锁键: fantasy:lock:{LockName}
        /// </summary>
        public string Lock(string lockName)
        {
            return Build("lock", lockName);
        }

        /// <summary>
        /// 分布式锁键（实体）: fantasy:lock:entity:{EntityType}:{EntityId}
        /// </summary>
        public string EntityLock<T>(long entityId) where T : class
        {
            return Build("lock", "entity", typeof(T).Name, entityId.ToString());
        }

        /// <summary>
        /// 分布式锁键（资源）: fantasy:lock:resource:{ResourceType}:{ResourceId}
        /// </summary>
        public string ResourceLock(string resourceType, string resourceId)
        {
            return Build("lock", "resource", resourceType, resourceId);
        }

        /// <summary>
        /// 分布式锁键（物品转移）: fantasy:lock:transfer:{FromUserId}:{ToUserId}:{ItemId}
        /// </summary>
        public string TransferLock(long fromUserId, long toUserId, long itemId)
        {
            return Build("lock", "transfer", fromUserId.ToString(), toUserId.ToString(), itemId.ToString());
        }

        /// <summary>
        /// 计数器键: fantasy:counter:{CounterName}
        /// </summary>
        public string Counter(string counterName)
        {
            return Build("counter", counterName);
        }

        /// <summary>
        /// 每日计数器键: fantasy:counter:daily:{CounterName}:{Date}
        /// </summary>
        public string DailyCounter(string counterName, DateTime date)
        {
            return Build("counter", "daily", counterName, date.ToString("yyyyMMdd"));
        }

        /// <summary>
        /// 排行榜键: fantasy:rank:{RankName}
        /// </summary>
        public string Rank(string rankName)
        {
            return Build("rank", rankName);
        }

        /// <summary>
        /// 排行榜键（按类型）: fantasy:rank:{RankType}:{RankName}
        /// </summary>
        public string Rank(string rankType, string rankName)
        {
            return Build("rank", rankType, rankName);
        }

        /// <summary>
        /// 列表键: fantasy:list:{ListName}
        /// </summary>
        public string List(string listName)
        {
            return Build("list", listName);
        }

        /// <summary>
        /// 集合键: fantasy:set:{SetName}
        /// </summary>
        public string Set(string setName)
        {
            return Build("set", setName);
        }

        /// <summary>
        /// 哈希表键: fantasy:hash:{HashName}
        /// </summary>
        public string Hash(string hashName)
        {
            return Build("hash", hashName);
        }

        /// <summary>
        /// 哈希表字段（玩家属性）: fantasy:hash:player:{PlayerId}
        /// </summary>
        public string PlayerHash(long playerId)
        {
            return Build("hash", "player", playerId.ToString());
        }

        /// <summary>
        /// 消息发布订阅频道: fantasy:channel:{ChannelName}
        /// </summary>
        public string Channel(string channelName)
        {
            return Build("channel", channelName);
        }

        /// <summary>
        /// 世界间通信频道: fantasy:channel:world:{FromWorldId}:{ToWorldId}
        /// </summary>
        public string WorldChannel(byte fromWorldId, byte toWorldId)
        {
            return Build("channel", "world", fromWorldId.ToString(), toWorldId.ToString());
        }

        /// <summary>
        /// 场景间通信频道: fantasy:channel:scene:{FromSceneId}:{ToSceneId}
        /// </summary>
        public string SceneChannel(uint fromSceneId, uint toSceneId)
        {
            return Build("channel", "scene", fromSceneId.ToString(), toSceneId.ToString());
        }

        /// <summary>
        /// 事件发布频道: fantasy:event:{EventName}
        /// </summary>
        public string Event(string eventName)
        {
            return Build("event", eventName);
        }

        /// <summary>
        /// 限流键: fantasy:ratelimit:{ResourceName}:{Identifier}
        /// </summary>
        public string RateLimit(string resourceName, string identifier)
        {
            return Build("ratelimit", resourceName, identifier);
        }

        /// <summary>
        /// 用户限流键: fantasy:ratelimit:user:{UserId}:{Action}
        /// </summary>
        public string UserRateLimit(long userId, string action)
        {
            return Build("ratelimit", "user", userId.ToString(), action);
        }

        /// <summary>
        /// 限流键（IP）: fantasy:ratelimit:ip:{IpAddress}:{Action}
        /// </summary>
        public string IpRateLimit(string ipAddress, string action)
        {
            return Build("ratelimit", "ip", ipAddress.Replace(".", "_"), action);
        }

        /// <summary>
        /// 缓存标签键: fantasy:tags:{TagName}
        /// </summary>
        public string Tag(string tagName)
        {
            return Build("tags", tagName);
        }

        /// <summary>
        /// TTL 记录键: fantasy:ttl:{EntityType}:{EntityId}
        /// </summary>
        public string Ttl(string entityType, long entityId)
        {
            return Build("ttl", entityType, entityId.ToString());
        }

        /// <summary>
        /// 缓存预热键: fantasy:warmup:{EntityType}
        /// </summary>
        public string Warmup(string entityType)
        {
            return Build("warmup", entityType);
        }

        /// <summary>
        /// 自定义键: fantasy:custom:{CustomKey}
        /// </summary>
        public string Custom(string customKey)
        {
            return Build("custom", customKey);
        }

        #endregion

        #region 模式匹配

        /// <summary>
        /// 所有实体键模式: fantasy:entity:*
        /// </summary>
        public string EntityPattern()
        {
            return Build("entity", "*");
        }

        /// <summary>
        /// 特定类型实体键模式: fantasy:entity:{EntityType}:*
        /// </summary>
        public string EntityPattern<T>() where T : class
        {
            return Build("entity", typeof(T).Name, "*");
        }

        /// <summary>
        /// 所有锁键模式: fantasy:lock:*
        /// </summary>
        public string LockPattern()
        {
            return Build("lock", "*");
        }

        /// <summary>
        /// 所有会话键模式: fantasy:session:*
        /// </summary>
        public string SessionPattern()
        {
            return Build("session", "*");
        }

        /// <summary>
        /// 所有频道模式: fantasy:channel:*
        /// </summary>
        public string ChannelPattern()
        {
            return Build("channel", "*");
        }

        /// <summary>
        /// 自定义模式
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
    /// Redis 键扩展方法
    /// </summary>
    public static class RedisKeyExtensions
    {
        /// <summary>
        /// 添加过期时间后缀（用于记录）
        /// </summary>
        public static string WithExpiry(this string key, TimeSpan expiry)
        {
            return $"{key}:expiry:{expiry.TotalSeconds}";
        }

        /// <summary>
        /// 添加时间戳后缀
        /// </summary>
        public static string WithTimestamp(this string key, DateTime timestamp)
        {
            return $"{key}:ts:{timestamp.Ticks}";
        }

        /// <summary>
        /// 添加版本后缀
        /// </summary>
        public static string WithVersion(this string key, int version)
        {
            return $"{key}:v:{version}";
        }

        /// <summary>
        /// 添加分片后缀（用于分片存储）
        /// </summary>
        public static string WithShard(this string key, int shardId)
        {
            return $"{key}:shard:{shardId}";
        }

        /// <summary>
        /// 根据哈希值计算分片 ID
        /// </summary>
        public static int GetShardId(this string key, int shardCount)
        {
            if (shardCount <= 0)
            {
                return 0;
            }

            var hash = key.GetHashCode();
            return Math.Abs(hash) % shardCount;
        }

        /// <summary>
        /// 生成分片键
        /// </summary>
        public static string WithSharding(this string key, int shardCount)
        {
            var shardId = key.GetShardId(shardCount);
            return key.WithShard(shardId);
        }
    }
}
#endif
