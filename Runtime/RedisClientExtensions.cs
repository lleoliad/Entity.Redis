#if FANTASY_NET
using Fantasy.Async;
using FreeRedis;

namespace Entities.Redis;

public static class RedisClientExtensions
{
    public static async FTask<string> PingAsync(this IRedisClient redisClient, string? message = null)
    {
        var response = redisClient.Ping();
        await FTask.CompletedTask;
        return response;
    }
}
#endif