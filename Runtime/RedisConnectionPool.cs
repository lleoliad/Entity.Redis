#if FANTASY_NET
using System.Collections.Concurrent;
using Fantasy;
using Fantasy.Async;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
#pragma warning disable CS8603 // Possible null reference return.

namespace Entities.Redis
{
    /// <summary>
    /// Redis 连接池管理器
    /// </summary>
    public sealed class RedisConnectionPool : IDisposable
    {
        private readonly ConcurrentDictionary<string, RedisDatabase> _connections = new ConcurrentDictionary<string, RedisDatabase>();
        private readonly Scene _scene;
        private readonly int _maxPoolSize;
        private bool _isDisposed;

        /// <summary>
        /// 创建 Redis 连接池
        /// </summary>
        public RedisConnectionPool(Scene scene, int maxPoolSize = 10)
        {
            _scene = scene;
            _maxPoolSize = maxPoolSize;
        }

        /// <summary>
        /// 获取或创建 Redis 连接
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

            // 检查池大小限制
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

            // 如果添加失败（并发情况），返回现有连接
            if (_connections.TryGetValue(key, out connection))
            {
                newConnection.Dispose();
                return connection;
            }

            newConnection.Dispose();
            throw new InvalidOperationException("Failed to add connection to pool");
        }

        /// <summary>
        /// 移除连接
        /// </summary>
        public bool RemoveConnection(string connectionString, string dbName)
        {
            var key = $"{connectionString}|{dbName}";
            return _connections.TryRemove(key, out _);
        }

        /// <summary>
        /// 获取当前连接数
        /// </summary>
        public int GetConnectionCount()
        {
            return _connections.Count;
        }

        /// <summary>
        /// 清理所有连接
        /// </summary>
        public void Clear()
        {
            foreach (var connection in _connections.Values)
            {
                // 连接本身不需要特殊清理，FreeRedis 会自动管理
            }

            _connections.Clear();
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
            Clear();
        }

        /// <summary>
        /// 检查连接是否健康
        /// </summary>
        public async FTask<bool> IsHealthyAsync(string connectionString, string dbName)
        {
            try
            {
                var connection = GetConnection(connectionString, dbName);
                // 尝试执行 PING 命令
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
