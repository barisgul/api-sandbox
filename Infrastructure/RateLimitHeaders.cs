using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace AuthSandbox.Infrastructure;

/// <summary>
/// Fixed-window rate limit for the demo domain APIs (<c>/api/sandbox/*</c>), per client IP. Every response carries
/// <c>RateLimit-Limit</c>, <c>RateLimit-Remaining</c> and <c>RateLimit-Reset</c>; when the window is used up the API
/// answers 429 (problem+json, code RATE_LIMIT_EXCEEDED) with <c>Retry-After</c>. These are the headers the hardened
/// OpenAPI documents describe.
/// </summary>
public static class RateLimitHeaders
{
    public const string Limit = "RateLimit-Limit";
    public const string Remaining = "RateLimit-Remaining";
    public const string Reset = "RateLimit-Reset";
    public const int MaxRequests = 600;
    public const int WindowSeconds = 60;

    private sealed class Window { public long Start; public int Count; }
    private static readonly ConcurrentDictionary<string, Window> Windows = new();

    public static IApplicationBuilder UseSandboxRateLimit(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        if (!ctx.Request.Path.StartsWithSegments("/api/sandbox"))
        {
            await next();
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var key = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        int count; long start;
        var w = Windows.GetOrAdd(key, _ => new Window { Start = now });
        lock (w)
        {
            if (now - w.Start >= WindowSeconds) { w.Start = now; w.Count = 0; }
            count = ++w.Count;
            start = w.Start;
        }
        if (Windows.Count > 10_000) // drop stale windows so the map stays small
            foreach (var stale in Windows.Where(x => now - x.Value.Start >= WindowSeconds).Select(x => x.Key).ToList())
                Windows.TryRemove(stale, out _);

        var reset = (int)Math.Max(1, start + WindowSeconds - now);
        ctx.Response.Headers[Limit] = MaxRequests.ToString();
        ctx.Response.Headers[Remaining] = Math.Max(0, MaxRequests - count).ToString();
        ctx.Response.Headers[Reset] = reset.ToString();

        if (count > MaxRequests)
        {
            ctx.Response.Headers.RetryAfter = reset.ToString();
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too Many Requests",
                Detail = $"Rate limit of {MaxRequests} requests per {WindowSeconds} seconds exceeded.",
            };
            problem.Extensions["code"] = "RATE_LIMIT_EXCEEDED";
            ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await ctx.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
            return;
        }
        await next();
    });
}
