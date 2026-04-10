#if FANTASY_NET
using Fantasy;
using Fantasy.Async;
using Fantasy.Entitas;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
#pragma warning disable CS8603 // Possible null reference return.

namespace Entities.Redis
{
    /// <summary>
    /// Cache-Aside 模式缓存实现
    /// 使用流程:
    /// 1. 先查缓存
    /// 2. 缓存命中则返回
    /// 3. 缓存未命中则查数据库
    /// 4. 将数据库结果写入缓存
    /// 5. 返回结果
    /// </summary>
    public sealed class CacheAside : ICacheStore, IEntityCacheStore
    {
        private readonly RedisCacheComponent _redisCache;
        private readonly RedisKeyBuilder _keyBuilder;
        private readonly CacheOptions _options;
        private readonly CacheStatistics _statistics;

        /// <summary>
        /// 缓存统计信息
        /// </summary>
        public CacheStatistics Statistics => _statistics;

        /// <summary>
        /// 创建 Cache-Aside 缓存实例
        /// </summary>
        public CacheAside(RedisCacheComponent redisCache, CacheOptions? options = null)
        {
            _redisCache = redisCache;
            _options = options ?? new CacheOptions();
            _keyBuilder = new RedisKeyBuilder();
            _statistics = new CacheStatistics();
        }

        /// <summary>
        /// 创建 Cache-Aside 缓存实例（自定义键前缀）
        /// </summary>
        public CacheAside(RedisCacheComponent redisCache, string prefix, CacheOptions? options = null)
            : this(redisCache, options)
        {
            _keyBuilder = new RedisKeyBuilder(prefix);
        }

        #region ICacheStore Implementation

        public async FTask<T> GetAsync<T>(string key) where T : class
        {
            if (!_options.Enabled)
            {
                return null;
            }

            try
            {
                var value = await _redisCache.GetAsync<T>(key);

                if (_options.EnableStatistics)
                {
                    // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                    if (value != null)
                    {
                        _statistics.HitCount++;
                    }
                    else
                    {
                        _statistics.MissCount++;
                    }
                }

                return value;
            }
            catch (Exception e)
            {
                Log.Error($"CacheAside GetAsync failed: key={key}, error={e.Message}");
                _statistics.MissCount++;
                return null;
            }
        }

        public async FTask SetAsync<T>(string key, T? value, TimeSpan? expiry = null) where T : class
        {
            if (!_options.Enabled)
            {
                return;
            }

            try
            {
                var effectiveExpiry = expiry ?? _options.DefaultExpiry;

                // 如果启用了空值缓存，且值为 null
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (value == null && _options.CacheNullValues)
                {
                    // 缓存一个特殊标记表示空值
                    await _redisCache.SetAsync(key, new NullValuePlaceholder(), effectiveExpiry);
                    _statistics.SetCount++;
                    return;
                }

#pragma warning disable CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
                await _redisCache.SetAsync(key, value, effectiveExpiry);
#pragma warning restore CS8634 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'class' constraint.
                _statistics.SetCount++;
            }
            catch (Exception e)
            {
                Log.Error($"CacheAside SetAsync failed: key={key}, error={e.Message}");
            }
        }

        public async FTask<long> DeleteAsync(params string[] keys)
        {
            if (!_options.Enabled)
            {
                return 0;
            }

            try
            {
                var count = await _redisCache.DeleteAsync(keys);
                _statistics.DeleteCount += (int)count;
                return count;
            }
            catch (Exception e)
            {
                Log.Error($"CacheAside DeleteAsync failed: keys={string.Join(",", keys)}, error={e.Message}");
                return 0;
            }
        }

        public async FTask<bool> ExistsAsync(string key)
        {
            if (!_options.Enabled)
            {
                return false;
            }

            try
            {
                return await _redisCache.ExistsAsync(key);
            }
            catch (Exception e)
            {
                Log.Error($"CacheAside ExistsAsync failed: key={key}, error={e.Message}");
                return false;
            }
        }

        public async FTask<bool> ExpireAsync(string key, TimeSpan expire)
        {
            if (!_options.Enabled)
            {
                return false;
            }

            try
            {
                return await _redisCache.ExpireAsync(key, expire);
            }
            catch (Exception e)
            {
                Log.Error($"CacheAside ExpireAsync failed: key={key}, error={e.Message}");
                return false;
            }
        }

