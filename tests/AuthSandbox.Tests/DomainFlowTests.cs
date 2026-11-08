using System.Net;
using System.Text.Json.Nodes;

namespace AuthSandbox.Tests;

public class InsuranceFlowTests
{
    private const string B = "/api/sandbox/insurance";

    private static object CarQuote(string coverage = "ALL_RISK", int buildYear = 2022) => new
    {
        product = "CAR", startDate = "2026-12-01", paymentFrequency = "MONTHLY", deductible = 250,
        policyholder = new
        {
            firstName = "Sanne", lastName = "Visser", dateOfBirth = "1991-04-23", email = "sanne@example.nl", phone = "+31612345678",
            address = new { street = "Keizersgracht", houseNumber = 123, postcode = "1015 CJ", city = "Amsterdam", country = "NL" },
        },
        car = new { licensePlate = "K-123-AB", catalogValue = 32500, buildYear, coverage, claimFreeYears = 6 },
    };

    [Fact]
    public async Task Quote_policy_claim_assessment_payout()
    {
        using var api = new TestApi();
        var quote = await api.Post($"{B}/quotes", CarQuote(), HttpStatusCode.Created);
        var premium = quote["premium"]!;
        Assert.Equal(21m, (decimal)premium["insuranceTaxRate"]!);
        Assert.Equal((decimal)premium["netPremium"]! + (decimal)premium["insuranceTax"]!, (decimal)premium["grossPremium"]!);

        var policy = await api.Post($"{B}/policies", new { quoteId = (string)quote["id"]!, iban = "NL33KNAB0417164301", accountHolderName = "Sanne Visser", acceptTerms = true }, HttpStatusCode.Created);
        Assert.Equal("ACTIVE", (string?)policy["status"]);
        Assert.Equal("POL-2026-000002", (string?)policy["policyNumber"]);
        Assert.Equal("CORE", (string?)policy["mandate"]!["scheme"]);
        TestApi.AssertCode(await api.Post($"{B}/policies", new { quoteId = (string)quote["id"]!, iban = "NL33KNAB0417164301", accountHolderName = "S", acceptTerms = true }, HttpStatusCode.Conflict), "QUOTE_ALREADY_ACCEPTED");

        // Claims on the seeded active policy (started 60 days ago)
        var form = TestApi.Form(new() { ["incidentDate"] = "2026-11-05", ["category"] = "COLLISION", ["description"] = "Rear-ended at a traffic light.", ["claimedAmount"] = "1850.00" },
            "attachment", TestApi.SamplePng, "image/png", "damage.png");
        var claim = await api.Post($"{B}/policies/pol_seed_car/claims", form, HttpStatusCode.Created);
        Assert.Equal("SUBMITTED", (string?)claim["status"]);
        Assert.Equal("damage.png", (string?)claim["attachments"]![0]!["fileName"]);

        var assessed = await api.Post($"{B}/claims/{claim["id"]}/assessment", new { decision = "AUTO" });
        Assert.Equal("APPROVED", (string?)assessed["status"]);
        Assert.Equal(1600m, (decimal)assessed["payoutAmount"]!);
        var paid = await api.Post($"{B}/claims/{claim["id"]}/payout", null);
        Assert.Equal("PAID", (string?)paid["status"]);
        var status = await api.Get($"{B}/claims/{claim["id"]}/status");
        Assert.Equal(3, status["history"]!.AsArray().Count);
    }

    [Fact]
    public async Task Insurance_negative_scenarios()
    {
        using var api = new TestApi();
        TestApi.AssertCode(await api.Post($"{B}/quotes", CarQuote(buildYear: 2012), HttpStatusCode.UnprocessableEntity), "COVERAGE_NOT_AVAILABLE");
        var theft = TestApi.Form(new() { ["incidentDate"] = "2026-11-01", ["category"] = "THEFT", ["description"] = "Car stolen overnight.", ["claimedAmount"] = "9000" },
            "attachment", TestApi.SamplePdf, "application/pdf", "report.pdf");
        TestApi.AssertCode(await api.Post($"{B}/policies/pol_seed_car/claims", theft, HttpStatusCode.UnprocessableEntity), "POLICE_REPORT_REQUIRED");

        var quote = await api.Post($"{B}/quotes", CarQuote(), HttpStatusCode.Created);
        api.Clock.Advance(TimeSpan.FromDays(31));
        TestApi.AssertCode(await api.Post($"{B}/policies", new { quoteId = (string)quote["id"]!, iban = "NL33KNAB0417164301", accountHolderName = "Sanne", acceptTerms = true }, HttpStatusCode.UnprocessableEntity), "QUOTE_EXPIRED");
    }
}

