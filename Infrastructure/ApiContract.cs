using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Interfaces;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AuthSandbox.Infrastructure;

/// <summary>
/// How carefully an OpenAPI document is curated. The sandbox deliberately serves documents at different
/// maturity so API governance tools (e.g. the Quixa maturity scorecard) have a realistic portfolio to show.
/// </summary>
public enum SpecProfile
{
    /// <summary>Generated as-is: no operationIds, no servers, sparse error responses (the "needs work" example).</summary>
    Uncurated,
    /// <summary>Described contract: operationIds, servers, descriptions, owner and a 4xx response per operation.</summary>
    Standard,
    /// <summary>Production-grade contract: also auth / validation / rate-limit / server errors, bounded inputs and examples.</summary>
    Hardened,
}

/// <summary>Contract-level settings (<c>OpenApi</c> section).</summary>
public class OpenApiContractOptions
{
    public const string Section = "OpenApi";
    /// <summary>Public base URL advertised in <c>servers</c> (must be https).</summary>
    public string ServerUrl { get; set; } = "https://api-sandbox-arhyeqhvhyfnhfag.northeurope-01.azurewebsites.net";
    public string ContactName { get; set; } = "Quixa Sandbox Team";
    public string ContactEmail { get; set; } = "sandbox@quixa.io";
    public string ContactUrl { get; set; } = "https://quixa.io";
    public string ChangelogUrl { get; set; } = "https://github.com/quixa-io/api-sandbox/blob/main/CHANGELOG.md";
}

internal static class ContractProfiles
{
    public static SpecProfile Of(string documentName) =>
        SwaggerSetup.AllDocs.FirstOrDefault(d => d.Name == documentName)?.Profile ?? SpecProfile.Uncurated;

    public static OpenApiResponse Problem(OperationFilterContext ctx, string description) => new()
    {
        Description = description,
        Content = new Dictionary<string, OpenApiMediaType>
        {
            ["application/problem+json"] = new() { Schema = ctx.SchemaGenerator.GenerateSchema(typeof(ProblemDetails), ctx.SchemaRepository) },
        },
    };

    public static OpenApiHeader Header(string description, int max) => new()
    {
        Description = description,
        Schema = new OpenApiSchema { Type = "integer", Format = "int32", Minimum = 0, Maximum = max },
    };
}

/// <summary>Operation-level contract: operationId, description and the documented responses for the document's profile.</summary>
public class ApiContractOperationFilter(Microsoft.Extensions.Options.IOptionsMonitor<Demo.DemoAuthOptions> demoAuth) : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var profile = ContractProfiles.Of(context.DocumentName);
        if (profile == SpecProfile.Uncurated) return;

        var action = context.MethodInfo.Name;
        operation.OperationId ??= char.ToLowerInvariant(action[0]) + action[1..];
        if (string.IsNullOrWhiteSpace(operation.Description))
            operation.Description = string.IsNullOrWhiteSpace(operation.Summary)
                ? $"{context.ApiDescription.HttpMethod} {context.ApiDescription.RelativePath}."
                : operation.Summary.TrimEnd('.') + ".";
        if (operation.RequestBody is { } body && string.IsNullOrWhiteSpace(body.Description))
        {
            var model = context.ApiDescription.ParameterDescriptions.FirstOrDefault(p => p.Source.Id is "Body" or "Form")?.Type;
            body.Description = model is { IsClass: true } && model != typeof(string) && !model.Name.StartsWith("IForm")
                ? Humanize(model.Name).Replace(" request", "") .Trim() is var n && n.Length > 0 ? char.ToUpperInvariant(n[0]) + n[1..] + " details." : "Request payload."
                : "Request payload.";
        }

        var responses = operation.Responses;
        var hasInput = operation.RequestBody != null || operation.Parameters.Count > 0;
        var secured = IsSecured(context);

        if (profile == SpecProfile.Standard)
        {
            if (!responses.Keys.Any(k => k.StartsWith('4')))
                responses[secured ? "401" : hasInput ? "400" : "429"] = ContractProfiles.Problem(context,
                    secured ? "Missing or invalid credentials." : hasInput ? "The request is invalid." : "Too many requests.");
            return;
        }

        // Hardened
        if (hasInput && !responses.ContainsKey("400") && !responses.ContainsKey("422"))
            responses["400"] = ContractProfiles.Problem(context, "The request is invalid (code VALIDATION_FAILED).");
        if (secured) responses.TryAdd("401", ContractProfiles.Problem(context, "Missing or invalid credentials."));
        var tooMany = ContractProfiles.Problem(context, "Rate limit exceeded; retry after the given number of seconds.");
        tooMany.Headers["Retry-After"] = ContractProfiles.Header("Seconds until the rate-limit window resets.", 3600);
        responses.TryAdd("429", tooMany);
        responses.TryAdd("500", ContractProfiles.Problem(context, "Unexpected server error."));

        // RateLimitHeaders middleware sets these on every /api/sandbox response, success or error
        foreach (var (_, response) in responses.Where(r => r.Key[0] is '2' or '4'))
        {
            response.Headers.TryAdd(RateLimitHeaders.Limit, ContractProfiles.Header("Requests allowed in the current window.", RateLimitHeaders.MaxRequests));
            response.Headers.TryAdd(RateLimitHeaders.Remaining, ContractProfiles.Header("Requests left in the current window.", RateLimitHeaders.MaxRequests));
            response.Headers.TryAdd(RateLimitHeaders.Reset, ContractProfiles.Header("Seconds until the current window resets.", RateLimitHeaders.WindowSeconds));
        }

        foreach (var p in operation.Parameters.Where(p => string.IsNullOrWhiteSpace(p.Description)))
            p.Description = DescribeParameter(p.Name);
    }

    private bool IsSecured(OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType?.Name == "OAuth2TokenController") return true; // client credentials are checked
        var doc = SwaggerSetup.AllDocs.First(d => d.Name == context.DocumentName);
        return doc.Scheme != null || (SwaggerSetup.DomainDocs.Contains(doc) && demoAuth.CurrentValue.Mode != Demo.DemoAuthMode.None);
    }

    private static string DescribeParameter(string name)
    {
        if (name.EndsWith("Id", StringComparison.Ordinal) && name.Length > 2)
            return $"Identifier of the {Humanize(name[..^2])}.";
        return name switch
        {
            "Consent-ID" => "Consent identifier returned by POST /psd2/v1/consents.",
            "format" => "Response format: `json` (camt.053-style) or `csv`.",
            "from" or "dateFrom" => "Start of the period (inclusive, ISO 8601 date).",
            "to" or "dateTo" => "End of the period (inclusive, ISO 8601 date).",
            _ => $"The `{name}` parameter.",
        };
    }

    private static string Humanize(string camel) =>
        string.Concat(camel.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}

