# Quixa API Sandbox

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

## 🔗 Demo Domain APIs (EU)

Four chainable, stateful demo APIs built for Quixa flows, demo pages and product videos. They follow EU rules. **All data is synthetic** (no real people, documents or accounts).

| Domain | Base path | OpenAPI document |
|---|---|---|
| 🏦 Banking (onboarding, KYC, SEPA, iDEAL-style, PSD2) | `/api/sandbox/banking` | `/swagger/v1-banking/swagger.json` |
| 🛡️ Insurance (quote → policy → claim) | `/api/sandbox/insurance` | `/swagger/v1-insurance/swagger.json` |
| ✈️ Travel & booking (hotels, flights, EU261) | `/api/sandbox/travel` | `/swagger/v1-travel/swagger.json` |
| 🛒 E-commerce (cart, VAT, shipping, returns) | `/api/sandbox/ecommerce` | `/swagger/v1-ecommerce/swagger.json` |

### Conventions

- **One OpenAPI document per domain**, with one folder (tag) per resource in flow order, e.g. Banking: `Customers` → `KYC / CDD` → `Accounts` → `Transfers` → `Payments`.
- **IDs flow step to step.** Every create returns `201` with an `id` (e.g. `cus_1006`, `acc_1007`), so a later Quixa node can bind `{{CreateCustomer.response.id}}`.
- **Repeatable demos.** `POST /api/sandbox/{domain}/reset` restores a deterministic seed and returns the fixture IDs. Calling it again gives the same result, and IDs restart from the same number.
- **Errors** are RFC 7807 `application/problem+json` with a stable `code`:
  - `400`: format or checksum failures, with an `errors` map (BSN 11-proef, IBAN mod-97, EAN check digit, postcode `1234 AB`, E.164).
  - `404`: unknown resource (`*_NOT_FOUND`).
  - `409`: state conflict (`KYC_ALREADY_APPROVED`, `HOLD_ALREADY_BOOKED`, …).
  - `422`: business-rule failure (`INSUFFICIENT_FUNDS`, `WITHDRAWAL_PERIOD_EXPIRED`, …).
- **Standards:**
  - ISO 3166-1 alpha-2 countries, ISO 4217 `EUR` amounts.
  - ISO 8601 dates. Times include the local offset, e.g. `2026-12-10T07:15:00+01:00`.
  - E.164 phone numbers, English API naming.
  - The BSN is never returned unmasked (GDPR data minimisation).
- **Request body types:**
  - JSON for most endpoints.
  - `multipart/form-data` with `format: binary` file fields for KYC (`document`) and claims (`attachment`).
  - `application/x-www-form-urlencoded` for `POST /oauth/token`.
- **Synthetic fixtures:**
  - BSNs: `234567892`, `876543219`, `999990019`, `300000005`.
  - IBANs: `NL91ABNA0417164300` (Daan de Boer), `NL33KNAB0417164301` (Sanne Visser), `NL69INGB0123456789` (Jan de Vries).
  - Card tokens: `tok_visa_success`, `tok_mastercard_success`, `tok_card_declined`, `tok_insufficient_funds`.

### Demo scenarios (step order)

#### 🏦 Banking — "Open a bank account and pay instantly"
1. `POST /banking/reset`
2. `POST /banking/customers`: BSN, postal address. Returns `id`.
3. `POST /banking/customers/{{2.id}}/kyc`: **multipart**. Fields: `documentType` (PASSPORT / ID_CARD / RESIDENCE_PERMIT), `documentNumber`, `issuingCountry`, `expiryDate`, file `document` (PDF/PNG/JPEG, max 5 MB). Returns `status: APPROVED`.
4. `POST /banking/accounts` with `{ customerId, product: "CURRENT", bankCode: "KNAB" }`. Returns a valid IBAN and its BIC. Products: `CURRENT`, `SAVINGS` (linked to a current account), `TERM_DEPOSIT` (`termMonths` 3/6/12/24, `openingAmount` ≥ €1,000) and `BUSINESS`.
5. `POST /banking/accounts/{{4.id}}/deposits` adds balance. Also available:
   - `POST /banking/accounts/{id}/withdrawals` (cash; not from savings, not from a term deposit before maturity)
   - `POST /banking/transfers` with `{ fromAccountId, toAccountId, amount }` (between accounts at this bank; savings can only send to the linked current account)
   - `GET /banking/customers/{id}/accounts`, `POST /banking/accounts/{id}/close` (balance must be 0)
