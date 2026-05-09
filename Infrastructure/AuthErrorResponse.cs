using System.Text.Json;

namespace AuthSandbox.Infrastructure;

/// <summary>
/// Writes a consistent JSON 401 error response body.
/// </summary>
public static class AuthErrorResponse
{
    private static readonly JsonSerializerOptions _options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task Write401Async(HttpResponse response, string scheme, string reason)
    {
        response.StatusCode = StatusCodes.Status401Unauthorized;
        response.ContentType = "application/json";

        var body = new
        {
            status = 401,
            error = "Unauthorized",
            scheme,
            reason
        };

        await response.WriteAsync(JsonSerializer.Serialize(body, _options));
    }
}
