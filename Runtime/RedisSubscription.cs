#if FANTASY_NET
using Fantasy;
using Fantasy.Async;
using FreeRedis;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
#pragma warning disable CS8603 // Possible null reference return.

namespace Entities.Redis
{
    /// <summary>
    /// Redis 订阅，支持发布订阅模式
    /// </summary>
    public sealed class RedisSubscription : IDisposable
    {
        private readonly RedisDatabase _redisDatabase;
        private readonly string _channel;
        private readonly Action<string, byte[]> _handler;
        private readonly IDisposable _subscription;
        private bool _isDisposed;

        /// <summary>
        /// 订阅的频道
        /// </summary>
        public string Channel => _channel;

        /// <summary>
        /// 是否已订阅
        /// </summary>
        public bool IsSubscribed { get; private set; }

        /// <summary>
        /// 创建 Redis 订阅实例
        /// </summary>
        internal RedisSubscription(RedisDatabase redisDatabase, string channel, Action<string, byte[]> handler)
        {
            _redisDatabase = redisDatabase;
            _channel = channel;
            _handler = handler;

            var redisClient = _redisDatabase.GetRedisClient();
            _subscription = redisClient.Subscribe(channel, (ch, msg) =>
            {
                if (!_isDisposed)
                {
                    try
                    {
                        _handler(ch, (byte[])msg);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Redis subscription handler error: channel={ch}, error={e.Message}");
                    }
                }
            });
        }

        /// <summary>
        /// 订阅频道（内部方法）
        /// </summary>
        internal async FTask SubscribeAsync()
        {
            if (IsSubscribed)
            {
                return;
            }

            IsSubscribed = true;
            await FTask.CompletedTask;
            Log.Debug($"Subscribed to Redis channel: {_channel}");
        }

        /// <summary>
        /// 取消订阅
        /// </summary>
        public async FTask UnsubscribeAsync()
        {
            if (_isDisposed || !IsSubscribed)
            {
                return;
            }

            try
            {
                var redisClient = _redisDatabase.GetRedisClient();

                // FreeRedis 会自动处理取消订阅
                _subscription.Dispose();

                IsSubscribed = false;
                await FTask.CompletedTask;
                Log.Debug($"Unsubscribed from Redis channel: {_channel}");
            }
            catch (Exception e)
            {
                Log.Error($"Failed to unsubscribe from Redis channel: {_channel}, error={e.Message}");
            }
        }

