#if FANTASY_NET
using System.Threading;
using Fantasy.Async;
using Fantasy.Entitas;

namespace Entities.Redis
{
    /// <summary>
    /// Defines the core cache storage operations.
    /// </summary>
    public interface ICacheStore
    {
        /// <summary>
        /// Gets a cached value.
        /// </summary>
        FTask<T> GetAsync<T>(string key) where T : class;

        /// <summary>
        /// Stores a value in the cache.
        /// </summary>
        FTask SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class;

        /// <summary>
        /// Deletes one or more cached entries.
        /// </summary>
        FTask<long> DeleteAsync(params string[] keys);

        /// <summary>
        /// Checks whether a cache key exists.
        /// </summary>
        FTask<bool> ExistsAsync(string key);

        /// <summary>
        /// Sets the expiration time for a cache key.
        /// </summary>
        FTask<bool> ExpireAsync(string key, TimeSpan expire);

        /// <summary>
        /// Gets the remaining time to live for a cache key.
        /// </summary>
        FTask<long> TtlAsync(string key);

        /// <summary>
        /// Gets multiple cached values.
        /// </summary>
        FTask<Dictionary<string, T>> GetManyAsync<T>(string[] keys) where T : class;

        /// <summary>
        /// Stores multiple cached values.
        /// </summary>
        FTask SetManyAsync<T>(Dictionary<string, T> items, TimeSpan? expiry = null) where T : class;

        /// <summary>
        /// Deletes cache entries by key pattern.
        /// </summary>
        FTask<long> DeleteByPatternAsync(string pattern);
    }

    /// <summary>
    /// Defines cache operations specialized for entity types.
    /// </summary>
    public interface IEntityCacheStore
    {
        /// <summary>
        /// Gets an entity from the cache.
        /// </summary>
        FTask<T> GetEntityAsync<T>(long id) where T : Entity;

        /// <summary>
        /// Stores an entity in the cache.
        /// </summary>
        FTask SetEntityAsync<T>(T entity, TimeSpan? expiry = null) where T : Entity;

        /// <summary>
        /// Removes an entity from the cache.
        /// </summary>
        FTask<bool> RemoveEntityAsync<T>(long id) where T : Entity;

        /// <summary>
        /// Checks whether an entity exists in the cache.
        /// </summary>
        FTask<bool> ExistsEntityAsync<T>(long id) where T : Entity;

        /// <summary>
        /// Gets an entity from cache or loads it from the source with penetration protection.
        /// </summary>
        FTask<T> GetOrLoadEntityAsync<T>(long id, Func<FTask<T>> loader, TimeSpan? cacheDuration = null) where T : Entity;
    }

    /// <summary>
    /// Cache strategy options.
    /// </summary>
    public enum CacheStrategy
    {
        /// <summary>
        /// Cache-Aside: read cache first, then load from source and populate cache on miss.
        /// </summary>
        CacheAside,

        /// <summary>
        /// Read-Through: the cache layer loads data from the backing store.
        /// </summary>
        ReadThrough,

        /// <summary>
        /// Write-Through: writes update both cache and source immediately.
        /// </summary>
        WriteThrough,

        /// <summary>
        /// Write-Behind: the cache responds immediately and flushes to the source asynchronously.
        /// </summary>
        WriteBehind
    }

    /// <summary>
    /// Cache miss behavior options.
    /// </summary>
    public enum CacheMissStrategy
    {
        /// <summary>
        /// Return null immediately.
        /// </summary>
        ReturnNull,

        /// <summary>
        /// Load the value from the backing source.
        /// </summary>
        LoadFromSource,

        /// <summary>
        /// Return a default placeholder to reduce cache penetration.
        /// </summary>
        ReturnDefault
    }

    /// <summary>
    /// Cache update behavior options.
    /// </summary>
    public enum CacheUpdateStrategy
    {
        /// <summary>
        /// Update only the cache.
        /// </summary>
        CacheOnly,

        /// <summary>
        /// Update both cache and backing source.
        /// </summary>
        CacheAndSource,

