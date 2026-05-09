# API Auth Sandbox

A .NET 9 sandbox API for exploring and testing different HTTP authentication schemes via an interactive Swagger UI. Validate credentials directly in the Authorize dialog — wrong credentials are rejected with an inline error before they're saved, just like OAuth2.

---

## Authentication Schemes

| Scheme | Type | Credential |
|---|---|---|
| Basic | HTTP Basic | `sandbox-user` / `sandbox-pass` |
| Bearer | JWT | Call `POST /api/BearerAuth/token` to generate a token |
| API Key (Header) | `X-Api-Key` header | `sandbox-api-key-header-123` |
| API Key (Query) | `?api_key=` query param | `sandbox-api-key-query-123` |
| API Key (Cookie) | `AuthCookie` cookie | `sandbox-api-key-cookie-123` |
| OAuth2 | Client Credentials | `sandbox-client-id` / `sandbox-client-secret` |
| OpenID Connect | Discovery (mock) | Token validated as JWT Bearer |

---

## Getting Started

### Prerequisites
- [.NET 9 SDK](https://dotnet.microsoft.com/download)

### Run

```bash
git clone https://github.com/barisgul/api-sandbox.git
cd api-sandbox
cp appsettings.json appsettings.Development.json   # add your SandboxCredentials block
dotnet run
```

Open Swagger UI at: [http://localhost:5255/swagger](http://localhost:5255/swagger)

### Configuration

All credentials are stored in `appsettings.Development.json` under `SandboxCredentials` (excluded from source control). Example:

```json
{
  "SandboxCredentials": {
    "Basic": {
      "Username": "sandbox-user",
      "Password": "sandbox-pass"
    },
    "Bearer": {
      "JwtKey": "ThisIsASuperSecretKeyForSandbox123456789!!",
      "Issuer": "SandboxIssuer",
      "Audience": "SandboxAudience"
    },
    "ApiKeyHeader": { "Key": "sandbox-api-key-header-123" },
    "ApiKeyQuery":  { "Key": "sandbox-api-key-query-123" },
    "ApiKeyCookie": { "Key": "sandbox-api-key-cookie-123" },
    "OAuth2": {
      "ClientId": "sandbox-client-id",
      "ClientSecret": "sandbox-client-secret",
      "TokenUrl": "http://localhost:5255/oauth/token",
      "GrantType": "client_credentials",
      "Scopes": {
        "read_data": "Read access to secure data",
        "write_data": "Write access to secure data"
      }
    }
  }
}
```

---

## How It Works

### Swagger UI Validation
`wwwroot/swagger-custom.js` intercepts the Authorize button click via a global capture-phase listener. Before Swagger saves the credentials it:
1. POSTs them to a hidden `/api/validate/*` endpoint
2. If validation fails → clicks Logout to clear the saved credentials → injects an inline error (matching OAuth2's native error style)
3. If validation passes → credentials are saved normally and the padlock turns green

### OAuth2
Swagger UI calls the token endpoint (`POST /oauth/token`) natively — no custom JS needed. Wrong credentials return a standard OAuth2 error response.

### Bearer
Generate a token first via Swagger:
1. Switch to the **Bearer Auth** definition
2. Call `POST /api/BearerAuth/token`
3. Copy the `access_token` value and paste it into the Authorize dialog

---

## Project Structure

```
Controllers/
  BasicAuthController.cs          # Protected endpoint (Basic)
  BearerAuthController.cs         # Protected endpoint + token generator (JWT)
  ApiKeyHeaderAuthController.cs   # Protected endpoint (API Key via header)
  ApiKeyQueryAuthController.cs    # Protected endpoint (API Key via query)
  ApiKeyCookieAuthController.cs   # Protected endpoint (API Key via cookie)
  OAuth2AuthController.cs         # Protected endpoint (OAuth2)
  OAuth2TokenController.cs        # Token endpoint (client_credentials)
  OidcAuthController.cs           # Protected endpoint (OIDC/JWT)
  ValidationController.cs         # Internal: Swagger UI pre-authorize validation

Infrastructure/
  BasicAuthenticationHandler.cs   # Custom Basic auth handler
  ApiKeyAuthenticationHandler.cs  # Custom API Key handler (header/query/cookie)
  AuthErrorResponse.cs            # Shared 401 JSON response helper
  SandboxCredentials.cs           # Config model bound from appsettings

wwwroot/
  swagger-custom.js               # Swagger UI credential validation interceptor

Program.cs                        # Auth scheme registration + Swagger config
appsettings.json                  # Base config (no secrets)
appsettings.Development.json      # Dev credentials (gitignored)
```

---

## Notes

- This project is for **learning and testing only** — never use these credentials or patterns in production.
- `appsettings.Development.json` is gitignored to avoid committing secrets.
