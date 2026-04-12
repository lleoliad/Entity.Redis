# Entity.Redis

`Entity.Redis` extends the `Entity` server stack with Redis-based cache, distributed lock, key-building, and pub/sub capabilities.

The project is designed for Fantasy/Entity game server scenarios where Redis is used as a cache layer or a coordination component, rather than a full replacement for the primary database.

## NuGet

After the package is published, install it with:

```bash
dotnet add package Entity.Redis
```

This package targets:

- `net8.0`
- `net9.0`
- `net10.0`

## Features

- Redis cache access through `RedisDatabase`
- Scene-level cache component through `RedisCacheComponent`
- Cache-aside helper through `CacheAside`
- Distributed lock support through `RedisDistributedLock`
- Redis publish/subscribe support through `RedisSubscription`
- Key naming helper through `RedisKeyBuilder`
- Simple connection reuse through `RedisConnectionPool`

## Project Layout

```text
Entity.Redis
├── Entity.Redis.csproj
├── Runtime
│   ├── Cache
│   │   ├── CacheAside.cs
│   │   └── ICacheStore.cs
│   ├── RedisCacheComponent.cs
│   ├── RedisClientExtensions.cs
│   ├── RedisConnectionPool.cs
│   ├── RedisDatabase.cs
│   ├── RedisDistributedLock.cs
│   ├── RedisExceptionHelper.cs
│   ├── RedisKeyBuilder.cs
│   └── RedisSubscription.cs
└── README.md
```

## Requirements

- .NET 8 or later
- Fantasy/Entity runtime environment
- Redis server
- Package dependency: `FreeRedis`

## Installation

If you are developing inside the `Entity.Server.Support` repository, the project is typically added as a submodule under:

```text
Server/Support/Entity.Redis
```

Reference the project from your solution or dependent project as needed.

`Entity.Redis.csproj` already depends on:

- `FreeRedis`
- `../../Entity/Entity.csproj`

## Configuration

`RedisCacheComponent.Initialize(Scene scene)` scans the world database configuration and picks the first database entry whose `dbType` is `redis`.

Example `Fantasy.config` snippet:

```xml
<worlds>
  <world id="1" worldName="WorldA">
    <database dbType="MongoDB" dbName="fantasy_main" dbConnection="mongodb://127.0.0.1:27017/" />
    <database dbType="MongoDB" dbName="fantasy_log" dbConnection="mongodb://127.0.0.1:27017/" />
    <database dbType="Redis" dbName="fantasy_cache" dbConnection="127.0.0.1:6379,defaultDatabase=0" />
  </world>
</worlds>
```

Connection string format follows the implementation comment in `RedisDatabase.Initialize(...)`:

```text
localhost:6379,defaultDatabase=0,prefix=Fantasy:
```

## Quick Start

### Initialize the cache component

```csharp
var redisCache = await RedisCacheComponent.Initialize(scene);
if (redisCache == null)
{
    throw new Exception("Redis cache initialization failed.");
}
```

### Basic cache operations

```csharp
await redisCache.SetAsync("player:1001", playerDto, TimeSpan.FromMinutes(10));

var player = await redisCache.GetAsync<PlayerDto>("player:1001");

var exists = await redisCache.ExistsAsync("player:1001");
var ttl = await redisCache.TtlAsync("player:1001");

await redisCache.DeleteAsync("player:1001");
```

### Use cache-aside mode

```csharp
var cache = new CacheAside(redisCache, "fantasy");

var key = new RedisKeyBuilder("fantasy").Entity<PlayerDto>(1001);
var cached = await cache.GetAsync<PlayerDto>(key);

if (cached == null)
{
    cached = await LoadPlayerFromMongoAsync(1001);
    await cache.SetAsync(key, cached, TimeSpan.FromMinutes(5));
}
```

### Use distributed locks

```csharp
var redisDatabase = redisCache.RedisDatabase;
var keyBuilder = new RedisKeyBuilder("fantasy");
var lockKey = keyBuilder.Lock("matchmaking");

await using var redisLock = await RedisDistributedLock.AcquireAsync(
    redisDatabase,
    lockKey,
    TimeSpan.FromSeconds(30),
    TimeSpan.FromSeconds(3));

if (redisLock == null)
{
    return;
}

// critical section
```

### Build normalized Redis keys

```csharp
var keyBuilder = new RedisKeyBuilder("fantasy");

var entityKey = keyBuilder.Entity("Player", 1001);
var sessionKey = keyBuilder.Session(1001);
var lockKey = keyBuilder.EntityLock<PlayerDto>(1001);
var rankKey = keyBuilder.Rank("power");
```

## Main Types

### `RedisDatabase`

Core Redis wrapper implementing `IDatabase`. It exposes cache-oriented methods such as:

- `GetAsync<T>(string key)`
- `SetAsync<T>(string key, T value, TimeSpan? expiry = null)`
- `DeleteAsync(params string[] keys)`
- `ExistsAsync(string key)`
- `ExpireAsync(string key, TimeSpan expire)`
- `TtlAsync(string key)`

Important: the current implementation is cache-focused. Many full `IDatabase` persistence methods are intentionally reduced or return default values because Redis is treated as a cache layer.

Contract notes:

- `RedisDatabase` keeps `DatabaseType.None` because the upstream `DatabaseType` enum does not currently expose a dedicated Redis member.
- Unsupported `IDatabase` persistence/query members return completed tasks with empty/default results instead of partially implemented behavior.
- `Dispose()` now releases the underlying Redis client.

### `RedisCacheComponent`

Scene component that holds the active `RedisDatabase` instance and provides a higher-level API for cache, string, hash, set, sorted-set, lock, and subscription operations.

Use it when you want Redis access attached to a Fantasy `Scene`.

Lifecycle notes:

- When initialized from `DatabaseConfig`, the component owns the created `RedisDatabase` and disposes it with the component.
- All tracked subscriptions are disposed during component shutdown.

### `CacheAside`

Helper for a standard cache-aside workflow:

1. Read from Redis
2. On cache miss, read from the primary database
3. Write the loaded value back to Redis
4. Return the result

This type also tracks cache statistics when enabled.

### `RedisDistributedLock`

Distributed lock built on Redis `SET NX EX` semantics, with Lua-based safe release and extension.

Use it for:

- preventing duplicated scheduled jobs
- entity-level critical sections
- cross-process resource coordination

### `RedisSubscription`

Wrapper around Redis pub/sub for channel subscription and message publishing.

Use it for:

- cross-scene notifications
- world-to-world lightweight event fanout
- cache invalidation messages

Subscription instances are active as soon as they are created, and `Dispose()` immediately tears down the underlying FreeRedis subscription handle.

### `RedisKeyBuilder`

Provides consistent key construction and predefined patterns for:

- entity cache
- session state
- online users
- locks
- counters
- ranks
- hash/list/set keys
- pub/sub channels

## Notes

- Source files are compiled under the `FANTASY_NET` conditional symbol.
- Serialization in `RedisDatabase` uses `MemoryPack`.
- Redis is positioned as an auxiliary data store for cache and coordination; MongoDB or another primary database should still hold durable business data.
- Do not use `RedisDatabase` as a drop-in replacement for full `IDatabase` persistence/query workflows. Unsupported members intentionally no-op or return empty results.
- Do not commit production Redis passwords or private endpoints into tracked config files.

## License

This project is licensed under the terms of the [LICENSE](LICENSE).