public class TravelFlowTests
{
    private const string B = "/api/sandbox/travel";

    private static object Booking(string holdId, int travellers = 2) => new
    {
        holdId,
        contact = new { email = "sanne@example.nl", phone = "+31612345678" },
        travellers = Enumerable.Range(0, travellers).Select(i => new { firstName = "Traveller", lastName = $"No{i}", dateOfBirth = "1990-01-01", nationality = "NL" }),
    };

    [Fact]
    public async Task Hotel_search_hold_book_pay_cancel()
    {
        using var api = new TestApi();
        var offers = (await api.Get($"{B}/hotels/search?city=Amsterdam&checkIn=2026-12-10&checkOut=2026-12-12&guests=2")).AsArray();
        var offer = offers.First(o => (string?)o!["roomType"] == "DOUBLE_STANDARD")!;
        var price = offer["price"]!;
        Assert.Equal(298m, (decimal)price["baseAmount"]!);
        Assert.Equal(62.58m, (decimal)price["vatAmount"]!);       // 21% accommodation VAT
        Assert.Equal(37.25m, (decimal)price["taxesAndFees"]!);    // 12.5% Amsterdam tourist tax
        Assert.EndsWith("+01:00", (string)offer["freeCancellationUntil"]!);

        var hold = await api.Post($"{B}/holds", new { offerId = (string)offer["offerId"]! }, HttpStatusCode.Created);
        TestApi.AssertCode(await api.Post($"{B}/bookings", Booking((string)hold["id"]!, 3), HttpStatusCode.UnprocessableEntity), "TRAVELLER_COUNT_MISMATCH");
        var booking = await api.Post($"{B}/bookings", Booking((string)hold["id"]!), HttpStatusCode.Created);
        Assert.Equal("PENDING_PAYMENT", (string?)booking["status"]);
        Assert.Equal(6, ((string)booking["reference"]!).Length);
        TestApi.AssertCode(await api.Post($"{B}/bookings", Booking((string)hold["id"]!), HttpStatusCode.Conflict), "HOLD_ALREADY_BOOKED");

        TestApi.AssertCode(await api.Post($"{B}/bookings/{booking["id"]}/payments", new { method = "CARD", cardToken = "tok_card_declined" }, HttpStatusCode.UnprocessableEntity), "PAYMENT_DECLINED");
        var paid = await api.Post($"{B}/bookings/{booking["id"]}/payments", new { method = "CARD", cardToken = "tok_visa_success" });
        Assert.Equal("CONFIRMED", (string?)paid["status"]);

        var cancelled = await api.Post($"{B}/bookings/{booking["id"]}/cancellation", new { reason = "Change of plans" });
        Assert.Equal("CANCELLED", (string?)cancelled["status"]);
        Assert.Equal(397.83m, (decimal)cancelled["refund"]!["amount"]!);
    }

    [Fact]
    public async Task Hold_expires_after_15_minutes()
    {
        using var api = new TestApi();
        var offers = (await api.Get($"{B}/flights/search?origin=AMS&destination=BCN&date=2026-12-10&passengers=1")).AsArray();
        Assert.Equal(6, offers.Count);   // 2 flights × 3 fares
        Assert.EndsWith("+01:00", (string)offers[0]!["departureTime"]!);
        var hold = await api.Post($"{B}/holds", new { offerId = (string)offers[0]!["offerId"]! }, HttpStatusCode.Created);
        api.Clock.Advance(TimeSpan.FromMinutes(16));
        TestApi.AssertCode(await api.Post($"{B}/bookings", Booking((string)hold["id"]!, 1), HttpStatusCode.UnprocessableEntity), "HOLD_EXPIRED");
    }

    [Theory]
    [InlineData("AMS", "BCN", "NL", 225, true, 250)]
    [InlineData("AMS", "BCN", "NL", 150, false, 0)]
    [InlineData("AMS", "ATH", "NL", 200, true, 400)]
    [InlineData("AMS", "JFK", "NL", 200, true, 300)]
    [InlineData("AMS", "JFK", "NL", 260, true, 600)]
    [InlineData("IST", "AMS", "TR", 300, false, 0)]
    public async Task Eu261_compensation(string from, string to, string carrierCountry, int delay, bool eligible, decimal amount)
    {
        using var api = new TestApi();
        var scheduled = new DateTimeOffset(2026, 11, 1, 9, 25, 0, TimeSpan.FromHours(1));
        var r = await api.Post($"{B}/flights/compensation-checks", new
        {
            flightNumber = "QX101", departureAirport = from, arrivalAirport = to, carrierCountry,
            scheduledArrival = scheduled, actualArrival = scheduled.AddMinutes(delay),
        });
        Assert.Equal(eligible, (bool)r["eligible"]!);
        Assert.Equal(amount, (decimal)r["compensation"]!["amount"]!);
    }
}

