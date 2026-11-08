using System.Reflection;
using AuthSandbox.Infrastructure.Demo;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AuthSandbox.Infrastructure;

/// <summary>
/// OpenAPI documents: one per auth scheme and one per demo domain. In OAuth2 mode each domain
/// document also includes the token endpoint, so a single import is enough.
/// </summary>
public static class SwaggerSetup
{
    private const string Intro = "🧪 **Quixa Test API Sandbox** — ";

    public record Doc(string Name, string Label, string Title, string Description, string? Scheme);

    public static readonly Doc[] AuthDocs =
    [
        new("v1-basic", "Basic Auth", "Quixa Test API Sandbox — Basic Auth", Intro + "Purpose-built mock target server developed to test HTTP Basic authentication credentials (`sandbox-user` / `sandbox-pass`), header forwarding, and automated error handling in Quixa workflows.", "Basic"),
        new("v1-bearer", "Bearer Auth (JWT)", "Quixa Test API Sandbox — Bearer Auth (JWT)", Intro + "Purpose-built mock target server developed to test Bearer JWT authentication, token generation (`POST /api/BearerAuth/token`), and authorization headers.", "Bearer"),
        new("v1-apikey-header", "API Key (Header)", "Quixa Test API Sandbox — API Key (Header)", Intro + "Purpose-built mock target server developed to test `X-Api-Key` request header authentication (`sandbox-api-key-header-123`).", "ApiKeyHeader"),
        new("v1-apikey-query", "API Key (Query)", "Quixa Test API Sandbox — API Key (Query)", Intro + "Purpose-built mock target server developed to test `?api_key=` URL query parameter authentication (`sandbox-api-key-query-123`).", "ApiKeyQuery"),
        new("v1-apikey-cookie", "API Key (Cookie)", "Quixa Test API Sandbox — API Key (Cookie)", Intro + "Purpose-built mock target server developed to test `AuthCookie` cookie-based authentication (`sandbox-api-key-cookie-123`).", "ApiKeyCookie"),
        new("v1-oauth2", "OAuth 2.0", "Quixa Test API Sandbox — OAuth 2.0", Intro + "Purpose-built mock target server developed to test OAuth 2.0 Client Credentials flows, client_id / client_secret exchange, and scope verification.", "OAuth2"),
        new("v1-oidc", "OpenID Connect", "Quixa Test API Sandbox — OpenID Connect", Intro + "Purpose-built mock target server developed to test OpenID Connect discovery endpoints and identity token verification.", "OpenIDConnect"),
        new("v1-healthcare", "Healthcare & Patient Care", "Quixa Test API Sandbox — Healthcare & Patient Care Pipeline", Intro + "Multi-step chainable REST API pipeline simulating patient intake, telehealth appointment holding, consultation notes, e-prescriptions, and pharmacy fulfillment.", null),
        new("v1-health", "Health", "Quixa Test API Sandbox — Health", Intro + "Health check endpoint returning operational status, service metadata, and UTC timestamp.", null),
    ];

    private const string DemoNote = "\n\nAll data is **synthetic** and held in memory. `POST …/reset` restores the deterministic seed. Errors use RFC 7807 `application/problem+json` with a stable `code`.";

    public static readonly Doc[] DomainDocs =
    [
        new("v1-banking", "Banking (SEPA / PSD2)", "Quixa Demo — Retail Banking (EU)", Intro + "Customer onboarding with BSN and postal address, KYC/CDD with multipart document upload, IBAN accounts, Verification of Payee, SEPA Credit Transfer & SEPA Instant, iDEAL-style payment requests, Berlin Group NextGenPSD2 account information and camt.053-style statements." + DemoNote, null),
        new("v1-insurance", "Insurance", "Quixa Demo — Insurance (EU)", Intro + "Quote → policy (EUR premium incl. 21% insurance premium tax, SEPA direct debit mandate) → claim with multipart attachment → assessment and payout." + DemoNote, null),
        new("v1-travel", "Travel & Booking", "Quixa Demo — Travel & Booking (EU)", Intro + "Hotel and flight search → price-locked hold → booking with guest details → payment (SEPA / card token / iDEAL) → cancellation & refund, EU261 compensation check, per-country accommodation VAT and tourist tax." + DemoNote, null),
        new("v1-ecommerce", "E-Commerce", "Quixa Demo — E-Commerce (EU)", Intro + "GTIN/EAN catalog → cart → checkout with EU VAT (OSS destination rates) → order → PostNL/DHL-style tracking → returns within the 14-day EU withdrawal period." + DemoNote, null),
    ];

    public static IEnumerable<Doc> AllDocs => DomainDocs.Concat(AuthDocs);

    public static void AddSandboxSwagger(this IServiceCollection services, SandboxCredentials creds)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            foreach (var d in AllDocs)
                c.SwaggerDoc(d.Name, new OpenApiInfo { Title = d.Title, Version = "v1", Description = d.Description });

            var domainGroups = DomainDocs.Select(d => d.Name).ToHashSet();
            c.DocInclusionPredicate((doc, api) =>
            {
                // Domain documents also carry the token endpoint; it is dropped again unless DemoAuth:Mode = OAuth2.
                if (domainGroups.Contains(doc) && ControllerName(api) == "OAuth2Token") return true;
                return api.GroupName == doc;
            });

            c.TagActionsBy(api =>
            {
                if (ResourceTags.Resolve(api) is { } demo) return [demo.Tag];
                var tags = (api.ActionDescriptor as ControllerActionDescriptor)?.ControllerTypeInfo
                    .GetCustomAttribute<Microsoft.AspNetCore.Http.TagsAttribute>()?.Tags;
                return tags is { Count: > 0 } ? tags.ToList() : [ControllerName(api) ?? "Default"];
            });
            c.OrderActionsBy(api => ResourceTags.Resolve(api)?.SortKey ?? $"zz-{api.RelativePath}");
            c.CustomSchemaIds(t => t.FullName!.Replace("AuthSandbox.Domain.", "").Replace("AuthSandbox.", "").Replace("+", "."));

