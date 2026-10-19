using System.Globalization;
using System.Threading.RateLimiting;
using StackExchange.Redis;

namespace Bank.Api.Endpoints;

/// <summary>
/// Journey B4 stage 6. Rate limiting the API's callers, by the X-Client-Id header:
///   Lab:RateLimit=None (the default) | Global (one limit shared by everyone) | PerClient
///   Lab:RateLimitAlgorithm=TokenBucket (the default) | FixedWindow | SlidingWindow
///   Lab:RateLimitPerSecond (default 50) and Lab:RateLimitBurst (default 100, for the token bucket)
///   Lab:RateLimitStore=Memory (the default: each API instance counts on its own) | Redis (shared)
/// Refused requests get 429 Too Many Requests, with Retry-After.
/// </summary>
public static class RateLimits
{
    public const string ClientHeader = "X-Client-Id";

    public static IServiceCollection AddLabRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        var mode = config["Lab:RateLimit"];
        if (mode is not ("Global" or "PerClient"))
        {
            return services;
        }

        if (config["Lab:RateLimitStore"] == "Redis")
        {
            // Connect now, at startup. Connecting lazily inside the first request blocks a thread
            // while a flood of requests waits behind it: a slow start exactly when it hurts most.
            services.AddSingleton<IConnectionMultiplexer>(
                ConnectionMultiplexer.Connect(config.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false"));
            services.AddSingleton<RedisTokenBucket>();
            return services;
        }

        // #region in-memory
        var perSecond = config.GetValue("Lab:RateLimitPerSecond", 50);
        var burst = config.GetValue("Lab:RateLimitBurst", 100);
        var algorithm = config["Lab:RateLimitAlgorithm"] ?? "TokenBucket";
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }
                return default;
            };
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                // One partition for everyone, or one per client.
                var key = mode == "Global" ? "everyone" : ClientOf(context);
                return algorithm switch
                {
                    "FixedWindow" => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = perSecond, Window = TimeSpan.FromSeconds(1),
                    }),
                    "SlidingWindow" => RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = perSecond, Window = TimeSpan.FromSeconds(1), SegmentsPerWindow = 10,
                    }),
                    _ => RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = burst, TokensPerPeriod = Math.Max(1, perSecond / 10),
                        ReplenishmentPeriod = TimeSpan.FromMilliseconds(100),
                    }),
                };
            });
        });
        // #endregion in-memory
        return services;
    }

    public static WebApplication UseLabRateLimiting(this WebApplication app)
    {
        if (app.Configuration["Lab:RateLimit"] is not ("Global" or "PerClient"))
        {
            return app;
        }
        if (app.Configuration["Lab:RateLimitStore"] != "Redis")
        {
            app.UseRateLimiter();
            return app;
        }

        // #region redis-middleware
        var global = app.Configuration["Lab:RateLimit"] == "Global";
        app.Use(async (context, next) =>
        {
            var bucket = context.RequestServices.GetRequiredService<RedisTokenBucket>();
            var (allowed, retryAfter) = await bucket.TryTakeAsync(global ? "everyone" : ClientOf(context));
            if (!allowed)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                return;
            }
            await next(context);
        });
        // #endregion redis-middleware
        return app;
    }

    private static string ClientOf(HttpContext context) =>
        context.Request.Headers[ClientHeader].ToString() is { Length: > 0 } client ? client : "anonymous";
}

/// <summary>
/// B4 stage 6: a token bucket kept in Redis, so every API instance shares one limit per client.
/// The whole check runs as one Lua script, which Redis executes atomically, using Redis's own
/// clock so instances with different clocks agree.
/// </summary>
public sealed class RedisTokenBucket(IConnectionMultiplexer redis, IConfiguration config, ILogger<RedisTokenBucket> log)
{
    // #region token-bucket-script
    private const string Script = """
        local rate = tonumber(ARGV[1])      -- tokens added per second
        local capacity = tonumber(ARGV[2])  -- the most tokens the bucket holds: the burst
        local time = redis.call('TIME')
        local now = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)
        local state = redis.call('HMGET', KEYS[1], 'tokens', 'at')
        local tokens = tonumber(state[1]) or capacity
        local at = tonumber(state[2]) or now
        tokens = math.min(capacity, tokens + (now - at) * rate / 1000)
        local allowed, wait = 0, 0
        if tokens >= 1 then
            tokens = tokens - 1
            allowed = 1
        else
            wait = math.ceil((1 - tokens) * 1000 / rate)
        end
        redis.call('HSET', KEYS[1], 'tokens', tokens, 'at', now)
        redis.call('PEXPIRE', KEYS[1], math.ceil(capacity * 1000 / rate) + 1000)
        return { allowed, wait }
        """;
    // #endregion token-bucket-script

    private readonly int _perSecond = config.GetValue("Lab:RateLimitPerSecond", 50);
    private readonly int _burst = config.GetValue("Lab:RateLimitBurst", 100);

    public async Task<(bool Allowed, TimeSpan RetryAfter)> TryTakeAsync(string client)
    {
        try
        {
            var result = (RedisResult[])(await redis.GetDatabase().ScriptEvaluateAsync(Script, [new RedisKey($"rate:{client}")], [_perSecond, _burst]))!;
            return ((int)result[0] == 1, TimeSpan.FromMilliseconds((int)result[1]));
        }
        catch (RedisException e)
        {
            // If Redis is unavailable, allow the request: a rate limiter mustn't take the API down.
            log.LogWarning(e, "Rate limiter unavailable; allowing the request");
            return (true, TimeSpan.Zero);
        }
    }
}