/// <summary>Document-level contract: servers, owner, version and (hardened) bounded schemas.</summary>
public class ApiContractDocumentFilter(Microsoft.Extensions.Options.IOptionsMonitor<OpenApiContractOptions> options) : IDocumentFilter
{
    public void Apply(OpenApiDocument doc, DocumentFilterContext context)
    {
        var profile = ContractProfiles.Of(context.DocumentName);
        if (profile == SpecProfile.Uncurated) return;
        var o = options.CurrentValue;

        doc.Servers = [new OpenApiServer { Url = o.ServerUrl.TrimEnd('/'), Description = "Quixa Test API Sandbox" }];
        doc.Info.Version = "1.0.0";
        doc.Info.Contact = new OpenApiContact { Name = o.ContactName, Email = o.ContactEmail, Url = new Uri(o.ContactUrl) };
        if (profile != SpecProfile.Hardened) return;

        doc.Info.Version = "1.4.0";
        doc.Info.License = new OpenApiLicense { Name = "MIT", Url = new Uri("https://opensource.org/licenses/MIT") };
        doc.ExternalDocs = new OpenApiExternalDocs { Description = "Changelog", Url = new Uri(o.ChangelogUrl) };

        var visited = new HashSet<OpenApiSchema>(ReferenceEqualityComparer.Instance);
        foreach (var schema in doc.Components.Schemas.Values) Bound(schema, visited);
        foreach (var op in doc.Paths.Values.SelectMany(p => p.Operations.Values))
        {
            foreach (var p in op.Parameters) Bound(p.Schema, visited);
            if (op.RequestBody != null) foreach (var m in op.RequestBody.Content.Values) Bound(m.Schema, visited);
        }
    }

    /// <summary>Documents upper bounds for strings, integers and lists that do not declare their own (OWASP API4).</summary>
    private static void Bound(OpenApiSchema? s, HashSet<OpenApiSchema> visited)
    {
        if (s == null || s.Reference != null || !visited.Add(s)) return;
        switch (s.Type)
        {
            case "string" when s.MaxLength == null && s.Enum.Count == 0:
                s.MaxLength = s.Format switch
                {
                    "date" => 10, "date-time" => 35, "uuid" => 36, "email" => 254,
                    "binary" => (int)Demo.Uploads.DefaultMaxBytes, // upload size limit in bytes
                    _ => 500,
                };
                break;
            case "integer":
                s.Minimum ??= 0;
                s.Maximum ??= s.Format == "int64" ? 9_007_199_254_740_991 : int.MaxValue;
                break;
            case "array":
                s.MaxItems ??= 1000;
                break;
        }
        Bound(s.Items, visited);
        Bound(s.AdditionalProperties, visited);
        foreach (var p in s.Properties.Values) Bound(p, visited);
        foreach (var x in s.AllOf.Concat(s.OneOf).Concat(s.AnyOf)) Bound(x, visited);
    }
}
