#if FANTASY_NET
using Fantasy;
using Fantasy.Async;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
#pragma warning disable CS8603 // Possible null reference return.

namespace Entities.Redis
{
    /// <summary>
    /// Distributed Redis lock built on top of SET NX with expiration semantics.
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
        /// Gets whether the lock is currently held.
        /// </summary>
        public bool IsLocked => _isLocked;

        /// <summary>
        /// Gets the Redis key used for the lock.
        /// </summary>
        public string LockKey => _lockKey;

        /// <summary>
        /// Gets the unique lock token stored in Redis.
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
        /// Tries to acquire a distributed lock.
        /// </summary>
        /// <param name="redisDatabase">The Redis database instance.</param>
        /// <param name="lockKey">The Redis key used for locking.</param>
        /// <param name="expiry">The lock expiration window.</param>
        /// <param name="retryTimeout">The retry timeout. Null means no retry.</param>
        /// <returns>The acquired lock, or null if acquisition failed.</returns>
        public static async FTask<RedisDistributedLock?> AcquireAsync(
            RedisDatabase redisDatabase,
            string lockKey,
            TimeSpan expiry,
            TimeSpan? retryTimeout = null)
        {
            var @lock = new RedisDistributedLock(redisDatabase, lockKey, expiry);

            var startTime = DateTime.UtcNow;
            var retryInterval = TimeSpan.FromMilliseconds(50); // Retry interval.

            while (true)
            {
                if (await @lock.TryAcquireAsync())
                {
                    return @lock;
                }

                // Stop retrying once the timeout window is exceeded.
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
                    // Return immediately when retry is disabled.
                    await @lock.DisposeAsync();
                    return null;
                }

                // Wait briefly before the next retry attempt.
                // await FTask.Delay(retryInterval);
                // await Task.Delay(retryInterval);
                await FTask.Wait(redisDatabase.Scene, retryInterval.Microseconds);
            }
        }

        /// <summary>
        /// Attempts to acquire the lock once.
        /// </summary>
        private async FTask<bool> TryAcquireAsync()
        {
            try
            {
                var redisClient = _redisDatabase.GetRedisClient();

                // SET key value NX EX seconds
                // Use SET NX EX to implement atomic lock acquisition.
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
        /// Releases the lock if it is still owned by this instance.
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

                // Use Lua to ensure that only the lock owner can release the key.
                // Delete the key only when the stored token matches this lock instance.
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
        /// Extends the lock expiration while ownership is still valid.
        /// </summary>
        /// <param name="additionalTime">The additional lifetime to add.</param>
        /// <returns>True if the lock was extended successfully.</returns>
        public async FTask<bool> ExtendAsync(TimeSpan additionalTime)
        {
            if (!_isLocked)
            {
                return false;
            }

            try
            {
                var redisClient = _redisDatabase.GetRedisClient();

                // Use Lua to ensure that only the lock owner can extend the lock.
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
        /// Releases the lock resources.
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
        /// Keeps a lock alive by renewing it periodically.
        /// </summary>
        public sealed class LockRenewal : IDisposable
        {
            private readonly RedisDistributedLock _lock;
            private readonly TimeSpan _renewalInterval;
            private readonly CancellationTokenSource _cts;
            private bool _isDisposed;

            /// <summary>
            /// Creates a lock renewal helper.
            /// </summary>
            public LockRenewal(RedisDistributedLock @lock, TimeSpan renewalInterval)
            {
                _lock = @lock;
                _renewalInterval = renewalInterval;
                _cts = new CancellationTokenSource();

                // Start the background renewal loop.
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
            /// Stops lock renewal and releases resources.
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
        /// Creates a lock instance for use with `await using`.
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
    /// Convenience extensions for running code under a Redis distributed lock.
    /// </summary>
    public static class RedisDistributedLockExtensions
    {
        /// <summary>
        /// Executes an action within a distributed lock scope.
        /// </summary>
        /// <param name="cacheComponent">The cache component used to access Redis.</param>
        /// <param name="lockKey">The Redis key used for locking.</param>
        /// <param name="expiry">The lock expiration window.</param>
        /// <param name="action">The action executed while holding the lock.</param>
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
        /// Executes a function within a distributed lock scope and returns its result.
        /// </summary>
        /// <param name="cacheComponent">The cache component used to access Redis.</param>
        /// <param name="lockKey">The Redis key used for locking.</param>
        /// <param name="expiry">The lock expiration window.</param>
        /// <param name="func">The function executed while holding the lock.</param>
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
