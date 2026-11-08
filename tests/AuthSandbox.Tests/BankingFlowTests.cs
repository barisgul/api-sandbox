using System.Net;
using System.Net.Http.Json;

namespace AuthSandbox.Tests;

public class BankingFlowTests
{
    private const string B = "/api/sandbox/banking";

    private static object NewCustomer(string bsn = "234567892", string lastName = "Visser", string dob = "1991-04-23", string nationality = "NL") => new
    {
        firstName = "Sanne", lastName, dateOfBirth = dob, bsn, email = "sanne@example.nl", phone = "+31612345678", nationality,
        address = new { street = "Keizersgracht", houseNumber = 123, houseNumberAddition = "A", postcode = "1015cj", city = "Amsterdam", country = "NL" },
    };

    private static MultipartFormDataContent Kyc(string type = "PASSPORT", string expiry = "2031-06-30", byte[]? file = null, string contentType = "application/pdf") =>
        TestApi.Form(new() { ["documentType"] = type, ["documentNumber"] = "NX4K29PL7", ["issuingCountry"] = "NL", ["expiryDate"] = expiry },
            "document", file ?? TestApi.SamplePdf, contentType, "passport.pdf");

    [Fact]
    public async Task Onboarding_to_sepa_instant_to_statement_and_psd2()
    {
        using var api = new TestApi();
        var customer = await api.Post($"{B}/customers", NewCustomer(), HttpStatusCode.Created);
        var customerId = (string)customer["id"]!;
        Assert.Equal("NOT_STARTED", (string?)customer["kycStatus"]);
        Assert.Equal("1015 CJ", (string?)customer["address"]!["postcode"]);
        Assert.Equal("******892", (string?)customer["bsnMasked"]);
        Assert.Null(customer["bsn"]);

        var kyc = await api.Post($"{B}/customers/{customerId}/kyc", Kyc());
        Assert.Equal("APPROVED", (string?)kyc["status"]);
        Assert.Equal("application/pdf", (string?)kyc["document"]!["contentType"]);
        Assert.Equal(64, ((string)kyc["document"]!["sha256"]!).Length);

        var account = await api.Post($"{B}/accounts", new { customerId, bankCode = "KNAB" }, HttpStatusCode.Created);
        var accountId = (string)account["id"]!;
        var iban = (string)account["iban"]!;
        Assert.StartsWith("NL", iban);
        Assert.Equal("KNAB", iban[4..8]);
        Assert.True(AuthSandbox.Infrastructure.Demo.Validators.IsValidIban(iban));
        Assert.Equal("KNABNL2H", (string?)account["bic"]);

        await api.Post($"{B}/accounts/{accountId}/deposits", new { amount = 1000m }, HttpStatusCode.Created);

        var vop = await api.Post($"{B}/payee-verifications", new { iban = "NL91 ABNA 0417 1643 00", name = "D. de Boer" });
        Assert.Equal("CLOSE_MATCH", (string?)vop["result"]);
        Assert.Equal("Daan de Boer", (string?)vop["suggestedName"]);

        var payment = await api.Post($"{B}/payments/sepa-credit-transfers", new
        {
            debtorAccountId = accountId, creditorIban = "NL91ABNA0417164300", creditorName = "Daan de Boer", amount = 125.50m,
            remittanceInformation = "Huur december", endToEndId = "E2E-1", instant = true, payeeVerificationId = (string)vop["id"]!,
        }, HttpStatusCode.Created);
        Assert.Equal("ACSC", (string?)payment["status"]);
        Assert.Equal("SCT_INST", (string?)payment["scheme"]);
        Assert.Equal("CLOSE_MATCH", (string?)payment["payeeVerificationResult"]);

        var acc = await api.Get($"{B}/accounts/{accountId}");
        Assert.Equal(874.50m, (decimal)acc["balance"]!);
        var creditor = await api.Get($"{B}/accounts/acc_seed_daan");
        Assert.Equal(5125.50m, (decimal)creditor["balance"]!);

        var statement = await api.Get($"{B}/accounts/{accountId}/statements");
        Assert.Equal("camt.053.001.08", (string?)statement["messageType"]);
        Assert.Equal(2, statement["statement"]!["entries"]!.AsArray().Count);
        Assert.Equal("CLBD", (string?)statement["statement"]!["balances"]![1]!["type"]);

        var csv = await api.Client.GetStringAsync($"{B}/accounts/{accountId}/statements?format=csv");
        Assert.StartsWith("bookingDate,valueDate,amount", csv);
        Assert.Contains("125.50,EUR,DBIT,Daan de Boer", csv);

        // PSD2 AIS
        var consent = await api.Post($"{B}/psd2/v1/consents", new
        {
            access = new { accounts = new[] { new { iban } }, balances = new[] { new { iban } }, transactions = new[] { new { iban } } },
            recurringIndicator = true, validUntil = "2027-01-31", frequencyPerDay = 4,
        }, HttpStatusCode.Created);
        var consentId = (string)consent["consentId"]!;
        Assert.Equal("received", (string?)consent["consentStatus"]);
        Assert.NotNull(consent["_links"]!["startAuthorisation"]);

        var req = new HttpRequestMessage(HttpMethod.Get, $"{B}/psd2/v1/accounts/{accountId}/balances");
        req.Headers.Add("Consent-ID", consentId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client.SendAsync(req)).StatusCode);