            var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
            if (File.Exists(xml)) c.IncludeXmlComments(xml, includeControllerXmlComments: true);

            c.SchemaFilter<SchemaExampleFilter>();
            c.OperationFilter<DemoOperationFilter>();
            c.DocumentFilter<SecurityPerDocumentFilter>();
            AddSecuritySchemes(c, creds);
            c.DocumentFilter<ResourceTags.DocumentFilter>();   // last: replaces tags added by the XML-comments filter
        });
    }

    public static void UseSandboxSwaggerUi(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            foreach (var d in AllDocs) c.SwaggerEndpoint($"/swagger/{d.Name}/swagger.json", d.Label);
            c.InjectJavascript("/swagger-custom.js");
        });
    }

    private static string? ControllerName(ApiDescription api) =>
        (api.ActionDescriptor as ControllerActionDescriptor)?.ControllerName;

    private static void AddSecuritySchemes(SwaggerGenOptions c, SandboxCredentials creds)
    {
        c.AddSecurityDefinition("Basic", new OpenApiSecurityScheme
        {
            Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "basic", In = ParameterLocation.Header,
        });
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header,
            Description = "Call **POST /api/BearerAuth/token** first to get a JWT, then paste it here.",
        });
        c.AddSecurityDefinition("ApiKeyHeader", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey, Name = "X-Api-Key", In = ParameterLocation.Header,
            Description = $"Use value: **{creds.ApiKeyHeader.Key}**. (Note: 'Authorize' only saves this locally; validation happens on 'Execute'.)",
        });
        c.AddSecurityDefinition("ApiKeyQuery", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey, Name = "api_key", In = ParameterLocation.Query,
            Description = $"Use value: **{creds.ApiKeyQuery.Key}** — (Note: 'Authorize' only saves this locally; validation happens on 'Execute'.)",
        });
        c.AddSecurityDefinition("ApiKeyCookie", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey, Name = "AuthCookie", In = ParameterLocation.Cookie,
            Description = $"Use value: **{creds.ApiKeyCookie.Key}**. (Note: 'Authorize' only saves this locally; validation happens on 'Execute'.)",
        });
        c.AddSecurityDefinition("OAuth2", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Flows = new OpenApiOAuthFlows
            {
                ClientCredentials = new OpenApiOAuthFlow { TokenUrl = new Uri(creds.OAuth2.TokenUrl), Scopes = creds.OAuth2.Scopes },
            },
            Description = $"Client Credentials Flow. client_id: **{creds.OAuth2.ClientId}** / client_secret: **{creds.OAuth2.ClientSecret}**",
        });
        c.AddSecurityDefinition("OpenIDConnect", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OpenIdConnect, OpenIdConnectUrl = new Uri(creds.OpenIdConnect.DiscoveryUrl),
            Description = "OpenID Connect Discovery URL (mock — token is validated as JWT Bearer).",
        });
        c.AddSecurityDefinition("DemoApiKey", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey, Name = "X-Api-Key", In = ParameterLocation.Header,
            Description = "Demo domain API key (configured via `DemoAuth:ApiKey`).",
        });
    }
}

/// <summary>Applies <see cref="SchemaExampleAttribute"/> JSON examples.</summary>
public class SchemaExampleFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        var attr = context.Type.GetCustomAttribute<SchemaExampleAttribute>();
        if (attr is not null) schema.Example = OpenApiAnyFactory.CreateFromJson(attr.Json);
    }
}

/// <summary>Marks the OAuth2 token endpoint as public inside documents that require auth.</summary>
public class DemoOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType?.Name == "OAuth2TokenController")
            operation.Security = [new OpenApiSecurityRequirement()];
    }
}

/// <summary>
/// Each document only advertises the security scheme it actually uses. Domain documents follow
/// <c>DemoAuth:Mode</c> (None → no security, ApiKey → DemoApiKey, OAuth2 → client credentials).
/// </summary>
public class SecurityPerDocumentFilter(Microsoft.Extensions.Options.IOptionsMonitor<DemoAuthOptions> demoAuth) : IDocumentFilter
{
    public void Apply(OpenApiDocument doc, DocumentFilterContext context)
    {
        var docName = context.DocumentName;
        var scheme = SwaggerSetup.AuthDocs.FirstOrDefault(d => d.Name == docName)?.Scheme;
        if (scheme is null && SwaggerSetup.DomainDocs.Any(d => d.Name == docName))
        {
            var opts = demoAuth.CurrentValue;
            scheme = opts.Mode switch { DemoAuthMode.ApiKey => "DemoApiKey", DemoAuthMode.OAuth2 => "OAuth2", _ => null };
            if (opts.Mode != DemoAuthMode.OAuth2 && doc.Paths.Remove("/oauth/token"))
                doc.Tags = doc.Tags?.Where(t => t.Name != "Auth — OAuth2 token").ToList();
            if (opts.Mode == DemoAuthMode.ApiKey && doc.Components.SecuritySchemes.TryGetValue("DemoApiKey", out var s))
                s.Name = opts.ApiKeyHeader;
        }

        foreach (var key in doc.Components.SecuritySchemes.Keys.ToList())
            if (key != scheme) doc.Components.SecuritySchemes.Remove(key);

        doc.SecurityRequirements = scheme is null ? [] :
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = scheme } }] =
                    scheme == "OAuth2" ? [demoAuth.CurrentValue.ReadScope, demoAuth.CurrentValue.WriteScope] : [],
            },
        ];
    }
}
