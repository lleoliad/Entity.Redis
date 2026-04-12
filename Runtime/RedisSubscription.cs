#if FANTASY_NET
using Fantasy;
using Fantasy.Async;
using FreeRedis;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
#pragma warning disable CS8603 // Possible null reference return.

namespace Entities.Redis
{
    /// <summary>
    /// Represents a Redis pub/sub subscription.
    /// </summary>
    public sealed class RedisSubscription : IDisposable
    {
        private readonly RedisDatabase _redisDatabase;
        private readonly string _channel;
        private readonly Action<string, byte[]> _handler;
        private readonly IDisposable _subscription;
        private bool _isDisposed;

        /// <summary>
        /// Gets the subscribed channel.
        /// </summary>
        public string Channel => _channel;

        /// <summary>
        /// Gets whether the subscription is active.
        /// </summary>
        public bool IsSubscribed { get; private set; }

        /// <summary>
        /// Creates a Redis subscription instance.
        /// </summary>
        internal RedisSubscription(RedisDatabase redisDatabase, string channel, Action<string, byte[]> handler)
        {
            _redisDatabase = redisDatabase;
            _channel = channel;
            _handler = handler;
            IsSubscribed = true;

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
        /// Marks the channel as subscribed.
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
        /// Cancels the subscription.
        /// </summary>
        public async FTask UnsubscribeAsync()
        {
            if (_isDisposed || !IsSubscribed)
            {
                return;
            }

            try
            {
                // FreeRedis handles the unsubscribe mechanics through disposal.
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
        /// Publishes a binary message to the current channel.
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
        /// Publishes a UTF-8 string message to the current channel.
        /// </summary>
        public async FTask<long> PublishAsync(string message)
        {
            return await PublishAsync(System.Text.Encoding.UTF8.GetBytes(message));
        }

        /// <summary>
        /// Releases the subscription resources.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            IsSubscribed = false;
            _isDisposed = true;
            _subscription.Dispose();
        }
    }

    /// <summary>
    /// Represents a Redis pattern subscription with wildcard support.
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
        /// Gets the subscribed pattern.
        /// </summary>
        public string Pattern => _pattern;

        /// <summary>
        /// Gets whether the pattern subscription is active.
        /// </summary>
        public bool IsSubscribed { get; private set; }

        /// <summary>
        /// Creates a Redis pattern subscription instance.
        /// </summary>
        internal RedisPatternSubscription(RedisDatabase redisDatabase, string pattern, Action<string, object> handler)
        {
            _redisDatabase = redisDatabase;
            _pattern = pattern;
            _handler = handler;
            IsSubscribed = true;

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
        /// Cancels the pattern subscription.
        /// </summary>
        public async FTask UnsubscribeAsync()
        {
            if (_isDisposed || !IsSubscribed)
            {
                return;
            }

            try
            {
                // FreeRedis handles the unsubscribe mechanics through disposal.
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
        /// Releases the pattern subscription resources.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            IsSubscribed = false;
            _isDisposed = true;
            _subscription.Dispose();
        }
    }

    /// <summary>
    /// Lightweight Redis message bus for cross-server communication.
    /// </summary>
    public sealed class RedisMessageBus : IDisposable
    {
        private readonly RedisDatabase _redisDatabase;
        private readonly string _prefix;
        private bool _isDisposed;

        /// <summary>
        /// Creates a Redis message bus.
        /// </summary>
        public RedisMessageBus(RedisDatabase redisDatabase, string prefix = "fantasy:bus:")
        {
            _redisDatabase = redisDatabase;
            _prefix = prefix;
            _isDisposed = false;
        }

        /// <summary>
        /// Publishes a message to the specified topic.
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
        /// Subscribes to a specific topic.
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
        /// Subscribes to all topics that match the specified pattern.
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
                        // Remove the internal prefix and expose only the logical topic name.
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
        /// Releases message bus resources.
        /// </summary>
        public void Dispose()
        {
            _isDisposed = true;
        }
    }
}
#endif