        /// <summary>
        /// Invalidate the cache and rely on lazy reload.
        /// </summary>
        Invalidate
    }

    /// <summary>
    /// Configuration options for cache behavior.
    /// </summary>
    public sealed class CacheOptions
    {
        /// <summary>
        /// Gets or sets the default cache expiration.
        /// </summary>
        public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets the cache strategy.
        /// </summary>
        public CacheStrategy Strategy { get; set; } = CacheStrategy.CacheAside;

        /// <summary>
        /// Gets or sets the cache miss strategy.
        /// </summary>
        public CacheMissStrategy MissStrategy { get; set; } = CacheMissStrategy.LoadFromSource;

        /// <summary>
        /// Gets or sets the cache update strategy.
        /// </summary>
        public CacheUpdateStrategy UpdateStrategy { get; set; } = CacheUpdateStrategy.CacheAndSource;

        /// <summary>
        /// Gets or sets whether caching is enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets whether a secondary local cache is enabled.
        /// </summary>
        public bool UseLocalCache { get; set; } = false;

        /// <summary>
        /// Gets or sets the local cache expiration.
        /// </summary>
        public TimeSpan LocalCacheExpiry { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets whether cache statistics are collected.
        /// </summary>
        public bool EnableStatistics { get; set; } = false;

        /// <summary>
        /// Gets or sets whether null values are cached to reduce cache penetration.
        /// </summary>
        public bool CacheNullValues { get; set; } = true;

        /// <summary>
        /// Gets or sets the expiration for cached null placeholders.
        /// </summary>
        public TimeSpan NullValueExpiry { get; set; } = TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// Tracks cache usage statistics with atomic counters for thread safety.
    /// </summary>
    public sealed class CacheStatistics
    {
        private long _hitCount;
        private long _missCount;
        private long _setCount;
        private long _deleteCount;

        /// <summary>
        /// Gets the cache hit count.
        /// </summary>
        public long HitCount => Interlocked.Read(ref _hitCount);

        /// <summary>
        /// Gets the cache miss count.
        /// </summary>
        public long MissCount => Interlocked.Read(ref _missCount);

        /// <summary>
        /// Gets the cache hit rate.
        /// </summary>
        public double HitRate
        {
            get
            {
                var hits = Interlocked.Read(ref _hitCount);
                var misses = Interlocked.Read(ref _missCount);
                var total = hits + misses;
                return total > 0 ? (double)hits / total : 0;
            }
        }

        /// <summary>
        /// Gets the number of cache set operations.
        /// </summary>
        public long SetCount => Interlocked.Read(ref _setCount);

        /// <summary>
        /// Gets the number of cache delete operations.
        /// </summary>
        public long DeleteCount => Interlocked.Read(ref _deleteCount);

        /// <summary>
        /// Increments the hit counter.
        /// </summary>
        public void IncrementHit() => Interlocked.Increment(ref _hitCount);

        /// <summary>
        /// Increments the miss counter.
        /// </summary>
        public void IncrementMiss() => Interlocked.Increment(ref _missCount);

        /// <summary>
        /// Increments the set counter by the specified amount.
        /// </summary>
        public void IncrementSet(long count = 1) => Interlocked.Add(ref _setCount, count);

        /// <summary>
        /// Increments the delete counter by the specified amount.
        /// </summary>
        public void IncrementDelete(long count = 1) => Interlocked.Add(ref _deleteCount, count);

        /// <summary>
        /// Resets all counters.
        /// </summary>
        public void Reset()
        {
            Interlocked.Exchange(ref _hitCount, 0);
            Interlocked.Exchange(ref _missCount, 0);
            Interlocked.Exchange(ref _setCount, 0);
            Interlocked.Exchange(ref _deleteCount, 0);
        }

        /// <summary>
        /// Returns a compact summary of the current statistics.
        /// </summary>
        public string GetSummary()
        {
            return $"Hit: {HitCount}, Miss: {MissCount}, Rate: {HitRate:P2}, Set: {SetCount}, Delete: {DeleteCount}";
        }
    }
}
#endif
