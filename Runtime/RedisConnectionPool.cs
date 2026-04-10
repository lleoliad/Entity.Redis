#if FANTASY_NET
using System.Collections.Concurrent;
using Fantasy;
using Fantasy.Async;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
#pragma warning disable CS8603 // Possible null reference return.

namespace Entities.Redis
{
    /// <summary>
    /// Manages Redis database instances in a lightweight connection pool.
    /// </summary>
    public sealed class RedisConnectionPool : IDisposable
    {
        private readonly ConcurrentDictionary<string, RedisDatabase> _connections = new ConcurrentDictionary<string, RedisDatabase>();
        private readonly Scene _scene;
        private readonly int _maxPoolSize;
        private bool _isDisposed;

        /// <summary>
        /// Creates a Redis connection pool.
        /// </summary>
        public RedisConnectionPool(Scene scene, int maxPoolSize = 10)
        {
            _scene = scene;
            _maxPoolSize = maxPoolSize;
        }

        /// <summary>
        /// Gets an existing Redis connection or creates one if needed.
        /// </summary>
        public RedisDatabase GetConnection(string connectionString, string dbName)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(RedisConnectionPool));
            }

            var key = $"{connectionString}|{dbName}";

            if (_connections.TryGetValue(key, out var connection))
            {
                return connection;
            }

            // Warn when the configured pool size has been reached.
            if (_connections.Count >= _maxPoolSize)
            {
                Log.Warning($"Redis connection pool reached max size: {_maxPoolSize}");
            }

            var newConnection = new RedisDatabase();
            newConnection.Initialize(_scene, connectionString, dbName);

            if (_connections.TryAdd(key, newConnection))
            {
                return newConnection;
            }

            // If the add fails because of a concurrent insert, return the existing instance.
            if (_connections.TryGetValue(key, out connection))
            {
                newConnection.Dispose();
                return connection;
            }

            newConnection.Dispose();
            throw new InvalidOperationException("Failed to add connection to pool");
        }

        /// <summary>
        /// Removes a Redis connection from the pool.
        /// </summary>
        public bool RemoveConnection(string connectionString, string dbName)
        {
            var key = $"{connectionString}|{dbName}";
            return _connections.TryRemove(key, out _);
        }

        /// <summary>
        /// Returns the current number of pooled connections.
        /// </summary>
        public int GetConnectionCount()
        {
            return _connections.Count;
        }

        /// <summary>
        /// Clears all pooled connections.
        /// </summary>
        public void Clear()
        {
            foreach (var connection in _connections.Values)
            {
                // Individual FreeRedis connections do not require extra cleanup here.
            }

            _connections.Clear();
        }

        /// <summary>
        /// Releases pool resources.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            Clear();
        }

        /// <summary>
        /// Checks whether a pooled connection is healthy.
        /// </summary>
        public async FTask<bool> IsHealthyAsync(string connectionString, string dbName)
        {
            try
            {
                var connection = GetConnection(connectionString, dbName);
                // Try a PING command to verify connectivity.
                var response = await connection.GetRedisClient().PingAsync("");
                return !string.IsNullOrEmpty(response);
            }
            catch (Exception e)
            {
                Log.Error($"Redis health check failed: {e.Message}");
                return false;
            }
        }
    }
}
#endif