6. `POST /banking/payee-verifications` with `{ iban: "NL91ABNA0417164300", name: "D. de Boer" }`. Returns `CLOSE_MATCH` with `suggestedName`.
7. `POST /banking/payments/sepa-credit-transfers` with `instant: true`, `payeeVerificationId: {{6.id}}` and an optional `Idempotency-Key` header. Returns `ACSC`.
8. `GET /banking/accounts/{{4.id}}/statements`: camt.053-style JSON. Add `?format=csv` for CSV.
9. PSD2 AIS:
   - `POST /banking/psd2/v1/consents`
   - `POST /banking/psd2/v1/consents/{{9.consentId}}/authorisations`
   - `GET /banking/psd2/v1/accounts/{{4.id}}/transactions` with header `Consent-ID`.
- iDEAL-style: `POST /banking/payment-requests`, then `POST /banking/payment-requests/{id}/complete`. The request expires after 15 minutes.
- Negative tests:
  - BSN `123456789` → `400`; a duplicate BSN → `409 BSN_ALREADY_REGISTERED`.
  - Last name `Sanction` → KYC `REJECTED`; last name `Pep` → `MANUAL_REVIEW`.
  - A GIF upload → `422 UNSUPPORTED_FILE_FORMAT`; an expired document → `422 DOCUMENT_EXPIRED`.
  - An account before KYC → `422 KYC_NOT_APPROVED`; a large amount → `422 INSUFFICIENT_FUNDS`.

#### 🛡️ Insurance — "Insure a car and file a claim"
1. `POST /insurance/reset`
2. `POST /insurance/quotes`: `product: CAR`, policyholder, `car` details. The premium includes the 21% insurance premium tax.
3. `POST /insurance/policies` with `{ quoteId: {{2.id}}, iban, accountHolderName, acceptTerms: true }`. Creates the policy and a SEPA Core mandate.
4. `POST /insurance/policies/{{3.id}}/claims`: **multipart**. Fields: `incidentDate`, `category`, `description`, `claimedAmount`, file `attachment`.
5. `POST /insurance/claims/{{4.id}}/assessment`: `AUTO` approves up to €5,000; the deductible is applied.
6. `POST /insurance/claims/{{4.id}}/payout`, then `GET /insurance/claims/{{4.id}}/status`.
- Seeded active policy: `pol_seed_car`.
- Negative tests:
  - `ALL_RISK` on a car older than 8 years → `422 COVERAGE_NOT_AVAILABLE`.
  - `THEFT` without `policeReportNumber` → `422`.
  - Re-using a quote → `409`.

#### ✈️ Travel — "Book a hotel in Amsterdam, then cancel"
1. `GET /travel/hotels/search?city=Amsterdam&checkIn=2026-12-10&checkOut=2026-12-12&guests=2`. Prices include 21% accommodation VAT and the 12.5% Amsterdam tourist tax.
2. `POST /travel/holds` with `{ offerId: {{1[0].offerId}} }`. Locks the price for 15 minutes.
3. `POST /travel/bookings` with `{ holdId, contact, travellers[] }`. Returns `reference` (a 6-character PNR-style code).
4. `POST /travel/bookings/{{3.id}}/payments` with `CARD` + `tok_visa_success`, `IDEAL`, or `SEPA_CREDIT_TRANSFER`.
5. `POST /travel/bookings/{{3.id}}/cancellation`. Refunds follow the rate/fare rules.
- Flights: `GET /travel/flights/search?origin=AMS&destination=BCN&date=2026-12-10&passengers=1` offers LIGHT, STANDARD and FLEX fares, with air passenger tax.
- EU261: `POST /travel/flights/compensation-checks`. AMS→BCN with a 3h45 delay → €250; AMS→JFK with a 3h20 delay → €300 (halved); IST→AMS on a non-EU carrier → not eligible.

#### 🛒 E-commerce — "Buy, ship, return"
1. `POST /ecommerce/reset`, then `GET /ecommerce/products`. Products have GTIN-13 codes.
2. `POST /ecommerce/carts` with `{ country: "NL" }`.
3. `POST /ecommerce/carts/{{2.id}}/items` with `{ gtin | productId, quantity }`. Use `PATCH` or `DELETE /items/{itemId}` to change or remove an item.
4. `POST /ecommerce/carts/{{2.id}}/checkout`. VAT uses the destination country's rates (21% / 9%, DE 19% / 7%, …). Shipping is free from €50.
5. `POST /ecommerce/orders/{{4.id}}/shipments`. Returns a PostNL-style (`3SQXDM…`) or DHL-style (`JVGL…`) tracking number.
6. `POST /ecommerce/shipments/{{5.trackingNumber}}/advance` three times to reach `DELIVERED`.
7. `POST /ecommerce/orders/{{4.id}}/returns`, within the 14-day withdrawal period.
- Negative tests:
  - `prd_coffee` with quantity 5 → `422 INSUFFICIENT_STOCK`.
  - Advance with a back-dated `occurredAt` (more than 14 days ago), then return → `422 WITHDRAWAL_PERIOD_EXPIRED`.

