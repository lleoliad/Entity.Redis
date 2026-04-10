#if FANTASY_NET
using System.Collections.Concurrent;
using Fantasy;
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.Platform.Net;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

namespace Entities.Redis
{
    /// <summary>
    /// Redis 缓存组件，挂载在 Scene 上，提供缓存操作功能
    /// </summary>
    public sealed class RedisCacheComponent : Entity
    {
        private RedisDatabase _redisDatabase;
        private DatabaseConfig _databaseConfig;
        private readonly ConcurrentDictionary<string, RedisSubscription> _subscriptions = new();

        public RedisDatabase RedisDatabase => _redisDatabase;
        public DatabaseConfig DatabaseConfig => _databaseConfig;

        public static async FTask<RedisCacheComponent> Initialize(Scene scene)
        {
            if (scene.World.Config.DatabaseConfig != null)
            {
                foreach (var databaseConfig in scene.World.Config.DatabaseConfig)
                {
                    if (!string.IsNullOrWhiteSpace(databaseConfig.DbConnection))
                    {
                        var dbType = databaseConfig.DbType.ToLower();

                        switch (dbType)
                        {
                            case "redis":
                            {
                                try
                                {
                                    var redisCacheComponent = scene.GetOrAddComponent<RedisCacheComponent>().Initialize(databaseConfig);
                                    return redisCacheComponent;
                                }
                                catch (Exception e)
                                {
                                    Log.Error($"WorldId:{scene.World.Id} DbName:{databaseConfig.DbName} DbConnection:{databaseConfig.DbConnection} Initialization failed. Please check if the Redis server can be connected normally.\n{e.Message}");
                                }

                                break;
                            }
                        }
                    }
                }
            }

            await FTask.CompletedTask;
            return null;
        }

        public RedisCacheComponent Initialize(DatabaseConfig databaseConfig)
        {
            var redisDatabase = new RedisDatabase();
            redisDatabase.Initialize(this.Scene, databaseConfig.DbConnection, databaseConfig.DbName);
            return Initialize(redisDatabase, databaseConfig);
        }

        /// <summary>
        /// 初始化 Redis 缓存组件
        /// </summary>
        internal RedisCacheComponent Initialize(RedisDatabase redisDatabase, DatabaseConfig databaseConfig)
        {
            _redisDatabase = redisDatabase;
            _databaseConfig = databaseConfig;
            return this;
        }

        #region Basic Cache Operations

        /// <summary>
        /// 获取缓存数据
        /// </summary>
        public async FTask<T> GetAsync<T>(string key) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning null for cache operation");
                return null;
            }