public class ECommerceFlowTests
{
    private const string B = "/api/sandbox/ecommerce";

    private static object Checkout(string country = "NL", string postcode = "1015 CJ") => new
    {
        customer = new { firstName = "Sanne", lastName = "Visser", email = "sanne@example.nl", phone = "+31612345678" },
        shippingAddress = new { street = "Keizersgracht", houseNumber = 123, postcode, city = "Amsterdam", country },
        shippingMethod = "POSTNL_STANDARD", payment = new { method = "IDEAL" }, acceptTerms = true,
    };

    [Fact]
    public async Task Cart_checkout_ship_deliver_return()
    {
        using var api = new TestApi();
        var cart = await api.Post($"{B}/carts", new { country = "NL" }, HttpStatusCode.Created);
        var id = (string)cart["id"]!;
        await api.Post($"{B}/carts/{id}/items", new { gtin = "8712345000042", quantity = 1 }, HttpStatusCode.Created);   // cookbook 9%
        cart = await api.Post($"{B}/carts/{id}/items", new { productId = "prd_stroopwafels", quantity = 2 }, HttpStatusCode.Created);
        Assert.Equal(2, cart["items"]!.AsArray().Count);
        TestApi.AssertCode(await api.Post($"{B}/carts/{id}/items", new { productId = "prd_coffee", quantity = 5 }, HttpStatusCode.UnprocessableEntity), "INSUFFICIENT_STOCK");
        await api.Post($"{B}/carts/{id}/items", new { productId = "prd_raincoat", quantity = 1 }, HttpStatusCode.Created);   // 21%

        var order = await api.Post($"{B}/carts/{id}/checkout", Checkout(), HttpStatusCode.Created);
        var totals = order["totals"]!;
        var vat = totals["vatBreakdown"]!.AsArray();
        Assert.Equal(new[] { 21m, 9m }, vat.Select(v => (decimal)v!["rate"]!).ToArray());
        Assert.Equal(0m, (decimal)totals["shippingCost"]!);   // free above €50
        Assert.Equal((decimal)totals["grandTotal"]!, vat.Sum(v => (decimal)v!["grossAmount"]!));
        TestApi.AssertCode(await api.Post($"{B}/carts/{id}/checkout", Checkout(), HttpStatusCode.Conflict), "CART_ALREADY_CHECKED_OUT");

        TestApi.AssertCode(await api.Post($"{B}/orders/{order["id"]}/returns", new { lines = new[] { new { lineId = 1, quantity = 1 } } }, HttpStatusCode.UnprocessableEntity), "ORDER_NOT_DELIVERED");
        var shipment = await api.Post($"{B}/orders/{order["id"]}/shipments", new { }, HttpStatusCode.Created);
        var tracking = (string)shipment["trackingNumber"]!;
        Assert.StartsWith("3SQXDM", tracking);
        for (var i = 0; i < 3; i++) await api.Post($"{B}/shipments/{tracking}/advance", new { });
        Assert.Equal("DELIVERED", (string?)(await api.Get($"{B}/shipments/{tracking}"))["status"]);

        var ret = await api.Post($"{B}/orders/{order["id"]}/returns", new { lines = new[] { new { lineId = 3, quantity = 1, reason = "Too small" } } }, HttpStatusCode.Created);
        Assert.Equal(85.00m, (decimal)ret["refundAmount"]!);
        Assert.Equal("PARTIALLY_RETURNED", (string?)(await api.Get($"{B}/orders/{order["id"]}"))["status"]);
    }