        public async FTask<long> TtlAsync(string key)
        {
            if (!_options.Enabled)
            {
                return -1;
            }

            try
            {
                return await _redisCache.TtlAsync(key);
            }
            catch (Exception e)
            {
                Log.Error($"CacheAside TtlAsync failed: key={key}, error={e.Message}");
                return -1;
            }
        }

        public async FTask<Dictionary<string, T>> GetManyAsync<T>(string[] keys) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (!_options.Enabled || keys == null || keys.Length == 0)
            {
                return new Dictionary<string, T>();
            }

            var result = new Dictionary<string, T>();

            foreach (var key in keys)
            {
                var value = await GetAsync<T>(key);
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (value != null)
                {
                    result[key] = value;
                }
            }

            return result;
        }

        public async FTask SetManyAsync<T>(Dictionary<string, T> items, TimeSpan? expiry = null) where T : class
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (!_options.Enabled || items == null || items.Count == 0)
            {
                return;
            }

            foreach (var kvp in items)
            {
                await SetAsync(kvp.Key, kvp.Value, expiry);
            }
        }

        public async FTask<long> DeleteByPatternAsync(string pattern)
        {
            if (!_options.Enabled)
            {
                return 0;
            }

            try
            {
                var keys = await _redisCache.KeysAsync(pattern);
                if (keys.Count == 0)
                {
                    return 0;
                }

                return await _redisCache.DeleteAsync(keys.ToArray());
            }
            catch (Exception e)
            {
                Log.Error($"CacheAside DeleteByPatternAsync failed: pattern={pattern}, error={e.Message}");
                return 0;
            }
        }

        #endregion

        #region IEntityCacheStore Implementation

        public async FTask<T> GetEntityAsync<T>(long id) where T : Entity
        {
            var key = _keyBuilder.Entity<T>(id);
            return await GetAsync<T>(key);
        }

        public async FTask SetEntityAsync<T>(T entity, TimeSpan? expiry = null) where T : Entity
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (entity == null)
            {
                return;
            }

            var key = _keyBuilder.Entity<T>(entity.Id);
            await SetAsync(key, entity, expiry);
        }

        public async FTask<bool> RemoveEntityAsync<T>(long id) where T : Entity
        {
            var key = _keyBuilder.Entity<T>(id);
            var count = await DeleteAsync(key);
            return count > 0;
        }

        public async FTask<bool> ExistsEntityAsync<T>(long id) where T : Entity
        {
            var key = _keyBuilder.Entity<T>(id);
            return await ExistsAsync(key);
        }

        public async FTask<T> GetOrLoadEntityAsync<T>(long id, Func<FTask<T>> loader, TimeSpan? cacheDuration = null) where T : Entity
        {
            var key = _keyBuilder.Entity<T>(id);

            // 先查缓存
            var cached = await GetAsync<T>(key);
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (cached != null)
            {
                // 检查是否是空值占位符
                // ReSharper disable once SuspiciousTypeConversion.Global
                if (cached is NullValuePlaceholder)
                {
                    return null;
                }

                return cached;
            }

            // 缓存未命中，从数据源加载
            try
            {
                var entity = await loader();

                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (entity == null && _options.CacheNullValues)
                {
                    // 缓存空值，防止缓存穿透
                    await SetAsync<T>(key, null, cacheDuration ?? _options.NullValueExpiry);
                }
                else if (entity != null)
                {
                    // 写入缓存
                    await SetAsync(key, entity, cacheDuration ?? _options.DefaultExpiry);
                }

                return entity;
            }
            catch (Exception e)
            {
                Log.Error($"CacheAside GetOrLoadEntityAsync loader failed: key={key}, error={e.Message}");
                throw;
            }
        }

        #endregion

        #region Extension Methods

        /// <summary>
        /// 获取或加载（泛型）
        /// </summary>
        public async FTask<T> GetOrLoadAsync<T>(string key, Func<FTask<T>> loader, TimeSpan? cacheDuration = null) where T : class
        {
            // 先查缓存
            var cached = await GetAsync<T>(key);
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (cached != null)
            {
                // 检查是否是空值占位符
                if (cached is NullValuePlaceholder)
                {
                    return null;
                }

                return cached;
            }

            // 缓存未命中，从数据源加载
            try
            {
                var value = await loader();

                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (value == null && _options.CacheNullValues)
                {
                    // 缓存空值，防止缓存穿透
                    await SetAsync<T>(key, null, cacheDuration ?? _options.NullValueExpiry);
                }
                else if (value != null)
                {
                    // 写入缓存
                    await SetAsync(key, value, cacheDuration ?? _options.DefaultExpiry);
                }

                return value;
            }
            catch (Exception e)
            {
                Log.Error($"CacheAside GetOrLoadAsync loader failed: key={key}, error={e.Message}");
                throw;
            }
        }

        /// <summary>
        /// 获取或加载（带分布式锁，防止缓存击穿）
        /// </summary>
        public async FTask<T> GetOrLoadWithLockAsync<T>(string key, Func<FTask<T>> loader, TimeSpan? cacheDuration = null, TimeSpan? lockTimeout = null) where T : class
        {
            // 先查缓存
            var cached = await GetAsync<T>(key);
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (cached != null)
            {
                if (cached is NullValuePlaceholder)
                {
                    return null;
                }

                return cached;
            }

            // 使用分布式锁防止缓存击穿
            var lockKey = _keyBuilder.Lock($"load:{key}");

            await using var @lock = await _redisCache.AcquireLockAsync(
                lockKey,
                TimeSpan.FromSeconds(10),
                lockTimeout ?? TimeSpan.FromSeconds(5));

            if (@lock == null)
            {
                // 获取锁失败，直接从数据源加载
                return await loader();
            }

            // 获取锁成功，再次检查缓存（double-check）
            cached = await GetAsync<T>(key);
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (cached != null)
            {
                if (cached is NullValuePlaceholder)
                {
                    return null;
                }

                return cached;
            }

            // 从数据源加载
            try
            {
                var value = await loader();

                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (value == null && _options.CacheNullValues)
                {
                    await SetAsync<T>(key, null, cacheDuration ?? _options.NullValueExpiry);
                }
                else if (value != null)
                {
                    await SetAsync(key, value, cacheDuration ?? _options.DefaultExpiry);
                }

                return value;
            }
            catch (Exception e)
            {
                Log.Error($"CacheAside GetOrLoadWithLockAsync loader failed: key={key}, error={e.Message}");
                throw;
            }
        }

        /// <summary>
        /// 刷新缓存（删除）
        /// </summary>
        public async FTask InvalidateAsync(params string[] keys)
        {
            await DeleteAsync(keys);
        }

        /// <summary>
        /// 刷新实体缓存
        /// </summary>
        public async FTask InvalidateEntityAsync<T>(long id) where T : Entity
        {
            await RemoveEntityAsync<T>(id);
        }

        /// <summary>
        /// 刷新类型下的所有缓存
        /// </summary>
        public async FTask InvalidateTypeAsync<T>() where T : class
        {
            var pattern = _keyBuilder.EntityPattern<T>();
            await DeleteByPatternAsync(pattern);
        }

        /// <summary>
        /// 预热缓存
        /// </summary>
        public async FTask WarmUpAsync<T>(IEnumerable<long> ids, Func<long, FTask<T>> loader) where T : Entity
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (ids == null)
            {
                return;
            }

            foreach (var id in ids)
            {
                try
                {
                    await GetOrLoadEntityAsync(id, () => loader(id));
                }
                catch (Exception e)
                {
                    Log.Error($"CacheAside WarmUpAsync failed for id={id}: {e.Message}");
                }
            }
        }

        #endregion

        #region Null Value Placeholder

        /// <summary>
        /// 空值占位符，用于防止缓存穿透
        /// </summary>
        private sealed class NullValuePlaceholder
        {
        }

        #endregion
    }

    /// <summary>
    /// Scene 扩展方法，提供便捷的缓存操作
    /// </summary>
    public static class SceneCacheExtensions
    {
        /// <summary>
        /// 获取或加载实体（优先缓存）
        /// </summary>
        public static async FTask<T> GetOrFetchAsync<T>(
            this Scene scene,
            string cacheKey,
            Func<FTask<T>> fetchFromDb,
            TimeSpan? cacheDuration = null) where T : class
        {
            // var cache = scene.RedisCacheComponent;
            var cache = scene.GetComponent<RedisCacheComponent>();

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (cache == null)
            {
                // Redis 不可用，直接从数据库获取
                return await fetchFromDb();
            }

            var cacheAside = new CacheAside(cache);
            return await cacheAside.GetOrLoadAsync(cacheKey, fetchFromDb, cacheDuration);
        }

        /// <summary>
        /// 获取或加载实体（优先缓存，使用实体类型作为键前缀）
        /// </summary>
        public static async FTask<T> GetOrFetchAsync<T>(
            this Scene scene,
            long entityId,
            Func<FTask<T>> fetchFromDb,
            TimeSpan? cacheDuration = null) where T : Entity
        {
            // var cache = scene.RedisCacheComponent;
            var cache = scene.GetComponent<RedisCacheComponent>();

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (cache == null)
            {
                return await fetchFromDb();
            }

            var keyBuilder = new RedisKeyBuilder();
            var cacheKey = keyBuilder.Entity<T>(entityId);

            var cacheAside = new CacheAside(cache);
            return await cacheAside.GetOrLoadAsync(cacheKey, fetchFromDb, cacheDuration);
        }

        /// <summary>
        /// 刷新实体缓存
        /// </summary>
        public static async FTask InvalidateCacheAsync<T>(this Scene scene, long entityId) where T : Entity
        {
            // var cache = scene.RedisCacheComponent;
            var cache = scene.GetComponent<RedisCacheComponent>();

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (cache == null)
            {
                return;
            }

            var keyBuilder = new RedisKeyBuilder();
            var cacheKey = keyBuilder.Entity<T>(entityId);
            await cache.DeleteAsync(cacheKey);
        }

        /// <summary>
        /// 使用分布式锁执行操作
        /// </summary>
        public static async FTask WithDistributedLockAsync(
            this Scene scene,
            string lockName,
            TimeSpan lockExpiry,
            Func<FTask> action)
        {
            // var cache = scene.RedisCacheComponent;
            var cache = scene.GetComponent<RedisCacheComponent>();

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (cache == null)
            {
                // Redis 不可用，直接执行操作
                await action();
                return;
            }

            var keyBuilder = new RedisKeyBuilder();
            var lockKey = keyBuilder.Lock(lockName);

            await using var @lock = await cache.AcquireLockAsync(lockKey, lockExpiry);

            if (@lock == null)
            {
                throw new InvalidOperationException($"Failed to acquire distributed lock: {lockName}");
            }

            await action();
        }

        /// <summary>
        /// 使用分布式锁执行操作（带返回值）
        /// </summary>
        public static async FTask<T> WithDistributedLockAsync<T>(
            this Scene scene,
            string lockName,
            TimeSpan lockExpiry,
            Func<FTask<T>> func)
        {
            // var cache = scene.RedisCacheComponent;
            var cache = scene.GetComponent<RedisCacheComponent>();

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (cache == null)
            {
                return await func();
            }

            var keyBuilder = new RedisKeyBuilder();
            var lockKey = keyBuilder.Lock(lockName);

            await using var @lock = await cache.AcquireLockAsync(lockKey, lockExpiry);

            if (@lock == null)
            {
                throw new InvalidOperationException($"Failed to acquire distributed lock: {lockName}");
            }

            return await func();
        }

        /// <summary>
        /// 发布跨服务器消息
        /// </summary>
        public static async FTask PublishMessageAsync(
            this Scene scene,
            string channel,
            string message)
        {
            // var cache = scene.RedisCacheComponent;
            var cache = scene.GetComponent<RedisCacheComponent>();

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (cache == null)
            {
                Log.Warning($"Redis is not available, cannot publish message to channel: {channel}");
                return;
            }

            await cache.PublishAsync(channel, message);
        }

        /// <summary>
        /// 订阅跨服务器消息
        /// </summary>
        public static async FTask<RedisSubscription> SubscribeMessageAsync(
            this Scene scene,
            string channel,
            Action<string, string> handler)
        {
            // var cache = scene.RedisCacheComponent;
            var cache = scene.GetComponent<RedisCacheComponent>();

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (cache == null)
            {
                throw new InvalidOperationException("Redis is not available for message subscription");
            }

            return await cache.SubscribeAsync(channel, handler);
        }
    }
}
#endif