#if FANTASY_NET
using Fantasy.Async;
using Fantasy.Entitas;

namespace Entities.Redis
{
    /// <summary>
    /// 缓存存储接口
    /// </summary>
    public interface ICacheStore
    {
        /// <summary>
        /// 获取缓存数据
        /// </summary>
        FTask<T> GetAsync<T>(string key) where T : class;

        /// <summary>
        /// 设置缓存数据
        /// </summary>
        FTask SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class;

        /// <summary>
        /// 删除缓存数据
        /// </summary>
        FTask<long> DeleteAsync(params string[] keys);

        /// <summary>
        /// 检查键是否存在
        /// </summary>
        FTask<bool> ExistsAsync(string key);

        /// <summary>
        /// 设置过期时间
        /// </summary>
        FTask<bool> ExpireAsync(string key, TimeSpan expire);

        /// <summary>
        /// 获取剩余过期时间
        /// </summary>
        FTask<long> TtlAsync(string key);

        /// <summary>
        /// 批量获取
        /// </summary>
        FTask<Dictionary<string, T>> GetManyAsync<T>(string[] keys) where T : class;

        /// <summary>
        /// 批量设置
        /// </summary>
        FTask SetManyAsync<T>(Dictionary<string, T> items, TimeSpan? expiry = null) where T : class;

        /// <summary>
        /// 批量删除
        /// </summary>
        FTask<long> DeleteByPatternAsync(string pattern);
    }

    /// <summary>
    /// 实体缓存存储接口
    /// </summary>
    public interface IEntityCacheStore
    {
        /// <summary>
        /// 获取实体
        /// </summary>
        FTask<T> GetEntityAsync<T>(long id) where T : Entity;

        /// <summary>
        /// 保存实体
        /// </summary>
        FTask SetEntityAsync<T>(T entity, TimeSpan? expiry = null) where T : Entity;

        /// <summary>
        /// 删除实体
        /// </summary>
        FTask<bool> RemoveEntityAsync<T>(long id) where T : Entity;

        /// <summary>
        /// 检查实体是否存在
        /// </summary>
        FTask<bool> ExistsEntityAsync<T>(long id) where T : Entity;

        /// <summary>
        /// 获取或加载实体（缓存穿透保护）
        /// </summary>
        FTask<T> GetOrLoadEntityAsync<T>(long id, Func<FTask<T>> loader, TimeSpan? cacheDuration = null) where T : Entity;
    }

    /// <summary>
    /// 缓存策略枚举
    /// </summary>
    public enum CacheStrategy
    {
        /// <summary>
        /// Cache-Aside: 先查缓存，未命中则查数据库，然后写入缓存
        /// </summary>
        CacheAside,

        /// <summary>
        /// Read-Through: 缓存负责从数据库加载数据
        /// </summary>
        ReadThrough,

        /// <summary>
        /// Write-Through: 写入时同时更新缓存和数据库
        /// </summary>
        WriteThrough,

        /// <summary>
        /// Write-Behind: 异步写入数据库，缓存立即返回
        /// </summary>
        WriteBehind
    }

    /// <summary>
    /// 缓存未命中策略
    /// </summary>
    public enum CacheMissStrategy
    {
        /// <summary>
        /// 直接返回 null
        /// </summary>
        ReturnNull,

        /// <summary>
        /// 从数据源加载
        /// </summary>
        LoadFromSource,

        /// <summary>
        /// 返回默认值（防止缓存穿透）
        /// </summary>
        ReturnDefault
    }

    /// <summary>
    /// 缓存更新策略
    /// </summary>
    public enum CacheUpdateStrategy
    {
        /// <summary>
        /// 仅更新缓存
        /// </summary>
        CacheOnly,

        /// <summary>
        /// 更新缓存和数据源
        /// </summary>
        CacheAndSource,

        /// <summary>
        /// 删除缓存（Lazy Loading）
        /// </summary>
        Invalidate
    }

    /// <summary>
    /// 缓存配置
    /// </summary>
    public sealed class CacheOptions
    {
        /// <summary>
        /// 默认过期时间
        /// </summary>
        public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// 缓存策略
        /// </summary>
        public CacheStrategy Strategy { get; set; } = CacheStrategy.CacheAside;

        /// <summary>
        /// 缓存未命中策略
        /// </summary>
        public CacheMissStrategy MissStrategy { get; set; } = CacheMissStrategy.LoadFromSource;

        /// <summary>
        /// 缓存更新策略
        /// </summary>
        public CacheUpdateStrategy UpdateStrategy { get; set; } = CacheUpdateStrategy.CacheAndSource;

        /// <summary>
        /// 是否启用缓存
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 是否使用二级缓存（本地缓存）
        /// </summary>
        public bool UseLocalCache { get; set; } = false;

        /// <summary>
        /// 本地缓存过期时间
        /// </summary>
        public TimeSpan LocalCacheExpiry { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 是否记录缓存命中统计
        /// </summary>
        public bool EnableStatistics { get; set; } = false;

        /// <summary>
        /// 缓存穿透保护（空值缓存）
        /// </summary>
        public bool CacheNullValues { get; set; } = true;

        /// <summary>
        /// 空值缓存过期时间
        /// </summary>
        public TimeSpan NullValueExpiry { get; set; } = TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// 缓存统计信息
    /// </summary>
    public sealed class CacheStatistics
    {
        /// <summary>
        /// 缓存命中次数
        /// </summary>
        public long HitCount { get; set; }

        /// <summary>
        /// 缓存未命中次数
        /// </summary>
        public long MissCount { get; set; }

        /// <summary>
        /// 缓存命中率
        /// </summary>
        public double HitRate => HitCount + MissCount > 0 ? (double)HitCount / (HitCount + MissCount) : 0;

        /// <summary>
        /// 缓存设置次数
        /// </summary>
        public long SetCount { get; set; }

        /// <summary>
        /// 缓存删除次数
        /// </summary>
        public long DeleteCount { get; set; }

        /// <summary>
        /// 重置统计
        /// </summary>
        public void Reset()
        {
            HitCount = 0;
            MissCount = 0;
            SetCount = 0;
            DeleteCount = 0;
        }

        /// <summary>
        /// 获取统计摘要
        /// </summary>
        public string GetSummary()
        {
            return $"Hit: {HitCount}, Miss: {MissCount}, Rate: {HitRate:P2}, Set: {SetCount}, Delete: {DeleteCount}";
        }
    }
}
#endif