        var auth = await api.Post($"{B}/psd2/v1/consents/{consentId}/authorisations", new { outcome = "APPROVE" }, HttpStatusCode.Created);
        Assert.Equal("valid", (string?)auth["consentStatus"]);

        req = new HttpRequestMessage(HttpMethod.Get, $"{B}/psd2/v1/accounts/{accountId}/transactions");
        req.Headers.Add("Consent-ID", consentId);
        var tx = await (await api.Client.SendAsync(req)).Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();
        Assert.Equal("-125.50", (string?)tx!["transactions"]!["booked"]![0]!["transactionAmount"]!["amount"]);
    }

    [Fact]
    public async Task Negative_scenarios_return_realistic_errors()
    {
        using var api = new TestApi();
        var bad = await api.Post($"{B}/customers", NewCustomer(bsn: "123456789"), HttpStatusCode.BadRequest);
        Assert.NotNull(bad["errors"]!["Bsn"]);

        TestApi.AssertCode(await api.Post($"{B}/customers", NewCustomer(bsn: "123456782"), HttpStatusCode.Conflict), "BSN_ALREADY_REGISTERED");
        TestApi.AssertCode(await api.Post($"{B}/customers", NewCustomer(dob: "2012-01-01"), HttpStatusCode.UnprocessableEntity), "MINIMUM_AGE_NOT_MET");
        TestApi.AssertCode(await api.Get($"{B}/customers/cus_nope", HttpStatusCode.NotFound), "CUSTOMER_NOT_FOUND");

        var c = await api.Post($"{B}/customers", NewCustomer(), HttpStatusCode.Created);
        var id = (string)c["id"]!;
        TestApi.AssertCode(await api.Post($"{B}/accounts", new { customerId = id }, HttpStatusCode.UnprocessableEntity), "KYC_NOT_APPROVED");
        TestApi.AssertCode(await api.Post($"{B}/customers/{id}/kyc", Kyc(file: "GIF89a"u8.ToArray(), contentType: "image/gif"), HttpStatusCode.UnprocessableEntity), "UNSUPPORTED_FILE_FORMAT");
        TestApi.AssertCode(await api.Post($"{B}/customers/{id}/kyc", Kyc(contentType: "image/png"), HttpStatusCode.UnprocessableEntity), "FILE_CONTENT_MISMATCH");
        TestApi.AssertCode(await api.Post($"{B}/customers/{id}/kyc", Kyc(expiry: "2024-01-01"), HttpStatusCode.UnprocessableEntity), "DOCUMENT_EXPIRED");
        await api.Post($"{B}/customers/{id}/kyc", Kyc(file: TestApi.SamplePng, contentType: "image/png"));
        TestApi.AssertCode(await api.Post($"{B}/customers/{id}/kyc", Kyc(), HttpStatusCode.Conflict), "KYC_ALREADY_APPROVED");

        var sanction = await api.Post($"{B}/customers", NewCustomer(bsn: "876543219", lastName: "Sanction"), HttpStatusCode.Created);
        var rejected = await api.Post($"{B}/customers/{sanction["id"]}/kyc", Kyc());
        Assert.Equal("REJECTED", (string?)rejected["status"]);
        Assert.True((bool)rejected["screening"]!["sanctionsMatch"]!);

        TestApi.AssertCode(await api.Post($"{B}/payments/sepa-credit-transfers", new
        {
            debtorAccountId = "acc_seed_sanne", creditorIban = "NL91ABNA0417164300", creditorName = "Daan de Boer", amount = 999999m,
        }, HttpStatusCode.UnprocessableEntity), "INSUFFICIENT_FUNDS");
        await api.Post($"{B}/payments/sepa-credit-transfers", new
        {
            debtorAccountId = "acc_seed_sanne", creditorIban = "NL91ABNA0417164300", creditorName = "X", amount = 1m, currency = "USD",
        }, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Account_types_add_balance_withdraw_and_transfer()
    {
        using var api = new TestApi();
        const string customer = "cus_seed_sanne", current = "acc_seed_sanne";   // CURRENT, balance 1854.13

        TestApi.AssertCode(await api.Post($"{B}/accounts", new { customerId = customer, product = "SAVINGS", linkedAccountId = "acc_seed_daan" }, HttpStatusCode.UnprocessableEntity), "CURRENT_ACCOUNT_REQUIRED");
        var savings = await api.Post($"{B}/accounts", new { customerId = customer, product = "SAVINGS" }, HttpStatusCode.Created);
        Assert.Equal(current, (string?)savings["linkedAccountId"]);
        Assert.Equal(1.75m, (decimal)savings["interestRate"]!);
        var savingsId = (string)savings["id"]!;

        var added = await api.Post($"{B}/accounts/{current}/deposits", new { amount = 1000m, description = "Salary" }, HttpStatusCode.Created);
        Assert.Equal(2854.13m, (decimal)added["balanceAfter"]!);

        var transfer = await api.Post($"{B}/transfers", new { fromAccountId = current, toAccountId = savingsId, amount = 500m, description = "Monthly savings" }, HttpStatusCode.Created);
        Assert.Equal(2354.13m, (decimal)transfer["fromBalanceAfter"]!);
        Assert.Equal(500m, (decimal)transfer["toBalanceAfter"]!);
        await api.Get($"{B}/transfers/{transfer["id"]}");

        TestApi.AssertCode(await api.Post($"{B}/transfers", new { fromAccountId = savingsId, toAccountId = "acc_seed_daan", amount = 10m }, HttpStatusCode.UnprocessableEntity), "SAVINGS_TRANSFER_RESTRICTED");
        TestApi.AssertCode(await api.Post($"{B}/accounts/{savingsId}/withdrawals", new { amount = 10m }, HttpStatusCode.UnprocessableEntity), "WITHDRAWAL_NOT_ALLOWED");
        TestApi.AssertCode(await api.Post($"{B}/transfers", new { fromAccountId = current, toAccountId = savingsId, amount = 99999m }, HttpStatusCode.UnprocessableEntity), "INSUFFICIENT_FUNDS");
        await api.Post($"{B}/transfers", new { fromAccountId = savingsId, toAccountId = current, amount = 100m }, HttpStatusCode.Created);

        var cash = await api.Post($"{B}/accounts/{current}/withdrawals", new { amount = 200m }, HttpStatusCode.Created);
        Assert.Equal("DBIT", (string?)cash["creditDebitIndicator"]);

        var term = await api.Post($"{B}/accounts", new { customerId = customer, product = "TERM_DEPOSIT", termMonths = 12, openingAmount = 1500m }, HttpStatusCode.Created);
        Assert.Equal(1500m, (decimal)term["balance"]!);
        Assert.Equal("2027-11-08", (string?)term["maturityDate"]);
        TestApi.AssertCode(await api.Post($"{B}/transfers", new { fromAccountId = (string)term["id"]!, toAccountId = current, amount = 100m }, HttpStatusCode.UnprocessableEntity), "TERM_DEPOSIT_LOCKED");
        TestApi.AssertCode(await api.Post($"{B}/accounts", new { customerId = customer, product = "TERM_DEPOSIT", termMonths = 5, openingAmount = 1500m }, HttpStatusCode.UnprocessableEntity), "INVALID_TERM");
        TestApi.AssertCode(await api.Post($"{B}/payments/sepa-credit-transfers", new { debtorAccountId = savingsId, creditorIban = "NL91ABNA0417164300", creditorName = "Daan de Boer", amount = 1m }, HttpStatusCode.UnprocessableEntity), "ACCOUNT_TYPE_NOT_ALLOWED");

        var list = (await api.Get($"{B}/customers/{customer}/accounts")).AsArray();
        Assert.Equal(["CURRENT", "SAVINGS", "TERM_DEPOSIT"], list.Select(a => (string)a!["product"]!).ToArray());

        TestApi.AssertCode(await api.Post($"{B}/accounts/{savingsId}/close", null, HttpStatusCode.Conflict), "ACCOUNT_HAS_BALANCE");
        await api.Post($"{B}/transfers", new { fromAccountId = savingsId, toAccountId = current, amount = 400m }, HttpStatusCode.Created);
        var closed = await api.Post($"{B}/accounts/{savingsId}/close", null);
        Assert.Equal("CLOSED", (string?)closed["status"]);
        TestApi.AssertCode(await api.Post($"{B}/accounts/{savingsId}/deposits", new { amount = 1m }, HttpStatusCode.Conflict), "ACCOUNT_CLOSED");
    }

    [Fact]
    public async Task Idempotency_key_replays_and_rejects_reuse()
    {
        using var api = new TestApi();
        async Task<HttpResponseMessage> Pay(decimal amount)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"{B}/payments/sepa-credit-transfers")
            {
                Content = JsonContent.Create(new { debtorAccountId = "acc_seed_sanne", creditorIban = "NL69INGB0123456789", creditorName = "Jan de Vries", amount }),
            };
            req.Headers.Add("Idempotency-Key", "demo-key-1");
            return await api.Client.SendAsync(req);
        }
        Assert.Equal(HttpStatusCode.Created, (await Pay(10m)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Pay(10m)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Pay(11m)).StatusCode);
        var acc = await api.Get($"{B}/accounts/acc_seed_sanne");
        Assert.Equal(1844.13m, (decimal)acc["balance"]!);   // 1854.13 seed - 10.00 once
    }

    [Fact]
    public async Task Ideal_payment_request_expires_after_15_minutes()
    {
        using var api = new TestApi();
        var pr = await api.Post($"{B}/payment-requests", new { creditorAccountId = "acc_seed_daan", amount = 49.95m, description = "Order 42" }, HttpStatusCode.Created);
        api.Clock.Advance(TimeSpan.FromMinutes(16));
        TestApi.AssertCode(await api.Post($"{B}/payment-requests/{pr["id"]}/complete", new { outcome = "SUCCESS" }, HttpStatusCode.UnprocessableEntity), "PAYMENT_REQUEST_EXPIRED");

        var pr2 = await api.Post($"{B}/payment-requests", new { creditorAccountId = "acc_seed_daan", amount = 49.95m, description = "Order 43" }, HttpStatusCode.Created);
        var paid = await api.Post($"{B}/payment-requests/{pr2["id"]}/complete", new { outcome = "SUCCESS", payerAccountId = "acc_seed_sanne" });
        Assert.Equal("PAID", (string?)paid["status"]);
    }

    [Fact]
    public async Task Reset_is_idempotent_and_restores_seed()
    {
        using var api = new TestApi();
        var first = await api.Post($"{B}/customers", NewCustomer(), HttpStatusCode.Created);
        var r1 = await api.Post($"{B}/reset", null);
        var r2 = await api.Post($"{B}/reset", null);
        Assert.Equal(r1.ToJsonString(), r2.ToJsonString());
        var again = await api.Post($"{B}/customers", NewCustomer(), HttpStatusCode.Created);
        Assert.Equal((string?)first["id"], (string?)again["id"]);   // IDs restart after reset
    }
}