    [Fact]
    public async Task Destination_vat_and_expired_withdrawal_period()
    {
        using var api = new TestApi();
        var cart = await api.Post($"{B}/carts", new { country = "DE" }, HttpStatusCode.Created);
        cart = await api.Post($"{B}/carts/{cart["id"]}/items", new { productId = "prd_headphones", quantity = 1 }, HttpStatusCode.Created);
        Assert.Equal(19m, (decimal)cart["items"]![0]!["vatRate"]!);
        var order = await api.Post($"{B}/carts/{cart["id"]}/checkout", Checkout("DE", "10115"), HttpStatusCode.Created);
        var shipment = await api.Post($"{B}/orders/{order["id"]}/shipments", new { carrier = "DHL" }, HttpStatusCode.Created);
        Assert.StartsWith("JVGL", (string)shipment["trackingNumber"]!);
        var past = TestApi.Start.AddDays(-20);
        for (var i = 0; i < 3; i++) await api.Post($"{B}/shipments/{shipment["id"]}/advance", new { occurredAt = past.AddHours(i) });
        TestApi.AssertCode(await api.Post($"{B}/orders/{order["id"]}/returns", new { lines = new[] { new { lineId = 1, quantity = 1 } } }, HttpStatusCode.UnprocessableEntity), "WITHDRAWAL_PERIOD_EXPIRED");
    }
}

public class DemoAuthTests
{
    [Fact]
    public async Task Api_key_mode_protects_domain_endpoints()
    {
        using var api = new TestApi(("DemoAuth:Mode", "ApiKey"), ("DemoAuth:ApiKey", "test-key"));
        TestApi.AssertCode(await api.Get("/api/sandbox/ecommerce/products", HttpStatusCode.Unauthorized), "UNAUTHORIZED");
        api.Client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        await api.Get("/api/sandbox/ecommerce/products");
        var doc = await api.Get("/swagger/v1-ecommerce/swagger.json");
        Assert.NotNull(doc["components"]!["securitySchemes"]!["DemoApiKey"]);
    }

    private static async Task<string> Token(TestApi api, string? scope = null)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = "sandbox-client-id", ["client_secret"] = "sandbox-client-secret",
        };
        if (scope is not null) form["scope"] = scope;
        var token = await api.Post("/oauth/token", new FormUrlEncodedContent(form));
        Assert.Equal("Bearer", (string?)token["token_type"]);
        return (string)token["access_token"]!;
    }

    [Fact]
    public async Task OAuth2_mode_accepts_client_credentials_token()
    {
        using var api = new TestApi(("DemoAuth:Mode", "OAuth2"));
        TestApi.AssertCode(await api.Get("/api/sandbox/ecommerce/products", HttpStatusCode.Unauthorized), "UNAUTHORIZED");
        api.Client.DefaultRequestHeaders.Authorization = new("Bearer", await Token(api));   // default: all scopes
        await api.Get("/api/sandbox/ecommerce/products");
        await api.Post("/api/sandbox/ecommerce/carts", new { country = "NL" }, HttpStatusCode.Created);

        var doc = await api.Get("/swagger/v1-ecommerce/swagger.json");
        Assert.NotNull(doc["paths"]!["/oauth/token"]);
        Assert.Equal(["read", "write"], doc["components"]!["securitySchemes"]!["OAuth2"]!["flows"]!["clientCredentials"]!["scopes"]!.AsObject().Select(k => k.Key).ToArray());
        Assert.Equal("clientCredentials", doc["components"]!["securitySchemes"]!["OAuth2"]!["flows"]!.AsObject().First().Key);
    }

    [Fact]
    public async Task OAuth2_mode_enforces_scopes_and_rejects_bad_tokens()
    {
        using var api = new TestApi(("DemoAuth:Mode", "OAuth2"));
        api.Client.DefaultRequestHeaders.Authorization = new("Bearer", await Token(api, "read"));
        await api.Get("/api/sandbox/ecommerce/products");
        var forbidden = await api.Post("/api/sandbox/ecommerce/carts", new { country = "NL" }, HttpStatusCode.Forbidden);
        TestApi.AssertCode(forbidden, "INSUFFICIENT_SCOPE");
        Assert.Equal("write", (string?)forbidden["requiredScope"]);

        // write also grants read
        api.Client.DefaultRequestHeaders.Authorization = new("Bearer", await Token(api, "write"));
        await api.Post("/api/sandbox/ecommerce/carts", new { country = "NL" }, HttpStatusCode.Created);
        await api.Get("/api/sandbox/ecommerce/products");

        // a token with an unknown scope only is rejected
        api.Client.DefaultRequestHeaders.Authorization = new("Bearer", await Token(api, "admin"));
        TestApi.AssertCode(await api.Get("/api/sandbox/ecommerce/products", HttpStatusCode.Forbidden), "INSUFFICIENT_SCOPE");

        api.Client.DefaultRequestHeaders.Authorization = new("Bearer", "not-a-jwt");
        await api.Get("/api/sandbox/ecommerce/products", HttpStatusCode.Unauthorized);

        await api.Post("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = "sandbox-client-id", ["client_secret"] = "wrong",
        }), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task None_mode_is_open_by_default()
    {
        using var api = new TestApi();
        await api.Get("/api/sandbox/ecommerce/products");
    }
}

