#if FANTASY_NET
using Fantasy;
using Fantasy.Async;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
#pragma warning disable CS8603 // Possible null reference return.

namespace Entities.Redis
{
    /// <summary>
    /// Redis 分布式锁，使用 SET resource_name unique_value NX PX 30000 实现
    /// </summary>
    public sealed class RedisDistributedLock : IAsyncDisposable
    {
        private readonly RedisDatabase _redisDatabase;
        private readonly string _lockKey;
        private readonly string _lockValue;
        private readonly TimeSpan _expiry;
        private bool _isLocked;
        private bool _isDisposed;

        /// <summary>
        /// 是否已获取锁
        /// </summary>
        public bool IsLocked => _isLocked;

        /// <summary>
        /// 锁的键
        /// </summary>
        public string LockKey => _lockKey;

        /// <summary>
        /// 锁的值（唯一标识）
        /// </summary>
        public string LockValue => _lockValue;

        private RedisDistributedLock(RedisDatabase redisDatabase, string lockKey, TimeSpan expiry)
        {
            _redisDatabase = redisDatabase;
            _lockKey = lockKey;
            _lockValue = Guid.NewGuid().ToString();
            _expiry = expiry;
        }

        /// <summary>
        /// 尝试获取分布式锁
        /// </summary>
        /// <param name="redisDatabase">Redis 数据库实例</param>
        /// <param name="lockKey">锁的键名</param>
        /// <param name="expiry">锁的过期时间</param>
        /// <param name="retryTimeout">重试超时时间，null 表示不重试</param>
        /// <returns>锁实例，如果获取失败则返回 null</returns>
        public static async FTask<RedisDistributedLock?> AcquireAsync(
            RedisDatabase redisDatabase,
            string lockKey,
            TimeSpan expiry,
            TimeSpan? retryTimeout = null)
        {
            var @lock = new RedisDistributedLock(redisDatabase, lockKey, expiry);

            var startTime = DateTime.UtcNow;
            var retryInterval = TimeSpan.FromMilliseconds(50); // 重试间隔

            while (true)
            {
                if (await @lock.TryAcquireAsync())
                {
                    return @lock;
                }

                // 检查是否超过重试超时
                if (retryTimeout.HasValue)
                {
                    var elapsed = DateTime.UtcNow - startTime;
                    if (elapsed >= retryTimeout.Value)
                    {
                        Log.Warning($"Failed to acquire Redis lock: {lockKey} after {elapsed.TotalSeconds:F2}s");
                        await @lock.DisposeAsync();
                        return null;
                    }
                }
                else
                {
                    // 不重试，直接返回
                    await @lock.DisposeAsync();
                    return null;
                }

                // 等待一段时间后重试
                // await FTask.Delay(retryInterval);
                // await Task.Delay(retryInterval);
                await FTask.Wait(redisDatabase.Scene, retryInterval.Microseconds);
            }
        }