            return await _redisDatabase.GetAsync<T>(key);
        }

        /// <summary>
        /// 设置缓存数据
        /// </summary>
        public async FTask SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, skipping cache operation");
                return;
            }

            await _redisDatabase.SetAsync(key, value, expiry);
        }

        /// <summary>
        /// 删除缓存数据
        /// </summary>
        public async FTask<long> DeleteAsync(params string[] keys)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, skipping cache delete operation");
                return 0;
            }

            return await _redisDatabase.DeleteAsync(keys);
        }

        /// <summary>
        /// 检查键是否存在
        /// </summary>
        public async FTask<bool> ExistsAsync(string key)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning false for Exists operation");
                return false;
            }

            return await _redisDatabase.ExistsAsync(key);
        }

        /// <summary>
        /// 设置过期时间
        /// </summary>
        public async FTask<bool> ExpireAsync(string key, TimeSpan expire)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning false for Expire operation");
                return false;
            }

            return await _redisDatabase.ExpireAsync(key, expire);
        }

        /// <summary>
        /// 获取剩余过期时间（秒）
        /// </summary>
        public async FTask<long> TtlAsync(string key)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning -1 for Ttl operation");
                return -1;
            }

            return await _redisDatabase.TtlAsync(key);
        }

        #endregion

        #region String Operations

        /// <summary>
        /// 自增操作
        /// </summary>
        public async FTask<long> IncrByAsync(string key, long value = 1)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning 0 for IncrBy operation");
                return 0;
            }

            return await _redisDatabase.IncrByAsync(key, value);
        }

        /// <summary>
        /// 自减操作
        /// </summary>
        public async FTask<long> DecrByAsync(string key, long value = 1)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning 0 for DecrBy operation");
                return 0;
            }

            return await _redisDatabase.DecrByAsync(key, value);
        }

        /// <summary>
        /// 字符串追加
        /// </summary>
        public async FTask<long> AppendAsync(string key, string value)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning 0 for Append operation");
                return 0;
            }

            return await _redisDatabase.AppendAsync(key, value);
        }

        /// <summary>
        /// 获取字符串长度
        /// </summary>
        public async FTask<long> StrLenAsync(string key)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning 0 for StrLen operation");
                return 0;
            }

            return await _redisDatabase.StrLenAsync(key);
        }

        #endregion

        #region Hash Operations

        /// <summary>
        /// 设置 Hash 字段
        /// </summary>
        public async FTask<bool> HSetAsync<T>(string key, string field, T value) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning false for HSet operation");
                return false;
            }

            return await _redisDatabase.HSetAsync(key, field, value);
        }

        /// <summary>
        /// 获取 Hash 字段
        /// </summary>
        public async FTask<T> HGetAsync<T>(string key, string field) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning null for HGet operation");
                return null;
            }

            return await _redisDatabase.HGetAsync<T>(key, field);
        }

        /// <summary>
        /// 删除 Hash 字段
        /// </summary>
        public async FTask<long> HDelAsync(string key, params string[] fields)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning 0 for HDel operation");
                return 0;
            }

            return await _redisDatabase.HDelAsync(key, fields);
        }

        /// <summary>
        /// 检查 Hash 字段是否存在
        /// </summary>
        public async FTask<bool> HExistsAsync(string key, string field)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning false for HExists operation");
                return false;
            }

            return await _redisDatabase.HExistsAsync(key, field);
        }

        /// <summary>
        /// 获取所有 Hash 字段
        /// </summary>
        public async FTask<string[]> HKeysAsync(string key)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning empty array for HKeys operation");
                return Array.Empty<string>();
            }

            return await _redisDatabase.HKeysAsync(key);
        }

        /// <summary>
        /// 获取 Hash 字段数量
        /// </summary>
        public async FTask<long> HLenAsync(string key)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning 0 for HLen operation");
                return 0;
            }

            return await _redisDatabase.HLenAsync(key);
        }

        #endregion

        #region List Operations

        /// <summary>
        /// 从列表左侧推入值
        /// </summary>
        public async FTask<long> LPushAsync<T>(string key, T value) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning 0 for LPush operation");
                return 0;
            }

            return await _redisDatabase.LPushAsync(key, value);
        }

        /// <summary>
        /// 从列表右侧推入值
        /// </summary>
        public async FTask<long> RPushAsync<T>(string key, T value) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning 0 for RPush operation");
                return 0;
            }

            return await _redisDatabase.RPushAsync(key, value);
        }

        /// <summary>
        /// 从列表左侧弹出值
        /// </summary>
        public async FTask<T> LPopAsync<T>(string key) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning null for LPop operation");
                return null;
            }

            return await _redisDatabase.LPopAsync<T>(key);
        }

        /// <summary>
        /// 从列表右侧弹出值
        /// </summary>
        public async FTask<T> RPopAsync<T>(string key) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning null for RPop operation");
                return null;
            }

            return await _redisDatabase.RPopAsync<T>(key);
        }

        /// <summary>
        /// 获取列表长度
        /// </summary>
        public async FTask<long> LLenAsync(string key)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning 0 for LLen operation");
                return 0;
            }

            return await _redisDatabase.LLenAsync(key);
        }

        /// <summary>
        /// 获取列表指定范围的元素
        /// </summary>
        public async FTask<List<T>> LRangeAsync<T>(string key, long start = 0, long stop = -1) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning empty list for LRange operation");
                return new List<T>();
            }

            return await _redisDatabase.LRangeAsync<T>(key, start, stop);
        }

        #endregion

        #region Set Operations

        /// <summary>
        /// 向集合添加成员
        /// </summary>
        public async FTask<long> SAddAsync<T>(string key, T value) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning 0 for SAdd operation");
                return 0;
            }

            return await _redisDatabase.SAddAsync(key, value);
        }

        /// <summary>
        /// 获取集合所有成员
        /// </summary>
        public async FTask<List<T>> SMembersAsync<T>(string key) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning empty list for SMembers operation");
                return new List<T>();
            }

            return await _redisDatabase.SMembersAsync<T>(key);
        }

        /// <summary>
        /// 移除集合成员
        /// </summary>
        public async FTask<long> SRemAsync<T>(string key, T value) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning 0 for SRem operation");
                return 0;
            }

            return await _redisDatabase.SRemAsync(key, value);
        }

        /// <summary>
        /// 判断成员是否在集合中
        /// </summary>
        public async FTask<bool> SIsMemberAsync<T>(string key, T value) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning false for SIsMember operation");
                return false;
            }

            return await _redisDatabase.SIsMemberAsync(key, value);
        }

        /// <summary>
        /// 获取集合成员数
        /// </summary>
        public async FTask<long> SCardAsync(string key)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning 0 for SCard operation");
                return 0;
            }

            return await _redisDatabase.SCardAsync(key);
        }

        #endregion

        #region Sorted Set Operations

        /// <summary>
        /// 向有序集合添加成员
        /// </summary>
        public async FTask<bool> ZAddAsync<T>(string key, T value, double score) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning false for ZAdd operation");
                return false;
            }

            return await _redisDatabase.ZAddAsync(key, value, score);
        }

        /// <summary>
        /// 获取有序集合指定范围的成员
        /// </summary>
        public async FTask<List<T>> ZRangeAsync<T>(string key, long start = 0, long stop = -1) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning empty list for ZRange operation");
                return new List<T>();
            }

            return await _redisDatabase.ZRangeAsync<T>(key, start, stop);
        }

        /// <summary>
        /// 获取有序集合指定分数范围的成员
        /// </summary>
        public async FTask<List<T>> ZRangeByScoreAsync<T>(string key, double min, double max) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning empty list for ZRangeByScore operation");
                return new List<T>();
            }

            return await _redisDatabase.ZRangeByScoreAsync<T>(key, min, max);
        }

        /// <summary>
        /// 移除有序集合成员
        /// </summary>
        public async FTask<long> ZRemAsync<T>(string key, T value) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning 0 for ZRem operation");
                return 0;
            }

            return await _redisDatabase.ZRemAsync(key, value);
        }

        /// <summary>
        /// 获取有序集合成员数
        /// </summary>
        public async FTask<long> ZCardAsync(string key)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning 0 for ZCard operation");
                return 0;
            }

            return await _redisDatabase.ZCardAsync(key);
        }

        /// <summary>
        /// 获取成员的分数
        /// </summary>
        public async FTask<double> ZScoreAsync<T>(string key, T value) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning 0 for ZScore operation");
                return 0;
            }

            return await _redisDatabase.ZScoreAsync(key, value);
        }

        /// <summary>
        /// 增加成员的分数
        /// </summary>
        public async FTask<double> ZIncrByAsync<T>(string key, T value, double increment) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning 0 for ZIncrBy operation");
                return 0;
            }

            return await _redisDatabase.ZIncrByAsync(key, value, increment);
        }

        #endregion

        #region Distributed Lock

        /// <summary>
        /// 获取分布式锁
        /// </summary>
        public async FTask<RedisDistributedLock?> AcquireLockAsync(string lockKey, TimeSpan expiry, TimeSpan? retryTimeout = null)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning null for AcquireLock operation");
                return null;
            }

            return await RedisDistributedLock.AcquireAsync(_redisDatabase, lockKey, expiry, retryTimeout);
        }

        #endregion

        #region Pub/Sub

        /// <summary>
        /// 发布消息
        /// </summary>
        public async FTask<long> PublishAsync(string channel, string message)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning 0 for Publish operation");
                return 0;
            }

            return await PublishAsync(channel, System.Text.Encoding.UTF8.GetBytes(message));
        }

        /// <summary>
        /// 发布消息（字节）
        /// </summary>
        public async FTask<long> PublishAsync(string channel, byte[] message)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning 0 for Publish operation");
                return 0;
            }

            try
            {
                var redisClient = _redisDatabase.GetRedisClient();
                return await redisClient.PublishAsync(channel, message);
            }
            catch (Exception e)
            {
                Log.Error($"Redis PublishAsync failed: channel={channel}, error={e.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 订阅频道
        /// </summary>
        public async FTask<RedisSubscription> SubscribeAsync(string channel, Action<string, string> handler)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning null for Subscribe operation");
                return null;
            }

            var subscription = new RedisSubscription(_redisDatabase, channel, (ch, msg) => { handler(ch, System.Text.Encoding.UTF8.GetString(msg)); });

            _subscriptions[channel] = subscription;
            await subscription.SubscribeAsync();
            return subscription;
        }

        /// <summary>
        /// 订阅频道（字节）
        /// </summary>
        public async FTask<RedisSubscription> SubscribeBytesAsync(string channel, Action<string, byte[]> handler)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning null for SubscribeBytes operation");
                return null;
            }

            var subscription = new RedisSubscription(_redisDatabase, channel, handler);
            _subscriptions[channel] = subscription;
            await subscription.SubscribeAsync();
            return subscription;
        }

        /// <summary>
        /// 取消订阅频道
        /// </summary>
        public async FTask UnsubscribeAsync(string channel)
        {
            if (_subscriptions.TryRemove(channel, out var subscription))
            {
                await subscription.UnsubscribeAsync();
            }
        }

        /// <summary>
        /// 取消所有订阅
        /// </summary>
        public async FTask UnsubscribeAllAsync()
        {
            foreach (var subscription in _subscriptions.Values)
            {
                await subscription.UnsubscribeAsync();
            }

            _subscriptions.Clear();
        }

        #endregion

        #region Pattern-based Operations

        /// <summary>
        /// 根据模式获取键列表
        /// </summary>
        public async FTask<List<string>> KeysAsync(string pattern)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (_redisDatabase == null)
            {
                Log.Warning("RedisDatabase is not initialized, returning empty list for Keys operation");
                return new List<string>();
            }

            try
            {
                var redisClient = _redisDatabase.GetRedisClient();
                var keys = await redisClient.KeysAsync(pattern);
                return new List<string>(keys);
            }
            catch (Exception e)
            {
                Log.Error($"Redis KeysAsync failed: pattern={pattern}, error={e.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// 根据模式删除键
        /// </summary>
        public async FTask<long> DeleteByPatternAsync(string pattern)
        {
            var keys = await KeysAsync(pattern);
            if (keys.Count == 0)
            {
                return 0;
            }

            return await DeleteAsync(keys.ToArray());
        }

        #endregion

        public override void Dispose()
        {
            // 取消所有订阅
            UnsubscribeAllAsync().Coroutine();

            base.Dispose();
        }
    }
}
#endif