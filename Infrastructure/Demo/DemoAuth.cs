using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace AuthSandbox.Infrastructure.Demo;

public enum DemoAuthMode
{
    /// <summary>Domain APIs are open (default — easiest for demos).</summary>
    None,
    /// <summary>Requires header <c>X-Api-Key: {DemoAuth:ApiKey}</c>.</summary>
    ApiKey,
    /// <summary>Requires a Bearer token from <c>POST /oauth/token</c> (client_credentials).</summary>
    OAuth2,
}

/// <summary>Configuration section <c>DemoAuth</c>. Override on Azure with <c>DemoAuth__Mode=ApiKey</c>.</summary>
public class DemoAuthOptions
{
    public const string Section = "DemoAuth";
    public DemoAuthMode Mode { get; set; } = DemoAuthMode.None;
    public string ApiKeyHeader { get; set; } = "X-Api-Key";
    public string ApiKey { get; set; } = "quixa-demo-key";
    /// <summary>OAuth2 scope required for GET/HEAD.</summary>
    public string ReadScope { get; set; } = "read";
    /// <summary>OAuth2 scope required for POST/PUT/PATCH/DELETE (also grants read).</summary>
    public string WriteScope { get; set; } = "write";
}

/// <summary>Global filter that protects every controller derived from <see cref="DemoControllerBase"/>.</summary>
public class DemoAuthFilter(IOptionsMonitor<DemoAuthOptions> options) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor cad ||
            !typeof(DemoControllerBase).IsAssignableFrom(cad.ControllerTypeInfo)) return;

        var opts = options.CurrentValue;
        switch (opts.Mode)
        {
            case DemoAuthMode.ApiKey:
                var provided = context.HttpContext.Request.Headers[opts.ApiKeyHeader].ToString();
                if (provided != opts.ApiKey)
                    context.Result = Unauthorized(context, "ApiKey", string.IsNullOrEmpty(provided)
                        ? $"Header '{opts.ApiKeyHeader}' is missing." : "API key is invalid.");
                break;
            case DemoAuthMode.OAuth2:
                var result = await context.HttpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
                if (!result.Succeeded)
                    context.Result = Unauthorized(context, "Bearer",
                        result.Failure?.Message ?? "Missing Bearer token. Obtain one from POST /oauth/token (client_credentials).");
                else
                {
                    context.HttpContext.User = result.Principal!;
                    // read for GET/HEAD, write for everything else; write also grants read.
                    var method = context.HttpContext.Request.Method;
                    var isRead = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
                    var required = isRead ? opts.ReadScope : opts.WriteScope;
                    var granted = result.Principal!.FindAll("scope").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToHashSet();
                    if (!granted.Contains(required) && !(isRead && granted.Contains(opts.WriteScope)))
                        context.Result = InsufficientScope(context, required);
                }
                break;
        }
    }

    private static ObjectResult InsufficientScope(AuthorizationFilterContext ctx, string scope)
    {
        ctx.HttpContext.Response.Headers.WWWAuthenticate = $"Bearer error=\"insufficient_scope\", scope=\"{scope}\"";
        var problem = new ProblemDetails
        {
            Status = 403,
            Title = "Forbidden",
            Detail = $"The access token lacks the '{scope}' scope. Request it via POST /oauth/token (scope={scope}).",
            Type = "https://quixa.io/problems/insufficient-scope",
        };
        problem.Extensions["code"] = "INSUFFICIENT_SCOPE";
        problem.Extensions["requiredScope"] = scope;
        return new ObjectResult(problem) { StatusCode = 403, ContentTypes = { "application/problem+json" } };
    }

    private static ObjectResult Unauthorized(AuthorizationFilterContext ctx, string scheme, string detail)
    {
        ctx.HttpContext.Response.Headers.WWWAuthenticate = scheme;
        var problem = new ProblemDetails
        {
            Status = 401,
            Title = "Unauthorized",
            Detail = detail,
            Type = "https://quixa.io/problems/unauthorized",
        };
        problem.Extensions["code"] = "UNAUTHORIZED";
        problem.Extensions["scheme"] = scheme;
        return new ObjectResult(problem) { StatusCode = 401, ContentTypes = { "application/problem+json" } };
    }
}