        /// <summary>
        /// 尝试获取锁（单次尝试）
        /// </summary>
        private async FTask<bool> TryAcquireAsync()
        {
            try
            {
                var redisClient = _redisDatabase.GetRedisClient();

                // SET key value NX EX seconds
                // 使用 SET NX EX 命令实现分布式锁
                var result = await redisClient.SetNxAsync(_lockKey, _lockValue, (int)_expiry.TotalSeconds);

                if (result)
                {
                    _isLocked = true;
                    Log.Debug($"Acquired Redis lock: {_lockKey} with value: {_lockValue}");
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                Log.Error($"Failed to acquire Redis lock: {_lockKey}, error: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 释放锁
        /// </summary>
        public async FTask ReleaseAsync()
        {
            if (!_isLocked)
            {
                return;
            }

            try
            {
                var redisClient = _redisDatabase.GetRedisClient();

                // 使用 Lua 脚本确保只有锁的持有者才能释放锁
                // 如果锁的值匹配，则删除；否则不删除
                const string luaScript = @"
                    if redis.call('get', KEYS[1]) == ARGV[1] then
                        return redis.call('del', KEYS[1])
                    else
                        return 0
                    end";

                var result = await redisClient.EvalAsync(luaScript, new[] { _lockKey }, _lockValue);

                if ((int)result > 0)
                {
                    _isLocked = false;
                    Log.Debug($"Released Redis lock: {_lockKey}");
                }
                else
                {
                    Log.Warning($"Failed to release Redis lock: {_lockKey}, lock may have been acquired by another process or expired");
                }
            }
            catch (Exception e)
            {
                Log.Error($"Failed to release Redis lock: {_lockKey}, error: {e.Message}");
            }
        }

        /// <summary>
        /// 延长锁的过期时间
        /// </summary>
        /// <param name="additionalTime">延长的时长</param>
        /// <returns>是否延长成功</returns>
        public async FTask<bool> ExtendAsync(TimeSpan additionalTime)
        {
            if (!_isLocked)
            {
                return false;
            }

            try
            {
                var redisClient = _redisDatabase.GetRedisClient();

                // 使用 Lua 脚本确保只有锁的持有者才能延长锁
                const string luaScript = @"
                    if redis.call('get', KEYS[1]) == ARGV[1] then
                        return redis.call('expire', KEYS[1], ARGV[2])
                    else
                        return 0
                    end";

                var result = await redisClient.EvalAsync(luaScript, new[] { _lockKey }, _lockValue, (int)additionalTime.TotalSeconds);

                if ((int)result > 0)
                {
                    Log.Debug($"Extended Redis lock: {_lockKey} by {additionalTime.TotalSeconds}s");
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                Log.Error($"Failed to extend Redis lock: {_lockKey}, error: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            if (_isLocked)
            {
                await ReleaseAsync();
            }
        }

        /// <summary>
        /// 锁的自动续期
        /// </summary>
        public sealed class LockRenewal : IDisposable
        {
            private readonly RedisDistributedLock _lock;
            private readonly TimeSpan _renewalInterval;
            private readonly CancellationTokenSource _cts;
            private bool _isDisposed;

            /// <summary>
            /// 创建锁续期实例
            /// </summary>
            public LockRenewal(RedisDistributedLock @lock, TimeSpan renewalInterval)
            {
                _lock = @lock;
                _renewalInterval = renewalInterval;
                _cts = new CancellationTokenSource();

                // 启动续期任务
                Task.Run(() => RenewalLoop(_cts.Token));
            }

            private async Task RenewalLoop(CancellationToken cancellationToken)
            {
                while (!cancellationToken.IsCancellationRequested && _lock._isLocked)
                {
                    try
                    {
                        await Task.Delay(_renewalInterval, cancellationToken);

                        if (!cancellationToken.IsCancellationRequested && _lock._isLocked)
                        {
                            await _lock.ExtendAsync(_renewalInterval.Add(TimeSpan.FromSeconds(10)));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Lock renewal error: {_lock._lockKey}, error: {e.Message}");
                    }
                }
            }

            /// <summary>
            /// 停止续期并释放资源
            /// </summary>
            public void Dispose()
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                _cts.Cancel();
                _cts.Dispose();
            }
        }

        /// <summary>
        /// 使用 await using 语法自动释放锁
        /// </summary>
        public static async FTask<RedisDistributedLock?> CreateAsync(
            RedisDatabase redisDatabase,
            string lockKey,
            TimeSpan expiry,
            TimeSpan? retryTimeout = null)
        {
            return await AcquireAsync(redisDatabase, lockKey, expiry, retryTimeout);
        }
    }

    /// <summary>
    /// Redis 分布式锁扩展方法
    /// </summary>
    public static class RedisDistributedLockExtensions
    {
        /// <summary>
        /// 在 using 块中使用分布式锁
        /// </summary>
        /// <param name="cacheComponent">缓存组件</param>
        /// <param name="lockKey">锁的键名</param>
        /// <param name="expiry">锁的过期时间</param>
        /// <param name="action">锁内的操作</param>
        public static async FTask WithLockAsync(
            this RedisCacheComponent cacheComponent,
            string lockKey,
            TimeSpan expiry,
            Func<FTask> action)
        {
            await using var @lock = await cacheComponent.AcquireLockAsync(lockKey, expiry);

            if (@lock == null)
            {
                throw new InvalidOperationException($"Failed to acquire lock: {lockKey}");
            }

            await action();
        }

        /// <summary>
        /// 在 using 块中使用分布式锁（带返回值）
        /// </summary>
        /// <param name="cacheComponent">缓存组件</param>
        /// <param name="lockKey">锁的键名</param>
        /// <param name="expiry">锁的过期时间</param>
        /// <param name="func">锁内的操作</param>
        public static async FTask<T> WithLockAsync<T>(
            this RedisCacheComponent cacheComponent,
            string lockKey,
            TimeSpan expiry,
            Func<FTask<T>> func)
        {
            await using var @lock = await cacheComponent.AcquireLockAsync(lockKey, expiry);

            if (@lock == null)
            {
                throw new InvalidOperationException($"Failed to acquire lock: {lockKey}");
            }

            return await func();
        }
    }
}
#endif
