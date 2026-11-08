using System.Globalization;
using AuthSandbox.Domain.Banking;
using AuthSandbox.Infrastructure.Demo;
using Microsoft.AspNetCore.Mvc;

namespace AuthSandbox.Controllers;

/// <summary>
/// PSD2 Account Information Service modelled on the Berlin Group NextGenPSD2 XS2A framework:
/// create consent → SCA (sandbox) → read accounts, balances and transactions with the <c>Consent-ID</c> header.
/// </summary>
[Route("api/sandbox/banking/psd2/v1")]
[ApiExplorerSettings(GroupName = "v1-banking")]
[Tags("Banking — PSD2 AIS")]
public class BankingPsd2Controller(BankingState state) : DemoControllerBase
{
    private string Base => "/api/sandbox/banking/psd2/v1";

    /// <summary>Create an account-information consent (consentStatus = received).</summary>
    /// <response code="422">CONSENT_ACCOUNT_UNKNOWN — an IBAN is not held at this sandbox bank.</response>
    [HttpPost("consents")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Consent), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public IActionResult CreateConsent([FromBody] ConsentRequest request, [FromHeader(Name = "X-Request-ID")] string? requestId)
    {
        if (request.ValidUntil!.Value <= state.Today) return FieldProblem(nameof(request.ValidUntil), "validUntil must be in the future.");
        if (request.ValidUntil.Value > state.Today.AddDays(180))
            return FieldProblem(nameof(request.ValidUntil), "validUntil may be at most 180 days ahead (PSD2 RTS Art. 10).");

        var all = request.Access.Accounts.Concat(request.Access.Balances).Concat(request.Access.Transactions).ToList();
        if (all.Count == 0) return FieldProblem("access", "At least one account reference is required.");
        var unknown = all.Select(a => Validators.NormalizeIban(a.Iban)).Where(i => state.FindByIban(i) is null).Distinct().ToList();
        if (unknown.Count > 0) return RuleProblem("CONSENT_ACCOUNT_UNKNOWN", "One or more IBANs are not held at this bank.", new { unknown });

        lock (state.Sync)
        {
            var consent = new Consent
            {
                ConsentId = state.NextId("consent"),
                Access = Normalize(request.Access),
                RecurringIndicator = request.RecurringIndicator,
                ValidUntil = request.ValidUntil.Value,
                FrequencyPerDay = request.FrequencyPerDay,
                LastActionDate = state.Today,
            };
            consent.Links = new()
            {
                ["self"] = new($"{Base}/consents/{consent.ConsentId}"),
                ["status"] = new($"{Base}/consents/{consent.ConsentId}/status"),
                ["startAuthorisation"] = new($"{Base}/consents/{consent.ConsentId}/authorisations"),
            };
            state.Consents[consent.ConsentId] = consent;
            if (requestId is not null) Response.Headers["X-Request-ID"] = requestId;
            return Created(consent.Links["self"].Href, consent);
        }
    }

    private static ConsentAccess Normalize(ConsentAccess a)
    {
        static List<AccountReference> N(List<AccountReference> l) =>
            l.Select(r => new AccountReference { Iban = Validators.NormalizeIban(r.Iban), Currency = r.Currency ?? "EUR" }).ToList();
        return new ConsentAccess { Accounts = N(a.Accounts), Balances = N(a.Balances), Transactions = N(a.Transactions) };
    }