        /// <summary>
        /// 发布消息到当前订阅的频道
        /// </summary>
        public async FTask<long> PublishAsync(byte[] message)
        {
            if (_isDisposed)
            {
                Log.Warning($"Cannot publish to disposed Redis subscription: {_channel}");
                return 0;
            }

            try
            {
                var redisClient = _redisDatabase.GetRedisClient();
                return await redisClient.PublishAsync(_channel, message);
            }
            catch (Exception e)
            {
                Log.Error($"Failed to publish to Redis channel: {_channel}, error={e.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 发布消息到当前订阅的频道（字符串）
        /// </summary>
        public async FTask<long> PublishAsync(string message)
        {
            return await PublishAsync(System.Text.Encoding.UTF8.GetBytes(message));
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            if (IsSubscribed)
            {
                UnsubscribeAsync().Coroutine();
            }

            _subscription.Dispose();
        }
    }

    /// <summary>
    /// Redis 模式订阅（支持通配符）
    /// </summary>
    public sealed class RedisPatternSubscription : IDisposable
    {
        private readonly RedisDatabase _redisDatabase;
        private readonly string _pattern;
        // private readonly Action<string, byte[]> _handler;
        private readonly Action<string, object> _handler;
        private readonly IDisposable _subscription;
        private bool _isDisposed;

        /// <summary>
        /// 订阅的模式
        /// </summary>
        public string Pattern => _pattern;

        /// <summary>
        /// 是否已订阅
        /// </summary>
        public bool IsSubscribed { get; private set; }

        /// <summary>
        /// 创建 Redis 模式订阅实例
        /// </summary>
        internal RedisPatternSubscription(RedisDatabase redisDatabase, string pattern, Action<string, object> handler)
        {
            _redisDatabase = redisDatabase;
            _pattern = pattern;
            _handler = handler;

            var redisClient = _redisDatabase.GetRedisClient();
            _subscription = redisClient.PSubscribe(pattern, (ch, msg) =>
            {
                if (!_isDisposed)
                {
                    try
                    {
                        _handler(ch, msg);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Redis pattern subscription handler error: pattern={pattern}, channel={ch}, message={msg}, error={e.Message}");
                    }
                }
            });
        }

        /// <summary>
        /// 取消订阅
        /// </summary>
        public async FTask UnsubscribeAsync()
        {
            if (_isDisposed || !IsSubscribed)
            {
                return;
            }

            try
            {
                // FreeRedis 会自动处理取消订阅
                _subscription.Dispose();

                IsSubscribed = false;
                await FTask.CompletedTask;
                Log.Debug($"Unsubscribed from Redis pattern: {_pattern}");
            }
            catch (Exception e)
            {
                Log.Error($"Failed to unsubscribe from Redis pattern: {_pattern}, error={e.Message}");
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            if (IsSubscribed)
            {
                UnsubscribeAsync().Coroutine();
            }

            _subscription.Dispose();
        }
    }

    /// <summary>
    /// Redis 消息总线，用于跨服务器消息传递
    /// </summary>
    public sealed class RedisMessageBus : IDisposable
    {
        private readonly RedisDatabase _redisDatabase;
        private readonly string _prefix;
        private bool _isDisposed;

        /// <summary>
        /// 创建 Redis 消息总线
        /// </summary>
        public RedisMessageBus(RedisDatabase redisDatabase, string prefix = "fantasy:bus:")
        {
            _redisDatabase = redisDatabase;
            _prefix = prefix;
            _isDisposed = false;
        }

        /// <summary>
        /// 发布消息到指定主题
        /// </summary>
        public async FTask<long> PublishAsync<T>(string topic, T message) where T : class
        {
            if (_isDisposed)
            {
                Log.Warning($"Cannot publish to disposed Redis message bus: topic={topic}");
                return 0;
            }

            try
            {
                var channel = $"{_prefix}{topic}";
                var bytes = MemoryPack.MemoryPackSerializer.Serialize(message);

                var redisClient = _redisDatabase.GetRedisClient();
                return await redisClient.PublishAsync(channel, bytes);
            }
            catch (Exception e)
            {
                Log.Error($"Failed to publish to Redis message bus: topic={topic}, error={e.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 订阅指定主题
        /// </summary>
        public RedisSubscription Subscribe<T>(string topic, Action<string, T> handler) where T : class
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(RedisMessageBus));
            }

            var channel = $"{_prefix}{topic}";

            return new RedisSubscription(_redisDatabase, channel, (ch, bytes) =>
            {
                try
                {
                    var message = MemoryPack.MemoryPackSerializer.Deserialize<T>(bytes);
                    if (message != null)
                    {
                        handler(ch, message);
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"Failed to deserialize Redis message bus message: topic={topic}, error={e.Message}");
                }
            });
        }

        /// <summary>
        /// 订阅匹配模式的所有主题
        /// </summary>
        public RedisPatternSubscription SubscribePattern<T>(string pattern, Action<string, T> handler) where T : class
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(RedisMessageBus));
            }

            var patternChannel = $"{_prefix}{pattern}";

            return new RedisPatternSubscription(_redisDatabase, patternChannel, (ch, msg) =>
            {
                try
                {
                    var message = MemoryPack.MemoryPackSerializer.Deserialize<T>((byte[]) typeof (byte[]).FromObject(msg));
                    if (message != null)
                    {
                        // 移除前缀，只返回原始主题名
                        var topic = ch.StartsWith(_prefix) ? ch.Substring(_prefix.Length) : ch;
                        handler(topic, message);
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"Failed to deserialize Redis message bus message: pattern={pattern}, channel={ch}, error={e.Message}");
                }
            });
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            // FreeRedis 会自动清理订阅
        }
    }
}
#endif
