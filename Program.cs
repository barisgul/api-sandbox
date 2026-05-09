using System.Text;
using AuthSandbox.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Bind SandboxCredentials from appsettings.Development.json
builder.Services.Configure<SandboxCredentials>(
    builder.Configuration.GetSection(SandboxCredentials.Section));

var sandboxCreds = builder.Configuration
    .GetSection(SandboxCredentials.Section)
    .Get<SandboxCredentials>() ?? new SandboxCredentials();

// Add services to the container.
builder.Services.AddControllers();

// Configure Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>("ApiKeyHeader", options =>
{
    options.Location = ApiKeyLocation.Header;
    options.KeyName = "X-Api-Key";
})
.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>("ApiKeyQuery", options =>
{
    options.Location = ApiKeyLocation.Query;
    options.KeyName = "api_key";
})
.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>("ApiKeyCookie", options =>
{
    options.Location = ApiKeyLocation.Cookie;
    options.KeyName = "AuthCookie";
})
.AddScheme<BasicAuthenticationOptions, BasicAuthenticationHandler>("Basic", options => { })
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = sandboxCreds.Bearer.Issuer,
        ValidAudience = sandboxCreds.Bearer.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(sandboxCreds.Bearer.JwtKey))
    };
    // Return 401 with JSON body (not a redirect) when JWT validation fails
    options.Events = new JwtBearerEvents
    {
        OnChallenge = async ctx =>
        {
            ctx.HandleResponse();
            var reason = ctx.AuthenticateFailure?.Message ?? "Missing or invalid Bearer token.";
            ctx.Response.ContentType = "application/json";
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            var body = System.Text.Json.JsonSerializer.Serialize(new
            {
                status = 401,
                error = "Unauthorized",
                scheme = "Bearer",
                reason
            }, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            await ctx.Response.WriteAsync(body);
        }
    };
});

// Configure Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1-basic", new OpenApiInfo { Title = "Basic Auth", Version = "v1" });
    c.SwaggerDoc("v1-bearer", new OpenApiInfo { Title = "Bearer Auth (JWT)", Version = "v1" });
    c.SwaggerDoc("v1-apikey-header", new OpenApiInfo { Title = "API Key (Header)", Version = "v1" });
    c.SwaggerDoc("v1-apikey-query", new OpenApiInfo { Title = "API Key (Query)", Version = "v1" });
    c.SwaggerDoc("v1-apikey-cookie", new OpenApiInfo { Title = "API Key (Cookie)", Version = "v1" });
    c.SwaggerDoc("v1-oauth2", new OpenApiInfo { Title = "OAuth 2.0", Version = "v1" });
    c.SwaggerDoc("v1-oidc", new OpenApiInfo { Title = "OpenID Connect", Version = "v1" });

    // 1. Basic Auth
    c.AddSecurityDefinition("Basic", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "basic",
        In = ParameterLocation.Header
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Basic" } }, new string[] {} }
    });

    // 2. Bearer Auth
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Call **POST /api/BearerAuth/token** first to get a JWT, then paste it here."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, new string[] {} }
    });

    // 3. API Key (Header)
    c.AddSecurityDefinition("ApiKeyHeader", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        Name = "X-Api-Key",
        In = ParameterLocation.Header,
        Description = $"Use value: **{sandboxCreds.ApiKeyHeader.Key}**. (Note: 'Authorize' only saves this locally; validation happens on 'Execute'.)"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKeyHeader" } }, new string[] {} }
    });

    // 4. API Key (Query)
    c.AddSecurityDefinition("ApiKeyQuery", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        Name = "api_key",
        In = ParameterLocation.Query,
        Description = $"Use value: **{sandboxCreds.ApiKeyQuery.Key}** — (Note: 'Authorize' only saves this locally; validation happens on 'Execute'.)"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKeyQuery" } }, new string[] {} }
    });

    // 5. API Key (Cookie)
    c.AddSecurityDefinition("ApiKeyCookie", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        Name = "AuthCookie",
        In = ParameterLocation.Cookie,
        Description = $"Use value: **{sandboxCreds.ApiKeyCookie.Key}**. (Note: 'Authorize' only saves this locally; validation happens on 'Execute'.)"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKeyCookie" } }, new string[] {} }
    });

    // 6. OAuth 2.0 – Client Credentials (shows client_id + client_secret in Swagger UI)
    c.AddSecurityDefinition("OAuth2", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.OAuth2,
        Flows = new OpenApiOAuthFlows
        {
            ClientCredentials = new OpenApiOAuthFlow
            {
                TokenUrl = new Uri(sandboxCreds.OAuth2.TokenUrl),
                Scopes = sandboxCreds.OAuth2.Scopes
            }
        },
        Description = $"Client Credentials Flow. client_id: **{sandboxCreds.OAuth2.ClientId}** / client_secret: **{sandboxCreds.OAuth2.ClientSecret}**"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "OAuth2" } }, new[] { "read_data" } }
    });

    // 7. OpenID Connect
    c.AddSecurityDefinition("OpenIDConnect", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.OpenIdConnect,
        OpenIdConnectUrl = new Uri(sandboxCreds.OpenIdConnect.DiscoveryUrl),
        Description = "OpenID Connect Discovery URL (mock — token is validated as JWT Bearer)."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "OpenIDConnect" } }, new string[] {} }
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1-basic/swagger.json", "Basic Auth");
        c.SwaggerEndpoint("/swagger/v1-bearer/swagger.json", "Bearer Auth (JWT)");
        c.SwaggerEndpoint("/swagger/v1-apikey-header/swagger.json", "API Key (Header)");
        c.SwaggerEndpoint("/swagger/v1-apikey-query/swagger.json", "API Key (Query)");
        c.SwaggerEndpoint("/swagger/v1-apikey-cookie/swagger.json", "API Key (Cookie)");
        c.SwaggerEndpoint("/swagger/v1-oauth2/swagger.json", "OAuth 2.0");
        c.SwaggerEndpoint("/swagger/v1-oidc/swagger.json", "OpenID Connect");
        c.InjectJavascript("/swagger-custom.js");
    });
}

app.UseStaticFiles();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