    /// <summary>Get a consent.</summary>
    [HttpGet("consents/{consentId}")]
    [ProducesResponseType(typeof(Consent), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetConsent(string consentId)
    {
        if (!state.Consents.TryGetValue(consentId, out var c)) return NotFoundProblem("Consent", consentId);
        ApplyExpiry(c);
        return Ok(c);
    }

    /// <summary>Get the consent status.</summary>
    [HttpGet("consents/{consentId}/status")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetConsentStatus(string consentId)
    {
        if (!state.Consents.TryGetValue(consentId, out var c)) return NotFoundProblem("Consent", consentId);
        ApplyExpiry(c);
        return Ok(new { consentStatus = c.ConsentStatus });
    }

    /// <summary>Sandbox SCA: authorise (or reject) the consent as the PSU. APPROVE → consentStatus valid.</summary>
    [HttpPost("consents/{consentId}/authorisations")]
    [Consumes("application/json")]
    [ProducesResponseType(201)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public IActionResult Authorise(string consentId, [FromBody] ConsentAuthorisationRequest request)
    {
        lock (state.Sync)
        {
            if (!state.Consents.TryGetValue(consentId, out var c)) return NotFoundProblem("Consent", consentId);
            if (c.ConsentStatus != "received")
                return ConflictProblem("CONSENT_ALREADY_PROCESSED", $"Consent is already '{c.ConsentStatus}'.");
            var approved = request.Outcome == "APPROVE";
            c.ConsentStatus = approved ? "valid" : "rejected";
            var authorisationId = state.NextId("auth");
            return Created($"{Base}/consents/{consentId}/authorisations/{authorisationId}", new
            {
                authorisationId,
                scaStatus = approved ? "finalised" : "failed",
                consentStatus = c.ConsentStatus,
                chosenScaMethod = new { authenticationType = "PUSH_OTP", name = "Sandbox bank app" },
            });
        }
    }

    /// <summary>Revoke a consent (consentStatus = terminatedByTpp).</summary>
    [HttpDelete("consents/{consentId}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult DeleteConsent(string consentId)
    {
        if (!state.Consents.TryGetValue(consentId, out var c)) return NotFoundProblem("Consent", consentId);
        c.ConsentStatus = "terminatedByTpp";
        return NoContent();
    }

    /// <summary>List accounts covered by the consent.</summary>
    /// <response code="401">CONSENT_INVALID or CONSENT_EXPIRED.</response>
    /// <response code="403">CONSENT_UNKNOWN.</response>
    [HttpGet("accounts")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public IActionResult ListAccounts([FromHeader(Name = "Consent-ID")] string? consentId)
    {
        var (consent, error) = Check(consentId);
        if (error is not null) return error;
        var ibans = consent!.Access.Accounts.Concat(consent.Access.Balances).Concat(consent.Access.Transactions)
            .Select(a => a.Iban).Distinct();
        return Ok(new { accounts = ibans.Select(i => state.FindByIban(i)).OfType<Account>().Select(ToXs2a) });
    }

    /// <summary>Read balances (closingBooked, interimAvailable).</summary>
    [HttpGet("accounts/{accountId}/balances")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetBalances(string accountId, [FromHeader(Name = "Consent-ID")] string? consentId)
    {
        var (consent, error) = Check(consentId);
        if (error is not null) return error;
        if (!state.Accounts.TryGetValue(accountId, out var account)) return NotFoundProblem("Account", accountId);
        if (!consent!.Access.Balances.Any(b => b.Iban == account.Iban))
            return Problem(403, "CONSENT_INVALID", "The consent does not grant balance access for this account.");

        var lastBookingDate = account.Transactions.Count > 0 ? account.Transactions[^1].BookingDate : state.Today;
        return Ok(new
        {
            account = new { iban = account.Iban, currency = account.Currency },
            balances = new object[]
            {
                new { balanceAmount = Amount(account.Balance), balanceType = "closingBooked", referenceDate = lastBookingDate },
                new { balanceAmount = Amount(account.Balance), balanceType = "interimAvailable", lastChangeDateTime = state.Now },
            },
        });
    }

    /// <summary>Read transactions. bookingStatus: booked (default), pending or both.</summary>
    [HttpGet("accounts/{accountId}/transactions")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetTransactions(string accountId, [FromHeader(Name = "Consent-ID")] string? consentId,
        [FromQuery] string bookingStatus = "booked", [FromQuery] DateOnly? dateFrom = null, [FromQuery] DateOnly? dateTo = null)
    {
        var (consent, error) = Check(consentId);
        if (error is not null) return error;
        if (bookingStatus is not ("booked" or "pending" or "both"))
            return FieldProblem(nameof(bookingStatus), "bookingStatus must be booked, pending or both.");
        if (!state.Accounts.TryGetValue(accountId, out var account)) return NotFoundProblem("Account", accountId);
        if (!consent!.Access.Transactions.Any(b => b.Iban == account.Iban))
            return Problem(403, "CONSENT_INVALID", "The consent does not grant transaction access for this account.");

        var from = dateFrom ?? state.Today.AddDays(-90);
        var to = dateTo ?? state.Today.AddDays(5);
        var booked = account.Transactions.Where(t => t.BookingDate >= from && t.BookingDate <= to).Reverse().Select(t =>
        {
            var outgoing = t.CreditDebitIndicator == CreditDebit.DBIT;
            return new
            {
                transactionId = t.Id,
                endToEndId = t.EndToEndId,
                bookingDate = t.BookingDate,
                valueDate = t.ValueDate,
                transactionAmount = Amount(outgoing ? -t.Amount : t.Amount),
                creditorName = outgoing ? t.CounterpartyName : account.OwnerName,
                creditorAccount = new { iban = outgoing ? t.CounterpartyIban : account.Iban },
                debtorName = outgoing ? account.OwnerName : t.CounterpartyName,
                debtorAccount = new { iban = outgoing ? account.Iban : t.CounterpartyIban },
                remittanceInformationUnstructured = t.RemittanceInformation,
                bankTransactionCode = t.BankTransactionCode,
            };
        });

        return Ok(new
        {
            account = new { iban = account.Iban, currency = account.Currency },
            transactions = new
            {
                booked = booked.Where(_ => bookingStatus != "pending").ToArray(),
                pending = Array.Empty<object>(),
                _links = new { account = new Link($"{Base}/accounts/{account.Id}") },
            },
        });
    }

    private object ToXs2a(Account a) => new
    {
        resourceId = a.Id,
        iban = a.Iban,
        bic = a.Bic,
        currency = a.Currency,
        name = a.Name,
        ownerName = a.OwnerName,
        product = a.Name,
        cashAccountType = a.Product is AccountProduct.SAVINGS or AccountProduct.TERM_DEPOSIT ? "SVGS" : "CACC",
        status = "enabled",
        _links = new
        {
            balances = new Link($"{Base}/accounts/{a.Id}/balances"),
            transactions = new Link($"{Base}/accounts/{a.Id}/transactions"),
        },
    };

    /// <summary>Berlin Group amounts are strings with two decimals.</summary>
    private static object Amount(decimal v) => new { currency = "EUR", amount = v.ToString("0.00", CultureInfo.InvariantCulture) };

    private void ApplyExpiry(Consent c)
    {
        if (c.ConsentStatus == "valid" && c.ValidUntil < state.Today) c.ConsentStatus = "expired";
    }

    private (Consent?, IActionResult?) Check(string? consentId)
    {
        if (string.IsNullOrWhiteSpace(consentId))
            return (null, Problem(400, "FORMAT_ERROR", "Header 'Consent-ID' is required."));
        if (!state.Consents.TryGetValue(consentId, out var c))
            return (null, Problem(403, "CONSENT_UNKNOWN", "The Consent-ID is not known."));
        ApplyExpiry(c);
        return c.ConsentStatus switch
        {
            "valid" => (c, null),
            "expired" => (null, Problem(401, "CONSENT_EXPIRED", "The consent has expired.")),
            _ => (null, Problem(401, "CONSENT_INVALID", $"The consent is '{c.ConsentStatus}', not 'valid'. Complete the authorisation first.")),
        };
    }
}
