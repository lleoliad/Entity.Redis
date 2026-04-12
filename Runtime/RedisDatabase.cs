#if FANTASY_NET
using System.Linq.Expressions;
using Fantasy;
using Fantasy.Async;
using Fantasy.Database;
using Fantasy.Entitas;
using FreeRedis;

#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.

namespace Entities.Redis
{
    /// <summary>
    /// Redis-backed database implementation.
    /// Primarily intended for caching, hot data, distributed locks, and pub/sub workloads.
    /// </summary>
    public sealed partial class RedisDatabase : IDatabase
    {
        private Scene _scene;
        private IRedisClient _redisClient;
        private readonly HashSet<string> _collections = new HashSet<string>();
        private bool _isDisposed;

        /// <summary>
        /// Gets the database type exposed to the Fantasy database abstraction.
        /// </summary>
        public DatabaseType DatabaseType { get; } = DatabaseType.None;

        /// <summary>
        /// Gets the configured database name.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Gets the underlying database client instance.
        /// </summary>
        public object GetDatabaseInstance => _redisClient;

        public Scene Scene => _scene;

        /// <summary>
        /// Initializes the Redis database connection.
        /// </summary>
        public IDatabase Initialize(Scene scene, string? connectionString, string dbName)
        {
            _scene = scene;
            Name = dbName;

            try
            {
                // Parse the Redis connection string.
                // Example: localhost:6379,defaultDatabase=0,prefix=Fantasy:
                RedisClient redisClient = new RedisClient(connectionString);

                // Configure serialization for object payloads.
                redisClient.Serialize = obj => MemoryPack.MemoryPackSerializer.Serialize(obj.GetType(), obj);
                redisClient.DeserializeRaw = (bytes, type) => MemoryPack.MemoryPackSerializer.Deserialize(type, bytes);
                redisClient.Notice += (s, e) => Console.WriteLine(e.Log); //print command log

                _redisClient = redisClient;
                _isDisposed = false;

                Log.Info($"RedisDatabase initialized successfully: {dbName} @ {connectionString}");
                return this;
            }
            catch (Exception e)
            {
                Log.Error($"RedisDatabase initialization failed: {dbName} @ {connectionString}\n{e}");
                throw;
            }
        }