### Optional authentication for the demo APIs

The demo APIs use OAuth2 by default (`DemoAuth:Mode` in `appsettings.json`); set it to `None` or `ApiKey` to change this. The OpenAPI documents advertise only the active scheme.

| Mode | How to call |
|---|---|
| `None` | No credentials needed. |
| `ApiKey` | Send header `X-Api-Key: quixa-demo-key`. Configure with `DemoAuth:ApiKeyHeader` and `DemoAuth:ApiKey`. |
| `OAuth2` (default) | Call `POST /oauth/token` (form-urlencoded, `grant_type=client_credentials`, `client_id=sandbox-client-id`, `client_secret=sandbox-client-secret`), then send `Authorization: Bearer <access_token>`. Scopes: `read` allows GET; `write` allows POST/PATCH/DELETE and also grants read. Without `scope`, the token gets every scope. A missing scope returns `403 INSUFFICIENT_SCOPE`. |

The legacy healthcare pipeline (`/api/sandbox/healthcare`) and the health endpoints are unchanged.

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

### Test

```bash
dotnet test
```

The tests in `tests/AuthSandbox.Tests` run every domain flow end to end in memory, with a fake clock for the hold, quote and withdrawal-period expiry cases.

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
        "read": "Read access (GET)",
        "write": "Write access (POST/PATCH/DELETE; includes read)"
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
  *AuthController.cs, OAuth2TokenController.cs, ValidationController.cs   # auth scheme sandboxes
  BankingSandboxController.cs      # onboarding, KYC (multipart), accounts, VoP, SEPA, iDEAL-style, statements
  BankingPsd2Controller.cs         # Berlin Group NextGenPSD2-style AIS (consents, accounts, balances, transactions)
  InsuranceSandboxController.cs    # quotes, policies, claims (multipart), assessment, payout
  TravelSandboxController.cs       # hotel/flight search, holds, bookings, payments, cancellations, EU261
  ECommerceSandboxController.cs    # catalog, carts, checkout with EU VAT, shipments, returns
  HealthcareSandboxController.cs   # legacy 5-step pipeline (unchanged)
Domain/<Domain>/                   # models, in-memory state + deterministic seed per domain
Infrastructure/
  Demo/                            # validators (BSN, IBAN, BIC, EAN, postcode…), problem helpers,
                                   # SandboxState base, DemoAuth filter, upload checks
  SwaggerSetup.cs                  # OpenAPI documents, examples, per-document security
tests/AuthSandbox.Tests/           # xUnit + WebApplicationFactory flow tests
```

---

## Azure Web App deployment

- **Hosting:** Azure App Service `api-sandbox` (North Europe), deployed by `.github/workflows/main_api-sandbox.yml` on every push to `main`. The workflow builds `AuthSandbox.sln`, so the test project compiles too, and publishes `AuthSandbox.csproj`. To gate deploys on the tests, add a `dotnet test AuthSandbox.sln -c Release` step after the build.
- **State is in memory, per instance.** Keep the app on **one instance**, or enable ARR affinity (session affinity), so a flow's requests reach the same instance. A restart or slot swap resets the data to the seed, which is the same state that `POST …/reset` gives.
- **Always On:** enable it so the seed is not lost to idle unloads during a recording.
- **App settings** (Configuration → Application settings; `__` separates sections):
  - `DemoAuth__Mode` = `None` | `ApiKey` | `OAuth2` (default `OAuth2`, set in `appsettings.json`)
  - `DemoAuth__ApiKey` = your demo key (default `quixa-demo-key`)
  - `SandboxCredentials__OAuth2__TokenUrl`: already set in `appsettings.json` to the Azure `/oauth/token` URL; override only for another host.
  - Override `SandboxCredentials__Bearer__JwtKey` and the OAuth2 client secret if the host is public.
- **Quixa import URLs:** one per domain, e.g. `https://<app-host>/swagger/v1-banking/swagger.json`. In `OAuth2` mode each domain document also contains `POST /oauth/token`.
- The server culture is fixed to invariant, so multipart amounts such as `1850.00` parse the same on every host.

---

## Notes

- This project is for **learning and testing only** — never use these credentials or patterns in production.
- `appsettings.Development.json` is gitignored to avoid committing secrets.
