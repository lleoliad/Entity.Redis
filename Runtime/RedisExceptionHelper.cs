#if FANTASY_NET
using Fantasy;
using Fantasy.Async;
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Entities.Redis
{
    /// <summary>
    /// Redis 异常处理辅助类
    /// </summary>
    public static class RedisExceptionHelper
    {
        /// <summary>
        /// 处理 Redis 异常
        /// </summary>
        public static bool HandleException(Exception? exception, string operation, string? key = null)
        {
            if (exception == null)
            {
                return false;
            }

            var errorMessage = exception.Message;
            var keyInfo = string.IsNullOrEmpty(key) ? "" : $" key={key}";

            // 根据异常类型决定如何处理
            switch (exception)
            {
                case TimeoutException:
                    Log.Warning($"Redis {operation} timeout{keyInfo}: {errorMessage}");
                    return true; // 可重试

                case ObjectDisposedException:
                    Log.Error($"Redis {operation} failed, client is disposed{keyInfo}");
                    return false; // 不可重试

                default:
                    Log.Error($"Redis {operation} failed{keyInfo}: {errorMessage}");
                    return true; // 默认可重试
            }
        }

        /// <summary>
        /// 判断是否为连接相关异常
        /// </summary>
        public static bool IsConnectionException(Exception? exception)
        {
            if (exception == null)
            {
                return false;
            }

            var message = exception.Message.ToLower();

            return message.Contains("connection") ||
                   message.Contains("connect") ||
                   message.Contains("network") ||
                   message.Contains("socket") ||
                   message.Contains("timeout");
        }

        /// <summary>
        /// 判断是否为可重试异常
        /// </summary>
        public static bool IsRetryable(Exception? exception)
        {
            if (exception == null)
            {
                return false;
            }

            return exception is TimeoutException ||
                   IsConnectionException(exception);
        }

        /// <summary>
        /// 获取友好的错误消息
        /// </summary>
        public static string GetFriendlyMessage(Exception? exception)
        {
            if (exception == null)
            {
                return "Unknown Redis error";
            }

            if (IsConnectionException(exception))
            {
                return "Redis connection error, please check if the Redis server is available";
            }

            if (exception is TimeoutException)
            {
                return "Redis operation timeout, please try again later";
            }

            return $"Redis error: {exception.Message}";
        }

        /// <summary>
        /// 安全执行 Redis 操作，捕获并记录异常
        /// </summary>
        public static async FTask<T?> SafeExecuteAsync<T>(Func<FTask<T>> operation, string operationName, string? key = null, T? defaultValue = default)
        {
            try
            {
                return await operation();
            }
            catch (Exception e)
            {
                HandleException(e, operationName, key);
                return defaultValue;
            }
        }

        /// <summary>
        /// 安全执行 Redis 操作（无返回值）
        /// </summary>
        public static async FTask SafeExecuteAsync(Func<FTask> operation, string operationName, string? key = null)
        {
            try
            {
                await operation();
            }
            catch (Exception e)
            {
                HandleException(e, operationName, key);
            }
        }

        /// <summary>
        /// 带重试的安全执行
        /// </summary>
        public static async FTask<T?> SafeExecuteWithRetryAsync<T>(
            Func<FTask<T>> operation,
            string operationName,
            int maxRetries = 3,
            string? key = null,
            T? defaultValue = default)
        {
            Exception? lastException = null;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    return await operation();
                }
                catch (Exception e)
                {
                    lastException = e;

                    if (!IsRetryable(e))
                    {
                        break;
                    }

                    if (i < maxRetries - 1)
                    {
                        var delay = TimeSpan.FromMilliseconds(100 * (i + 1));
                        // await FTask.Delay(delay);
                        await Task.Delay(delay);
                    }
                }
            }

            HandleException(lastException, operationName, key);
            return defaultValue;
        }

        /// <summary>
        /// 带重试的安全执行（无返回值）
        /// </summary>
        public static async FTask SafeExecuteWithRetryAsync(
            Func<FTask> operation,
            string operationName,
            int maxRetries = 3,
            string? key = null)
        {
            Exception? lastException = null;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    await operation();
                    return;
                }
                catch (Exception e)
                {
                    lastException = e;

                    if (!IsRetryable(e))
                    {
                        break;
                    }

                    if (i < maxRetries - 1)
                    {
                        var delay = TimeSpan.FromMilliseconds(100 * (i + 1));
                        // await FTask.Delay(delay);
                        await Task.Delay(delay);
                    }
                }
            }

            HandleException(lastException, operationName, key);
        }
    }

    /// <summary>
    /// Redis 操作结果
    /// </summary>
    public sealed class RedisResult<T>
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 结果值
        /// </summary>
        public T? Value { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// 异常对象
        /// </summary>
        public Exception? Exception { get; set; }

        /// <summary>
        /// 创建成功结果
        /// </summary>
        public static RedisResult<T> Success(T value)
        {
            return new RedisResult<T>
            {
                IsSuccess = true,
                Value = value
            };
        }

        /// <summary>
        /// 创建失败结果
        /// </summary>
        public static RedisResult<T> Failure(string error, Exception? exception = null)
        {
            return new RedisResult<T>
            {
                IsSuccess = false,
                Error = error,
                Exception = exception
            };
        }
    }

    /// <summary>
    /// Redis 健康检查结果
    /// </summary>
    public sealed class RedisHealthCheck
    {
        /// <summary>
        /// 是否健康
        /// </summary>
        public bool IsHealthy { get; set; }

        /// <summary>
        /// 响应时间（毫秒）
        /// </summary>
        public long ResponseTimeMs { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// 检查时间
        /// </summary>
        public DateTime CheckTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 创建健康结果
        /// </summary>
        public static RedisHealthCheck Healthy(long responseTimeMs)
        {
            return new RedisHealthCheck
            {
                IsHealthy = true,
                ResponseTimeMs = responseTimeMs
            };
        }

        /// <summary>
        /// 创建不健康结果
        /// </summary>
        public static RedisHealthCheck Unhealthy(string error)
        {
            return new RedisHealthCheck
            {
                IsHealthy = false,
                Error = error
            };
        }
    }
}
#endif
