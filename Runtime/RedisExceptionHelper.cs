#if FANTASY_NET
using Fantasy;
using Fantasy.Async;
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Entities.Redis
{
    /// <summary>
    /// Helper methods for handling Redis-related exceptions.
    /// </summary>
    public static class RedisExceptionHelper
    {
        /// <summary>
        /// Handles a Redis exception and returns whether the operation may be retried.
        /// </summary>
        public static bool HandleException(Exception? exception, string operation, string? key = null)
        {
            if (exception == null)
            {
                return false;
            }

            var errorMessage = exception.Message;
            var keyInfo = string.IsNullOrEmpty(key) ? "" : $" key={key}";

            // Decide how to handle the error based on the exception category.
            switch (exception)
            {
                case TimeoutException:
                    Log.Warning($"Redis {operation} timeout{keyInfo}: {errorMessage}");
                    return true; // Retry is allowed.

                case ObjectDisposedException:
                    Log.Error($"Redis {operation} failed, client is disposed{keyInfo}");
                    return false; // Retry is not allowed.

                default:
                    Log.Error($"Redis {operation} failed{keyInfo}: {errorMessage}");
                    return true; // Retry by default.
            }
        }

        /// <summary>
        /// Determines whether the exception is related to connectivity.
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
        /// Determines whether the exception is safe to retry.
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
        /// Returns a user-friendly Redis error message.
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
        /// Executes a Redis operation safely and logs any exception.
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
        /// Executes a Redis operation safely when no return value is required.
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
        /// Executes a Redis operation safely with retry support.
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
        /// Executes a Redis operation safely with retry support and no return value.
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
    /// Result wrapper for Redis operations.
    /// </summary>
    public sealed class RedisResult<T>
    {
        /// <summary>
        /// Gets or sets whether the operation completed successfully.
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// Gets or sets the operation result value.
        /// </summary>
        public T? Value { get; set; }

        /// <summary>
        /// Gets or sets the error message.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Gets or sets the captured exception instance.
        /// </summary>
        public Exception? Exception { get; set; }

        /// <summary>
        /// Creates a successful result.
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
        /// Creates a failed result.
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
    /// Result model for a Redis health check.
    /// </summary>
    public sealed class RedisHealthCheck
    {
        /// <summary>
        /// Gets or sets whether Redis is healthy.
        /// </summary>
        public bool IsHealthy { get; set; }

        /// <summary>
        /// Gets or sets the response time in milliseconds.
        /// </summary>
        public long ResponseTimeMs { get; set; }

        /// <summary>
        /// Gets or sets the health check error message.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Gets or sets when the health check was performed.
        /// </summary>
        public DateTime CheckTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Creates a healthy result.
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
        /// Creates an unhealthy result.
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
