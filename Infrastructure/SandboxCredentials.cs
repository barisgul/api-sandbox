namespace AuthSandbox.Infrastructure;

/// <summary>
/// Test credentials loaded from appsettings.Development.json → "SandboxCredentials".
/// Never use real secrets here — sandbox/testing only.
/// </summary>
public class SandboxCredentials
{
    public const string Section = "SandboxCredentials";

    public ApiKeyCredentials ApiKeyHeader { get; set; } = new();
    public ApiKeyCredentials ApiKeyQuery { get; set; } = new();
    public ApiKeyCredentials ApiKeyCookie { get; set; } = new();
    public BasicCredentials Basic { get; set; } = new();
    public BearerCredentials Bearer { get; set; } = new();
    public OAuth2Credentials OAuth2 { get; set; } = new();
    public OpenIdConnectCredentials OpenIdConnect { get; set; } = new();
}

public class ApiKeyCredentials
{
    public string Key { get; set; } = string.Empty;
}

public class BasicCredentials
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class BearerCredentials
{
    public string JwtKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
}

public class OAuth2Credentials
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string TokenUrl { get; set; } = string.Empty;
    public string GrantType { get; set; } = string.Empty;
    public Dictionary<string, string> Scopes { get; set; } = new();
}

public class OpenIdConnectCredentials
{
    public string DiscoveryUrl { get; set; } = string.Empty;
}