        /// <summary>
        /// Returns the raw Redis client for advanced operations.
        /// </summary>
        public IRedisClient GetRedisClient()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(RedisDatabase));
            }

            return _redisClient;
        }

        private static async FTask UnsupportedTask(string operation)
        {
            Log.Warning($"Redis as cache layer does not support {operation}. Use the primary database for persistence/query workloads.");
            await FTask.CompletedTask;
        }

        private static FTask<T> UnsupportedResult<T>(string operation, T fallback)
        {
            Log.Warning($"Redis as cache layer does not support {operation}. Use the primary database for persistence/query workloads.");
            return FTask<T>.FromResult(fallback);
        }

        #region Cache Operations - Basic cache operations

        /// <summary>
        /// Gets a cached value.
        /// </summary>
        public async FTask<T> GetAsync<T>(string key) where T : class
        {
            return await _redisClient.GetAsync<T>(key);

            // try
            // {
            //     var bytes = await _redisClient.GetAsync<T>(key);
            //     if (bytes == null || bytes.Length == 0)
            //     {
            //         return null;
            //     }
            //     return MemoryPackSerializer.Deserialize<T>(bytes);
            // }
            // catch (Exception e)
            // {
            //     Log.Error($"Redis GetAsync failed: key={key}, error={e.Message}");
            //     return null;
            // }
        }

        /// <summary>
        /// Stores a cached value.
        /// </summary>
        public async FTask SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class
        {
            try
            {
                // var bytes = MemoryPackSerializer.Serialize(value);
                if (expiry.HasValue)
                {
                    await _redisClient.SetExAsync(key, (int)expiry.Value.TotalSeconds, value);
                }
                else
                {
                    await _redisClient.SetAsync(key, value);
                }
            }
            catch (Exception e)
            {
                Log.Error($"Redis SetAsync failed: key={key}, error={e.Message}");
            }
        }

        /// <summary>
        /// Deletes one or more cached keys.
        /// </summary>
        public async FTask<long> DeleteAsync(params string[] keys)
        {
            try
            {
                return await _redisClient.DelAsync(keys);
            }
            catch (Exception e)
            {
                Log.Error($"Redis DeleteAsync failed: keys={string.Join(",", keys)}, error={e.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Checks whether a key exists.
        /// </summary>
        public async FTask<bool> ExistsAsync(string key)
        {
            try
            {
                return await _redisClient.ExistsAsync(key);
            }
            catch (Exception e)
            {
                Log.Error($"Redis ExistsAsync failed: key={key}, error={e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sets the expiration for a key.
        /// </summary>
        public async FTask<bool> ExpireAsync(string key, TimeSpan expire)
        {
            try
            {
                return await _redisClient.ExpireAsync(key, (int)expire.TotalSeconds);
            }
            catch (Exception e)
            {
                Log.Error($"Redis ExpireAsync failed: key={key}, error={e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the remaining TTL for a key.
        /// </summary>
        public async FTask<long> TtlAsync(string key)
        {
            try
            {
                return await _redisClient.TtlAsync(key);
            }
            catch (Exception e)
            {
                Log.Error($"Redis TtlAsync failed: key={key}, error={e.Message}");
                return -1;
            }
        }

        #endregion

        #region IDatabase Implementation - These are cache-only operations with reduced functionality

        // Redis is used here as a cache layer rather than a full persistence store.
        // The following IDatabase members therefore return defaults or no-op results.

        public FTask<long> Count<T>(string? name = null) where T : Entity
        {
            Log.Warning("Redis as cache layer does not support Count operation");
            return FTask<long>.FromResult(0L);
        }

        public FTask<long> Count<T>(Expression<Func<T, bool>> filter, string? name = null) where T : Entity
        {
            Log.Warning("Redis as cache layer does not support Count with filter operation");
            return FTask<long>.FromResult(0L);
        }

        public FTask<bool> Exist<T>(string? name = null) where T : Entity
        {
            Log.Warning("Redis as cache layer does not support Exist operation");
            return FTask<bool>.FromResult(false);
        }

        public FTask<bool> Exist<T>(Expression<Func<T, bool>> filter, string? name = null) where T : Entity
        {
            Log.Warning("Redis as cache layer does not support Exist with filter operation");
            return FTask<bool>.FromResult(false);
        }

        public FTask<T> QueryNotLock<T>(long id, bool isDeserialize = false, string? name = null) where T : Entity
        {
            Log.Warning("Redis as cache layer does not support QueryNotLock operation. Use MongoDB for persistent queries.");
            return FTask<T>.FromResult(null);
        }

        public FTask<T> Query<T>(long id, bool isDeserialize = false, string? name = null) where T : Entity
        {
            Log.Warning("Redis as cache layer does not support Query operation. Use MongoDB for persistent queries.");
            return FTask<T>.FromResult(null);
        }

        public FTask<(int count, List<T> dates)> QueryCountAndDatesByPage<T>(Expression<Func<T, bool>> filter, int pageIndex,
            int pageSize, bool isDeserialize = false, string? name = null) where T : Entity
        {
            Log.Warning("Redis as cache layer does not support QueryCountAndDatesByPage operation");
            return FTask<(int count, List<T> dates)>.FromResult((0, new List<T>()));
        }

        public FTask<(int count, List<T> dates)> QueryCountAndDatesByPage<T>(Expression<Func<T, bool>> filter, int pageIndex,
            int pageSize, string[] cols, bool isDeserialize = false, string? name = null) where T : Entity
        {
            Log.Warning("Redis as cache layer does not support QueryCountAndDatesByPage with cols operation");
            return FTask<(int count, List<T> dates)>.FromResult((0, new List<T>()));
        }

        public FTask<List<T>> QueryByPage<T>(Expression<Func<T, bool>> filter, int pageIndex, int pageSize,
            bool isDeserialize = false, string? name = null) where T : Entity
        {
            Log.Warning("Redis as cache layer does not support QueryByPage operation");
            return FTask<List<T>>.FromResult(new List<T>());
        }

        public FTask<List<T>> QueryByPage<T>(Expression<Func<T, bool>> filter, int pageIndex, int pageSize, string[] cols,
            bool isDeserialize = false, string? name = null) where T : Entity
        {
            Log.Warning("Redis as cache layer does not support QueryByPage with cols operation");
            return FTask<List<T>>.FromResult(new List<T>());
        }

        public FTask<List<T>> QueryByPageOrderBy<T>(Expression<Func<T, bool>> filter, int pageIndex, int pageSize,
            Expression<Func<T, object>> orderByExpression, bool isAsc = true, bool isDeserialize = false,
            string? name = null) where T : Entity
        {
            Log.Warning("Redis as cache layer does not support QueryByPageOrderBy operation");
            return FTask<List<T>>.FromResult(new List<T>());
        }

        public FTask<T?> First<T>(Expression<Func<T, bool>> filter, bool isDeserialize = false, string? name = null)
            where T : Entity
        {
            Log.Warning("Redis as cache layer does not support First operation");
            return FTask<T?>.FromResult(null);
        }

        public FTask<T> First<T>(string json, string[] cols, bool isDeserialize = false, string? name = null) where T : Entity
        {
            Log.Warning("Redis as cache layer does not support First with json operation");
            return FTask<T>.FromResult(null);
        }

        public FTask<List<T>> QueryOrderBy<T>(Expression<Func<T, bool>> filter, Expression<Func<T, object>> orderByExpression,
            bool isAsc = true, bool isDeserialize = false, string? name = null) where T : Entity
        {
            Log.Warning("Redis as cache layer does not support QueryOrderBy operation");
            return FTask<List<T>>.FromResult(new List<T>());
        }

        public FTask<List<T>> Query<T>(Expression<Func<T, bool>> filter, bool isDeserialize = false, string? name = null)
            where T : Entity
        {
            Log.Warning("Redis as cache layer does not support Query operation");
            return FTask<List<T>>.FromResult(new List<T>());
        }

        public FTask<List<T>> Query<T>(Expression<Func<T, bool>> filter, Expression<Func<T, object>>[] cols,
            bool isDeserialize = false, string? name = null) where T : Entity
        {
            Log.Warning("Redis as cache layer does not support Query with cols operation");
            return FTask<List<T>>.FromResult(new List<T>());
        }

        public FTask Query(long id, List<string> collectionNames, List<Entity> result, bool isDeserialize = false)
        {
            return UnsupportedTask("multi-collection Query operation");
        }

        public FTask<List<T>> QueryJson<T>(string json, bool isDeserialize = false, string? name = null) where T : Entity
        {
            Log.Warning("Redis as cache layer does not support QueryJson operation");
            return FTask<List<T>>.FromResult(new List<T>());
        }

        public FTask<List<T>> QueryJson<T>(string json, string[] cols, bool isDeserialize = false, string? name = null)
            where T : Entity
        {
            Log.Warning("Redis as cache layer does not support QueryJson with cols operation");
            return FTask<List<T>>.FromResult(new List<T>());
        }

        public FTask<List<T>> QueryJson<T>(long taskId, string json, bool isDeserialize = false, string? name = null)
            where T : Entity
        {
            Log.Warning("Redis as cache layer does not support QueryJson with taskId operation");
            return FTask<List<T>>.FromResult(new List<T>());
        }

        public FTask<List<T>> Query<T>(Expression<Func<T, bool>> filter, string[] cols, bool isDeserialize = false,
            string? name = null) where T : Entity
        {
            Log.Warning("Redis as cache layer does not support Query with cols operation");
            return FTask<List<T>>.FromResult(new List<T>());
        }

        public FTask Save<T>(T entity, string? name = null) where T : Entity
        {
            return UnsupportedTask("Save operation");
        }

        public FTask Save(long id, List<(Entity, string)> entities)
        {
            return UnsupportedTask("batch Save operation");
        }

        public FTask Save<T>(object transactionSession, T entity, string? name = null) where T : Entity
        {
            return UnsupportedTask("Save with transaction operation");
        }

        public FTask Insert<T>(T entity, string? name = null) where T : Entity, new()
        {
            return UnsupportedTask("Insert operation");
        }

        public FTask InsertBatch<T>(IEnumerable<T> list, string? name = null) where T : Entity, new()
        {
            return UnsupportedTask("InsertBatch operation");
        }

        public FTask InsertBatch<T>(object transactionSession, IEnumerable<T> list, string? name = null) where T : Entity, new()
        {
            return UnsupportedTask("InsertBatch with transaction operation");
        }

        public FTask<long> Remove<T>(object transactionSession, long id, string? name = null) where T : Entity, new()
        {
            Log.Warning("Redis as cache layer does not support Remove with transaction operation");
            return FTask<long>.FromResult(0L);
        }

        public FTask<long> Remove<T>(long id, string? name = null) where T : Entity, new()
        {
            Log.Warning("Redis as cache layer does not support Remove operation");
            return FTask<long>.FromResult(0L);
        }

        public FTask<long> Remove<T>(long coroutineLockQueueKey, object transactionSession, Expression<Func<T, bool>> filter,
            string? name = null) where T : Entity, new()
        {
            Log.Warning("Redis as cache layer does not support Remove with filter operation");
            return FTask<long>.FromResult(0L);
        }

        public FTask<long> Remove<T>(long coroutineLockQueueKey, Expression<Func<T, bool>> filter, string? name = null)
            where T : Entity, new()
        {
            Log.Warning("Redis as cache layer does not support Remove with filter operation");
            return FTask<long>.FromResult(0L);
        }

        public FTask<long> Sum<T>(Expression<Func<T, bool>> filter, Expression<Func<T, object>> sumExpression, string? name = null)
            where T : Entity
        {
            Log.Warning("Redis as cache layer does not support Sum operation");
            return FTask<long>.FromResult(0L);
        }

        public FTask CreateIndex<T>(string collection, params object[] keys) where T : Entity
        {
            return UnsupportedTask("CreateIndex operation");
        }

        public FTask CreateIndex<T>(params object[] keys) where T : Entity
        {
            return UnsupportedTask("CreateIndex operation");
        }

        public FTask CreateIndex<T>(object[] keys, object[] options) where T : Entity
        {
            return UnsupportedTask("CreateIndex operation");
        }

        public FTask CreateDB<T>() where T : Entity
        {
            return UnsupportedTask("CreateDB operation");
        }

        public FTask CreateDB(Type type)
        {
            return UnsupportedTask("CreateDB operation");
        }

        public FTask<T> QueryNotLock<T>(long id, bool isDeserialize = false, string? name = null, Scene? scene = null) where T : Entity
        {
            return UnsupportedResult<T>("QueryNotLock operation", null);
        }

        public FTask<T> Query<T>(long id, bool isDeserialize = false, string? name = null, Scene? scene = null) where T : Entity
        {
            return UnsupportedResult<T>("Query operation", null);
        }

        public FTask<(int count, List<T> dates)> QueryCountAndDatesByPage<T>(Expression<Func<T, bool>> filter, int pageIndex, int pageSize, bool isDeserialize = false, string? name = null, Scene? scene = null) where T : Entity
        {
            return UnsupportedResult("(count, dates) QueryCountAndDatesByPage operation", (0, new List<T>()));
        }

        public FTask<(int count, List<T> dates)> QueryCountAndDatesByPage<T>(Expression<Func<T, bool>> filter, int pageIndex, int pageSize, string[] cols, bool isDeserialize = false, string? name = null, Scene? scene = null) where T : Entity
        {
            return UnsupportedResult("(count, dates) QueryCountAndDatesByPage with cols operation", (0, new List<T>()));
        }

        public FTask<List<T>> QueryByPage<T>(Expression<Func<T, bool>> filter, int pageIndex, int pageSize, bool isDeserialize = false, string? name = null, Scene? scene = null) where T : Entity
        {
            return UnsupportedResult("QueryByPage operation", new List<T>());
        }

        public FTask<List<T>> QueryByPage<T>(Expression<Func<T, bool>> filter, int pageIndex, int pageSize, string[] cols, bool isDeserialize = false, string? name = null, Scene? scene = null) where T : Entity
        {
            return UnsupportedResult("QueryByPage with cols operation", new List<T>());
        }

        public FTask<List<T>> QueryByPageOrderBy<T>(Expression<Func<T, bool>> filter, int pageIndex, int pageSize, Expression<Func<T, object>> orderByExpression, bool isAsc = true, bool isDeserialize = false, string? name = null, Scene? scene = null) where T : Entity
        {
            return UnsupportedResult("QueryByPageOrderBy operation", new List<T>());
        }

        public FTask<T?> First<T>(Expression<Func<T, bool>> filter, bool isDeserialize = false, string? name = null, Scene? scene = null) where T : Entity
        {
            return UnsupportedResult<T?>("First operation", null);
        }

        public FTask<T> First<T>(string json, string[] cols, bool isDeserialize = false, string? name = null, Scene? scene = null) where T : Entity
        {
            return UnsupportedResult<T>("First with json operation", null);
        }

        public FTask<List<T>> QueryOrderBy<T>(Expression<Func<T, bool>> filter, Expression<Func<T, object>> orderByExpression, bool isAsc = true, bool isDeserialize = false, string? name = null, Scene? scene = null) where T : Entity
        {
            return UnsupportedResult("QueryOrderBy operation", new List<T>());
        }

        public FTask<List<T>> Query<T>(Expression<Func<T, bool>> filter, bool isDeserialize = false, string? name = null, Scene? scene = null) where T : Entity
        {
            return UnsupportedResult("Query operation", new List<T>());
        }

        public FTask<List<T>> Query<T>(Expression<Func<T, bool>> filter, Expression<Func<T, object>>[] cols, bool isDeserialize = false, string? name = null, Scene? scene = null) where T : Entity
        {
            return UnsupportedResult("Query with cols operation", new List<T>());
        }

        public FTask Query(long id, List<string> collectionNames, List<Entity> result, bool isDeserialize = false, Scene? scene = null)
        {
            return UnsupportedTask("multi-collection Query operation");
        }

        public FTask<List<T>> QueryJson<T>(string json, bool isDeserialize = false, string? name = null, Scene? scene = null) where T : Entity
        {
            return UnsupportedResult("QueryJson operation", new List<T>());
        }

        public FTask<List<T>> QueryJson<T>(string json, string[] cols, bool isDeserialize = false, string? name = null, Scene? scene = null) where T : Entity
        {
            return UnsupportedResult("QueryJson with cols operation", new List<T>());
        }

        public FTask<List<T>> QueryJson<T>(long taskId, string json, bool isDeserialize = false, string? name = null, Scene? scene = null) where T : Entity
        {
            return UnsupportedResult("QueryJson with taskId operation", new List<T>());
        }

        public FTask<List<T>> Query<T>(Expression<Func<T, bool>> filter, string[] cols, bool isDeserialize = false, string? name = null, Scene? scene = null) where T : Entity
        {
            return UnsupportedResult("Query with cols operation", new List<T>());
        }

        #endregion

        #region String Operations - Additional string operations

        /// <summary>
        /// Increments a numeric value.
        /// </summary>
        public async FTask<long> IncrByAsync(string key, long value = 1)
        {
            try
            {
                return await _redisClient.IncrByAsync(key, value);
            }
            catch (Exception e)
            {
                Log.Error($"Redis IncrByAsync failed: key={key}, error={e.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Decrements a numeric value.
        /// </summary>
        public async FTask<long> DecrByAsync(string key, long value = 1)
        {
            try
            {
                return await _redisClient.DecrByAsync(key, value);
            }
            catch (Exception e)
            {
                Log.Error($"Redis DecrByAsync failed: key={key}, error={e.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Appends content to a string value.
        /// </summary>
        public async FTask<long> AppendAsync(string key, string value)
        {
            try
            {
                return await _redisClient.AppendAsync(key, value);
            }
            catch (Exception e)
            {
                Log.Error($"Redis AppendAsync failed: key={key}, error={e.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Gets the length of a string value.
        /// </summary>
        public async FTask<long> StrLenAsync(string key)
        {
            try
            {
                return await _redisClient.StrLenAsync(key);
            }
            catch (Exception e)
            {
                Log.Error($"Redis StrLenAsync failed: key={key}, error={e.Message}");
                return 0;
            }
        }

        #endregion

        #region Hash Operations - Redis Hash operations

        /// <summary>
        /// Sets a Redis hash field.
        /// </summary>
        public async FTask<bool> HSetAsync<T>(string key, string field, T value) where T : class
        {
            try
            {
                // var bytes = MemoryPackSerializer.Serialize(value);
                await _redisClient.HSetAsync(key, field, value);
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"Redis HSetAsync failed: key={key}, field={field}, error={e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets a Redis hash field.
        /// </summary>
        public async FTask<T> HGetAsync<T>(string key, string field) where T : class
        {
            try
            {
                // var bytes = await _redisClient.HGetAsync(key, field);
                // if (bytes == null || bytes.Length == 0)
                // {
                //     return null;
                // }
                //
                // return MemoryPackSerializer.Deserialize<T>((byte[])typeof(byte[]).FromObject(bytes));

                var result = await _redisClient.HGetAsync<T>(key, field);
                return result;
            }
            catch (Exception e)
            {
                Log.Error($"Redis HGetAsync failed: key={key}, field={field}, error={e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Deletes one or more Redis hash fields.
        /// </summary>
        public async FTask<long> HDelAsync(string key, params string[] fields)
        {
            try
            {
                return await _redisClient.HDelAsync(key, fields);
            }
            catch (Exception e)
            {
                Log.Error($"Redis HDelAsync failed: key={key}, error={e.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Checks whether a Redis hash field exists.
        /// </summary>
        public async FTask<bool> HExistsAsync(string key, string field)
        {
            try
            {
                return await _redisClient.HExistsAsync(key, field);
            }
            catch (Exception e)
            {
                Log.Error($"Redis HExistsAsync failed: key={key}, field={field}, error={e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets all field names from a Redis hash.
        /// </summary>
        public async FTask<string[]> HKeysAsync(string key)
        {
            try
            {
                return await _redisClient.HKeysAsync(key);
            }
            catch (Exception e)
            {
                Log.Error($"Redis HKeysAsync failed: key={key}, error={e.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Gets the number of fields in a Redis hash.
        /// </summary>
        public async FTask<long> HLenAsync(string key)
        {
            try
            {
                return await _redisClient.HLenAsync(key);
            }
            catch (Exception e)
            {
                Log.Error($"Redis HLenAsync failed: key={key}, error={e.Message}");
                return 0;
            }
        }

        #endregion

        #region List Operations - Redis List operations

        /// <summary>
        /// Pushes a value to the left side of a list.
        /// </summary>
        public async FTask<long> LPushAsync<T>(string key, T value) where T : class
        {
            try
            {
                // var bytes = MemoryPackSerializer.Serialize(value);
                return await _redisClient.LPushAsync(key, value);
            }
            catch (Exception e)
            {
                Log.Error($"Redis LPushAsync failed: key={key}, error={e.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Pushes a value to the right side of a list.
        /// </summary>
        public async FTask<long> RPushAsync<T>(string key, T value) where T : class
        {
            try
            {
                // var bytes = MemoryPackSerializer.Serialize(value);
                return await _redisClient.RPushAsync(key, value);
            }
            catch (Exception e)
            {
                Log.Error($"Redis RPushAsync failed: key={key}, error={e.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Pops a value from the left side of a list.
        /// </summary>
        public async FTask<T> LPopAsync<T>(string key) where T : class
        {
            try
            {
                // var bytes = await _redisClient.LPopAsync(key);
                // if (bytes == null || bytes.Length == 0)
                // {
                //     return null;
                // }
                //
                // return MemoryPackSerializer.Deserialize<T>((byte[])typeof(byte[]).FromObject(bytes));

                return await _redisClient.LPopAsync<T>(key);
            }
            catch (Exception e)
            {
                Log.Error($"Redis LPopAsync failed: key={key}, error={e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Pops a value from the right side of a list.
        /// </summary>
        public async FTask<T> RPopAsync<T>(string key) where T : class
        {
            try
            {
                // var bytes = await _redisClient.RPopAsync(key);
                // if (bytes == null || bytes.Length == 0)
                // {
                //     return null;
                // }
                //
                // return MemoryPackSerializer.Deserialize<T>((byte[])typeof(byte[]).FromObject(bytes));

                return await _redisClient.RPopAsync<T>(key);
            }
            catch (Exception e)
            {
                Log.Error($"Redis RPopAsync failed: key={key}, error={e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the length of a list.
        /// </summary>
        public async FTask<long> LLenAsync(string key)
        {
            try
            {
                return await _redisClient.LLenAsync(key);
            }
            catch (Exception e)
            {
                Log.Error($"Redis LLenAsync failed: key={key}, error={e.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Gets a range of values from a list.
        /// </summary>
        public async FTask<List<T>> LRangeAsync<T>(string key, long start = 0, long stop = -1) where T : class
        {
            try
            {
                // var bytesList = await _redisClient.LRangeAsync(key, start, stop);
                // var result = new List<T>();
                //
                // foreach (var bytes in bytesList)
                // {
                //     if (bytes != null && bytes.Length > 0)
                //     {
                //         var item = MemoryPackSerializer.Deserialize<T>((byte[])typeof(byte[]).FromObject(bytes));
                //         if (item != null)
                //         {
                //             result.Add(item);
                //         }
                //     }
                // }
                //
                // return result;

                var result = new List<T>();
                result.AddRange(await _redisClient.LRangeAsync<T>(key, start, stop));
                return result;
            }
            catch (Exception e)
            {
                Log.Error($"Redis LRangeAsync failed: key={key}, error={e.Message}");
                return new List<T>();
            }
        }

        #endregion

        #region Set Operations - Redis Set operations

        /// <summary>
        /// Adds a member to a set.
        /// </summary>
        public async FTask<long> SAddAsync<T>(string key, T value) where T : class
        {
            try
            {
                // var bytes = MemoryPackSerializer.Serialize(value);
                return await _redisClient.SAddAsync(key, value);
            }
            catch (Exception e)
            {
                Log.Error($"Redis SAddAsync failed: key={key}, error={e.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Gets all members of a set.
        /// </summary>
        public async FTask<List<T>> SMembersAsync<T>(string key) where T : class
        {
            try
            {
                // var bytesList = await _redisClient.SMembersAsync(key);
                var result = new List<T>();

                // foreach (var bytes in bytesList)
                // {
                //     if (bytes != null && bytes.Length > 0)
                //     {
                //         var item = MemoryPackSerializer.Deserialize<T>((byte[])typeof(byte[]).FromObject(bytes));
                //         if (item != null)
                //         {
                //             result.Add(item);
                //         }
                //     }
                // }

                result.AddRange(await _redisClient.SMembersAsync<T>(key));
                return result;
            }
            catch (Exception e)
            {
                Log.Error($"Redis SMembersAsync failed: key={key}, error={e.Message}");
                return new List<T>();
            }
        }

        /// <summary>
        /// Removes a member from a set.
        /// </summary>
        public async FTask<long> SRemAsync<T>(string key, T value) where T : class
        {
            try
            {
                // var bytes = MemoryPackSerializer.Serialize(value);
                return await _redisClient.SRemAsync(key, value);
            }
            catch (Exception e)
            {
                Log.Error($"Redis SRemAsync failed: key={key}, error={e.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Checks whether a value is a set member.
        /// </summary>
        public async FTask<bool> SIsMemberAsync<T>(string key, T value) where T : class
        {
            try
            {
                // var bytes = MemoryPackSerializer.Serialize(value);
                return await _redisClient.SIsMemberAsync(key, value);
            }
            catch (Exception e)
            {
                Log.Error($"Redis SIsMemberAsync failed: key={key}, error={e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the number of members in a set.
        /// </summary>
        public async FTask<long> SCardAsync(string key)
        {
            try
            {
                return await _redisClient.SCardAsync(key);
            }
            catch (Exception e)
            {
                Log.Error($"Redis SCardAsync failed: key={key}, error={e.Message}");
                return 0;
            }
        }

        #endregion

        #region Sorted Set Operations - Redis Sorted Set operations

        /// <summary>
        /// Adds a member to a sorted set.
        /// </summary>
        public async FTask<bool> ZAddAsync<T>(string key, T value, double score) where T : class
        {
            try
            {
                var bytes = MemoryPack.MemoryPackSerializer.Serialize(value);
                await _redisClient.ZAddAsync(key, (decimal)score, System.Text.Encoding.UTF8.GetString(bytes));
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"Redis ZAddAsync failed: key={key}, error={e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets members from a sorted set range in ascending score order.
        /// </summary>
        public async FTask<List<T>> ZRangeAsync<T>(string key, long start = 0, long stop = -1) where T : class
        {
            try
            {
                var bytesList = await _redisClient.ZRangeAsync(key, start, stop);
                var result = new List<T>();

                foreach (var bytes in bytesList)
                {
                    if (bytes != null && bytes.Length > 0)
                    {
                        var item = MemoryPack.MemoryPackSerializer.Deserialize<T>((byte[])typeof(byte[]).FromObject(bytes));
                        if (item != null)
                        {
                            result.Add(item);
                        }
                    }
                }

                return result;
            }
            catch (Exception e)
            {
                Log.Error($"Redis ZRangeAsync failed: key={key}, error={e.Message}");
                return new List<T>();
            }
        }

        /// <summary>
        /// Gets members from a sorted set within a score range.
        /// </summary>
        public async FTask<List<T>> ZRangeByScoreAsync<T>(string key, double min, double max) where T : class
        {
            try
            {
                var bytesList = await _redisClient.ZRangeByScoreAsync(key, (decimal)min, (decimal)max);
                var result = new List<T>();

                foreach (var bytes in bytesList)
                {
                    if (bytes != null && bytes.Length > 0)
                    {
                        var item = MemoryPack.MemoryPackSerializer.Deserialize<T>((byte[])typeof(byte[]).FromObject(bytes));
                        if (item != null)
                        {
                            result.Add(item);
                        }
                    }
                }

                return result;
            }
            catch (Exception e)
            {
                Log.Error($"Redis ZRangeByScoreAsync failed: key={key}, error={e.Message}");
                return new List<T>();
            }
        }

        /// <summary>
        /// Removes a member from a sorted set.
        /// </summary>
        public async FTask<long> ZRemAsync<T>(string key, T value) where T : class
        {
            try
            {
                var bytes = MemoryPack.MemoryPackSerializer.Serialize(value);
                return await _redisClient.ZRemAsync(key, System.Text.Encoding.UTF8.GetString(bytes));
            }
            catch (Exception e)
            {
                Log.Error($"Redis ZRemAsync failed: key={key}, error={e.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Gets the number of members in a sorted set.
        /// </summary>
        public async FTask<long> ZCardAsync(string key)
        {
            try
            {
                return await _redisClient.ZCardAsync(key);
            }
            catch (Exception e)
            {
                Log.Error($"Redis ZCardAsync failed: key={key}, error={e.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Gets the score of a sorted-set member.
        /// </summary>
        public async FTask<double> ZScoreAsync<T>(string key, T value) where T : class
        {
            try
            {
                var bytes = MemoryPack.MemoryPackSerializer.Serialize(value);
                return (double)((await _redisClient.ZScoreAsync(key, System.Text.Encoding.UTF8.GetString(bytes)))!);
            }
            catch (Exception e)
            {
                Log.Error($"Redis ZScoreAsync failed: key={key}, error={e.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Increments the score of a sorted-set member.
        /// </summary>
        public async FTask<double> ZIncrByAsync<T>(string key, T value, double increment) where T : class
        {
            try
            {
                var bytes = MemoryPack.MemoryPackSerializer.Serialize(value);
                return (double)(await _redisClient.ZIncrByAsync(key, (decimal)increment, System.Text.Encoding.UTF8.GetString(bytes)));
            }
            catch (Exception e)
            {
                Log.Error($"Redis ZIncrByAsync failed: key={key}, error={e.Message}");
                return 0;
            }
        }

        #endregion

        #region Batch Operations

        /// <summary>
        /// Creates a lightweight Redis batch helper.
        /// </summary>
        public RedisBatch CreateBatch()
        {
            return new RedisBatch(_redisClient);
        }

        /// <summary>
        /// Executes a batch of queued Redis operations.
        /// </summary>
        public async FTask ExecuteBatchAsync(Action<RedisBatch> batchAction)
        {
            var batch = CreateBatch();
            batchAction(batch);
            await batch.ExecuteAsync();
        }

        #endregion

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            if (_redisClient is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    /// <summary>
    /// Lightweight wrapper for batched Redis operations.
    /// </summary>
    public sealed class RedisBatch
    {
        private readonly IRedisClient _redisClient;
        private readonly List<Func<Task>> _operations = new List<Func<Task>>();

        internal RedisBatch(IRedisClient redisClient)
        {
            _redisClient = redisClient;
        }

        /// <summary>
        /// Adds a get operation to the batch.
        /// </summary>
        public void Get(string key, Action<IRedisObject> callback)
        {
            _operations.Add(async () =>
            {
                var result = await _redisClient.GetAsync(key);
                callback(new RedisObject((byte[])typeof(byte[]).FromObject(result)));
            });
        }

        /// <summary>
        /// Adds a set operation to the batch.
        /// </summary>
        public void Set(string key, byte[] value, TimeSpan? expiry = null)
        {
            _operations.Add(async () =>
            {
                if (expiry.HasValue)
                {
                    await _redisClient.SetExAsync(key, (int)expiry.Value.TotalSeconds, value);
                }
                else
                {
                    await _redisClient.SetAsync(key, value);
                }
            });
        }

        /// <summary>
        /// Executes all queued operations.
        /// </summary>
        public async Task ExecuteAsync()
        {
            foreach (var operation in _operations)
            {
                await operation();
            }

            _operations.Clear();
        }
    }

    /// <summary>
    /// Wrapper around a raw Redis value payload.
    /// </summary>
    public sealed class RedisObject : IRedisObject
    {
        private readonly byte[] _bytes;

        internal RedisObject(byte[] bytes)
        {
            _bytes = bytes;
        }

        /// <summary>
        /// Returns the raw byte buffer.
        /// </summary>
        public byte[] GetBytes()
        {
            return _bytes;
        }

        /// <summary>
        /// Deserializes the payload into the requested type.
        /// </summary>
        public T Deserialize<T>() where T : class
        {
            if (_bytes == null || _bytes.Length == 0)
            {
                return null;
            }

            return MemoryPack.MemoryPackSerializer.Deserialize<T>(_bytes);
        }
    }

    /// <summary>
    /// Contract for a wrapped Redis value.
    /// </summary>
    public interface IRedisObject
    {
        /// <summary>
        /// Returns the raw byte buffer.
        /// </summary>
        byte[] GetBytes();

        /// <summary>
        /// Deserializes the payload into the requested type.
        /// </summary>
        T Deserialize<T>() where T : class;
    }
}
#endif
