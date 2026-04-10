#if FANTASY_NET
using Fantasy;
using Fantasy.Async;
using Fantasy.Entitas;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
#pragma warning disable CS8603 // Possible null reference return.

namespace Entities.Redis
{
    /// <summary>
    /// Cache-Aside implementation.
    /// Workflow:
    /// 1. Read from cache first.
    /// 2. Return immediately on a cache hit.
    /// 3. Load from the backing source on a miss.
    /// 4. Write the loaded value back to cache.
    /// 5. Return the resulting value.
    /// </summary>
    public sealed class CacheAside : ICacheStore, IEntityCacheStore
    {
        private readonly RedisCacheComponent _redisCache;
        private readonly RedisKeyBuilder _keyBuilder;
        private readonly CacheOptions _options;
        private readonly CacheStatistics _statistics;

        /// <summary>
        /// Gets runtime cache statistics.
        /// </summary>
        public CacheStatistics Statistics => _statistics;

        /// <summary>
        /// Creates a Cache-Aside cache instance.
        /// </summary>
        public CacheAside(RedisCacheComponent redisCache, CacheOptions? options = null)
        {
            _redisCache = redisCache;
            _options = options ?? new CacheOptions();
            _keyBuilder = new RedisKeyBuilder();
            _statistics = new CacheStatistics();
        }

        /// <summary>
        /// Creates a Cache-Aside cache instance with a custom key prefix.
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

                // Store a null placeholder when null-value caching is enabled.
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (value == null && _options.CacheNullValues)
                {
                    // Cache a dedicated marker to represent a null payload.
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

            // Read from cache first.
            var cached = await GetAsync<T>(key);
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (cached != null)
            {
                // Check whether the cached value is the null placeholder.
                // ReSharper disable once SuspiciousTypeConversion.Global
                if (cached is NullValuePlaceholder)
                {
                    return null;
                }

                return cached;
            }

            // Cache miss: load the value from the backing source.
            try
            {
                var entity = await loader();

                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (entity == null && _options.CacheNullValues)
                {
                    // Cache the null placeholder to reduce cache penetration.
                    await SetAsync<T>(key, null, cacheDuration ?? _options.NullValueExpiry);
                }
                else if (entity != null)
                {
                    // Cache the loaded entity.
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
        /// Gets a cached value or loads it through the provided factory.
        /// </summary>
        public async FTask<T> GetOrLoadAsync<T>(string key, Func<FTask<T>> loader, TimeSpan? cacheDuration = null) where T : class
        {
            // Read from cache first.
            var cached = await GetAsync<T>(key);
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (cached != null)
            {
                // Check whether the cached value is the null placeholder.
                if (cached is NullValuePlaceholder)
                {
                    return null;
                }

                return cached;
            }

            // Cache miss: load the value from the backing source.
            try
            {
                var value = await loader();

                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (value == null && _options.CacheNullValues)
                {
                    // Cache the null placeholder to reduce cache penetration.
                    await SetAsync<T>(key, null, cacheDuration ?? _options.NullValueExpiry);
                }
                else if (value != null)
                {
                    // Cache the loaded value.
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
        /// Gets a cached value or loads it under a distributed lock to reduce cache breakdown.
        /// </summary>
        public async FTask<T> GetOrLoadWithLockAsync<T>(string key, Func<FTask<T>> loader, TimeSpan? cacheDuration = null, TimeSpan? lockTimeout = null) where T : class
        {
            // Read from cache first.
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

            // Use a distributed lock to prevent multiple loaders from rebuilding the same key.
            var lockKey = _keyBuilder.Lock($"load:{key}");

            await using var @lock = await _redisCache.AcquireLockAsync(
                lockKey,
                TimeSpan.FromSeconds(10),
                lockTimeout ?? TimeSpan.FromSeconds(5));

            if (@lock == null)
            {
                // Fall back to the loader when the lock cannot be acquired.
                return await loader();
            }

            // Double-check the cache after the lock has been acquired.
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

            // Load from the backing source.
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
        /// Invalidates the specified cache keys.
        /// </summary>
        public async FTask InvalidateAsync(params string[] keys)
        {
            await DeleteAsync(keys);
        }

        /// <summary>
        /// Invalidates the cache entry for a specific entity.
        /// </summary>
        public async FTask InvalidateEntityAsync<T>(long id) where T : Entity
        {
            await RemoveEntityAsync<T>(id);
        }

        /// <summary>
        /// Invalidates all cache entries for a given entity type.
        /// </summary>
        public async FTask InvalidateTypeAsync<T>() where T : class
        {
            var pattern = _keyBuilder.EntityPattern<T>();
            await DeleteByPatternAsync(pattern);
        }

        /// <summary>
        /// Warms up cache entries for the provided entity identifiers.
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
        /// Placeholder used to represent null values and reduce cache penetration.
        /// </summary>
        private sealed class NullValuePlaceholder
        {
        }

        #endregion
    }

    /// <summary>
    /// Scene extension methods that provide convenient cache helpers.
    /// </summary>
    public static class SceneCacheExtensions
    {
        /// <summary>
        /// Gets a value from cache first, then falls back to the backing source.
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
                // Fall back to the source directly when Redis is unavailable.
                return await fetchFromDb();
            }

            var cacheAside = new CacheAside(cache);
            return await cacheAside.GetOrLoadAsync(cacheKey, fetchFromDb, cacheDuration);
        }

        /// <summary>
        /// Gets an entity from cache first using the entity type as the key prefix.
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
        /// Invalidates the cache entry for a specific entity.
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
        /// Executes an operation under a distributed lock.
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
                // Execute directly when Redis is unavailable.
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
        /// Executes a function under a distributed lock and returns its result.
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
        /// Publishes a cross-server message.
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
        /// Subscribes to a cross-server message channel.
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