public class OpenApiTests
{
    [Fact]
    public async Task Domain_documents_have_correct_media_types()
    {
        using var api = new TestApi();
        var banking = (await api.Get("/swagger/v1-banking/swagger.json"))["paths"]!.AsObject();
        var kyc = banking["/api/sandbox/banking/customers/{customerId}/kyc"]!["post"]!["requestBody"]!["content"]!["multipart/form-data"]!["schema"]!;
        Assert.Equal("binary", (string?)kyc["properties"]!["document"]!["format"]);
        Assert.NotNull(kyc["properties"]!["documentType"]);
        Assert.NotNull(banking["/api/sandbox/banking/customers"]!["post"]!["requestBody"]!["content"]!["application/json"]);
        Assert.All(banking, p => Assert.StartsWith("/api/sandbox/banking/", p.Key));   // only banking, no token endpoint in None mode

        var insurance = (await api.Get("/swagger/v1-insurance/swagger.json"))["paths"]!.AsObject();
        var claim = insurance["/api/sandbox/insurance/policies/{policyId}/claims"]!["post"]!["requestBody"]!["content"]!["multipart/form-data"]!["schema"]!;
        Assert.Equal("binary", (string?)claim["properties"]!["attachment"]!["format"]);

        var oauth = (await api.Get("/swagger/v1-oauth2/swagger.json"))["paths"]!.AsObject();
        Assert.NotNull(oauth["/oauth/token"]!["post"]!["requestBody"]!["content"]!["application/x-www-form-urlencoded"]);

        var swaggerUi = await api.Client.GetStringAsync("/swagger/index.html");
        Assert.DoesNotContain("v1-demo", swaggerUi);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await api.Client.GetAsync("/swagger/v1-demo/swagger.json")).StatusCode);

        var doc = await api.Get("/swagger/v1-banking/swagger.json");
        Assert.Equal("234567892", (string?)doc["components"]!["schemas"]!["Banking.CreateCustomerRequest"]!["example"]!["bsn"]);
    }

    [Fact]
    public async Task Operations_are_grouped_per_resource_in_flow_order()
    {
        using var api = new TestApi();
        var doc = await api.Get("/swagger/v1-banking/swagger.json");
        var tags = doc["tags"]!.AsArray().Select(t => (string)t!["name"]!).ToList();
        Assert.Equal(["Customers", "KYC / CDD", "Accounts", "Transfers", "Verification of Payee", "Payments", "Payment Requests",
            "Statements", "PSD2 Consents", "PSD2 Account Information", "Sandbox"], tags);

        string TagOf(JsonNode d, string path, string method) => (string)d["paths"]![path]![method]!["tags"]![0]!;
        Assert.Equal("Accounts", TagOf(doc, "/api/sandbox/banking/accounts/{accountId}/withdrawals", "post"));
        Assert.Equal("Transfers", TagOf(doc, "/api/sandbox/banking/transfers", "post"));
        Assert.Equal("PSD2 Account Information", TagOf(doc, "/api/sandbox/banking/psd2/v1/accounts", "get"));
        var insurance = await api.Get("/swagger/v1-insurance/swagger.json");
        Assert.Equal("Claims", TagOf(insurance, "/api/sandbox/insurance/policies/{policyId}/claims", "post"));
        var travel = await api.Get("/swagger/v1-travel/swagger.json");
        Assert.Equal("Payments", TagOf(travel, "/api/sandbox/travel/bookings/{bookingId}/payments", "post"));
        var shop = await api.Get("/swagger/v1-ecommerce/swagger.json");
        Assert.Equal("Checkout & Orders", TagOf(shop, "/api/sandbox/ecommerce/carts/{cartId}/checkout", "post"));
        Assert.Equal("Shipments", TagOf(shop, "/api/sandbox/ecommerce/orders/{orderId}/shipments", "post"));
    }

    [Theory]
    [InlineData("v1-banking")] [InlineData("v1-insurance")] [InlineData("v1-travel")] [InlineData("v1-ecommerce")]
    [InlineData("v1-basic")] [InlineData("v1-oauth2")] [InlineData("v1-healthcare")] [InlineData("v1-health")]
    public async Task Every_document_renders(string doc)
    {
        using var api = new TestApi();
        var json = await api.Get($"/swagger/{doc}/swagger.json");
        Assert.NotEmpty(json["paths"]!.AsObject());
    }
}
