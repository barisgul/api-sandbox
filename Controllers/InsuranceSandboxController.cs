using AuthSandbox.Domain.Insurance;
using AuthSandbox.Infrastructure.Demo;
using Microsoft.AspNetCore.Mvc;

namespace AuthSandbox.Controllers;

/// <summary>Insurance demo (EU): quote → policy → claim (multipart) → assessment → payout.</summary>
[Route("api/sandbox/insurance")]
[ApiExplorerSettings(GroupName = "v1-insurance")]
[Tags("Insurance")]
public class InsuranceSandboxController(InsuranceState state) : DemoControllerBase
{
    private const decimal AutoApproveLimit = 5000m;

    /// <summary>Reset insurance data to the deterministic seed (idempotent).</summary>
    [HttpPost("reset")]
    public IActionResult Reset()
    {
        state.Reset();
        return Ok(new { domain = "insurance", status = "RESET", fixtures = state.Fixtures });
    }

    /// <summary>Step 1 — Request a quote (car, home contents or travel). Valid for 30 days.</summary>
    /// <response code="422">MINIMUM_AGE_NOT_MET, START_DATE_IN_PAST, START_DATE_TOO_FAR, COVERAGE_NOT_AVAILABLE, RISK_ADDRESS_NOT_DOMESTIC.</response>
    [HttpPost("quotes")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Quote), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public IActionResult CreateQuote([FromBody] QuoteRequest request)
    {
        var holder = request.Policyholder;
        var start = request.StartDate!.Value;
        var age = Age(holder.DateOfBirth!.Value, start);
        if (age < 18) return RuleProblem("MINIMUM_AGE_NOT_MET", "The policyholder must be at least 18 years old.");
        if (start < state.Today) return RuleProblem("START_DATE_IN_PAST", "startDate cannot be in the past.");
        if (start > state.Today.AddDays(365)) return RuleProblem("START_DATE_TOO_FAR", "startDate may be at most 12 months ahead.");
        if (request.Product == InsuranceProduct.CAR && request.Car!.Coverage == CarCoverage.ALL_RISK && start.Year - request.Car.BuildYear > 8)
            return RuleProblem("COVERAGE_NOT_AVAILABLE", "ALL_RISK is only available for cars up to 8 years old; choose WA_PLUS.");
        if (request.Product == InsuranceProduct.HOME_CONTENTS && holder.Address.Country != "NL")
            return RuleProblem("RISK_ADDRESS_NOT_DOMESTIC", "Home contents insurance requires a domestic risk address.");

        lock (state.Sync)
        {
            var quote = new Quote
            {
                Id = state.NextId("quo"),
                Product = request.Product,
                Status = QuoteStatus.QUOTED,
                PolicyholderName = $"{holder.FirstName} {holder.LastName}",
                Policyholder = holder,
                StartDate = start,
                Deductible = request.Deductible,
                CoverageLimit = request.Product switch
                {
                    InsuranceProduct.CAR => request.Car!.CatalogValue,
                    InsuranceProduct.HOME_CONTENTS => request.HomeContents!.InsuredAmount,
                    _ => 10000m * request.Travel!.Persons,
                },
                CoveredCategories = InsurancePricing.Categories(request.Product, request.Car?.Coverage),
                Details = (object?)request.Car ?? (object?)request.HomeContents ?? request.Travel,
                Premium = InsurancePricing.Gross(InsurancePricing.MonthlyNet(request, age), request.PaymentFrequency),
                CreatedAt = state.Now,
                ValidUntil = state.Now.AddDays(30),
            };
            holder.Address = holder.Address.Normalized();
            state.Quotes[quote.Id] = quote;
            return Created($"/api/sandbox/insurance/quotes/{quote.Id}", quote);
        }
    }

    /// <summary>Get a quote.</summary>
    [HttpGet("quotes/{quoteId}")]
    [ProducesResponseType(typeof(Quote), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetQuote(string quoteId)
    {
        if (!state.Quotes.TryGetValue(quoteId, out var q)) return NotFoundProblem("Quote", quoteId);
        if (q.Status == QuoteStatus.QUOTED && state.Now > q.ValidUntil) q.Status = QuoteStatus.EXPIRED;
        return Ok(q);
    }

    /// <summary>Step 2 — Accept a quote and issue the policy (with SEPA direct debit mandate).</summary>
    /// <response code="409">QUOTE_ALREADY_ACCEPTED.</response>
    /// <response code="422">QUOTE_EXPIRED or TERMS_NOT_ACCEPTED.</response>
    [HttpPost("policies")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Policy), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public IActionResult IssuePolicy([FromBody] IssuePolicyRequest request)
    {
        lock (state.Sync)
        {
            if (!state.Quotes.TryGetValue(request.QuoteId, out var quote)) return NotFoundProblem("Quote", request.QuoteId);
            if (quote.Status == QuoteStatus.ACCEPTED)
                return ConflictProblem("QUOTE_ALREADY_ACCEPTED", "This quote has already been turned into a policy.");
            if (quote.Status == QuoteStatus.EXPIRED || state.Now > quote.ValidUntil)
            {
                quote.Status = QuoteStatus.EXPIRED;
                return RuleProblem("QUOTE_EXPIRED", "The quote is no longer valid; request a new quote.", new { quote.ValidUntil });
            }
            if (!request.AcceptTerms) return RuleProblem("TERMS_NOT_ACCEPTED", "The policy conditions must be accepted (acceptTerms = true).");

            var year = quote.StartDate.Year;
            var number = $"POL-{year}-{state.NextNumber($"POL-{year}"):D6}";
            var policy = new Policy
            {
                Id = state.NextId("pol"),
                PolicyNumber = number,
                QuoteId = quote.Id,
                Product = quote.Product,
                Status = PolicyStatus.ACTIVE,
                PolicyholderName = quote.PolicyholderName,
                StartDate = quote.StartDate,
                EndDate = quote.StartDate.AddYears(1).AddDays(-1),
                CoolingOffUntil = state.Today.AddDays(14),
                Deductible = quote.Deductible,
                CoverageLimit = quote.CoverageLimit,
                CoveredCategories = quote.CoveredCategories,
                Premium = quote.Premium,
                Mandate = new SepaMandate
                {
                    MandateId = $"MNDT-{number}", Iban = Validators.NormalizeIban(request.Iban),
                    AccountHolderName = request.AccountHolderName, SignatureDate = state.Today,
                },
                FirstCollectionDate = quote.StartDate < state.Today.AddDays(5) ? state.Today.AddDays(5) : quote.StartDate,
                IssuedAt = state.Now,
            };
            quote.Status = QuoteStatus.ACCEPTED;
            state.Policies[policy.Id] = policy;
            return Created($"/api/sandbox/insurance/policies/{policy.Id}", policy);
        }
    }

    /// <summary>Get a policy.</summary>
    [HttpGet("policies/{policyId}")]
    [ProducesResponseType(typeof(Policy), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetPolicy(string policyId) =>
        state.Policies.TryGetValue(policyId, out var p) ? Ok(p) : NotFoundProblem("Policy", policyId);

    /// <summary>Cancel a policy. Within the 14-day cooling-off period the full premium is refunded, otherwise pro rata.</summary>
    [HttpPost("policies/{policyId}/cancel")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Policy), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public IActionResult CancelPolicy(string policyId, [FromBody] CancelPolicyRequest request)
    {
        lock (state.Sync)
        {
            if (!state.Policies.TryGetValue(policyId, out var p)) return NotFoundProblem("Policy", policyId);
            if (p.Status == PolicyStatus.CANCELLED) return ConflictProblem("POLICY_ALREADY_CANCELLED", "The policy is already cancelled.");
            var effective = request.EffectiveDate ?? state.Today;
            if (effective < state.Today) return FieldProblem(nameof(request.EffectiveDate), "effectiveDate cannot be in the past.");

            var annual = p.Premium.AnnualGrossPremium;
            var refund = state.Today <= p.CoolingOffUntil
                ? (p.Premium.Frequency == PaymentFrequency.YEARLY ? annual : p.Premium.GrossPremium)
                : p.Premium.Frequency == PaymentFrequency.YEARLY
                    ? Validators.Money(annual * Math.Max(0, p.EndDate.DayNumber - effective.DayNumber) / 365m)
                    : 0m;
            p.Status = PolicyStatus.CANCELLED;
            p.CancelledAt = effective;
            p.RefundAmount = refund;
            return Ok(p);
        }
    }

    /// <summary>Step 3 — File a claim with an attachment (multipart/form-data).</summary>
    /// <response code="409">POLICY_NOT_ACTIVE.</response>
    /// <response code="422">INCIDENT_OUTSIDE_COVERAGE, INCIDENT_IN_FUTURE, CATEGORY_NOT_COVERED, POLICE_REPORT_REQUIRED, AMOUNT_EXCEEDS_COVERAGE, file errors.</response>
    [HttpPost("policies/{policyId}/claims")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [ProducesResponseType(typeof(Claim), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<IActionResult> FileClaim(string policyId, [FromForm] ClaimForm form)
    {
        if (!state.Policies.TryGetValue(policyId, out var policy)) return NotFoundProblem("Policy", policyId);
        if (policy.Status != PolicyStatus.ACTIVE) return ConflictProblem("POLICY_NOT_ACTIVE", "Claims can only be filed on an active policy.");

        var incident = form.IncidentDate!.Value;
        if (incident > state.Today) return RuleProblem("INCIDENT_IN_FUTURE", "incidentDate cannot be in the future.");
        if (incident < policy.StartDate || incident > policy.EndDate)
            return RuleProblem("INCIDENT_OUTSIDE_COVERAGE", "The incident happened outside the policy period.",
                new { policy.StartDate, policy.EndDate });
        var category = form.Category!.Value;
        if (!policy.CoveredCategories.Contains(category.ToString()))
            return RuleProblem("CATEGORY_NOT_COVERED", $"{category} is not covered by this policy.", new { policy.CoveredCategories });
        if (category == ClaimCategory.THEFT && string.IsNullOrWhiteSpace(form.PoliceReportNumber))
            return RuleProblem("POLICE_REPORT_REQUIRED", "Theft claims require a police report number (proces-verbaal).");
        if (form.ClaimedAmount > policy.CoverageLimit)
            return RuleProblem("AMOUNT_EXCEEDS_COVERAGE", "The claimed amount exceeds the insured amount.", new { policy.CoverageLimit });

        var (upload, error) = await ReadUploadAsync(form.Attachment!, "attachment");
        if (error is not null) return error;

        lock (state.Sync)
        {
            var claim = new Claim
            {
                Id = state.NextId("clm"),
                ClaimNumber = $"CLM-{state.Today.Year}-{state.NextNumber($"CLM-{state.Today.Year}"):D6}",
                PolicyId = policy.Id,
                PolicyNumber = policy.PolicyNumber,
                Status = ClaimStatus.SUBMITTED,
                Category = category,
                IncidentDate = incident,
                Description = form.Description,
                ClaimedAmount = Validators.Money(form.ClaimedAmount),
                Deductible = policy.Deductible,
                PoliceReportNumber = form.PoliceReportNumber,
                Attachments = [upload!],
                SubmittedAt = state.Now,
                History = [new ClaimEvent { Status = ClaimStatus.SUBMITTED, At = state.Now, Note = "Claim received." }],
            };
            state.Claims[claim.Id] = claim;
            return Created($"/api/sandbox/insurance/claims/{claim.Id}", claim);
        }
    }

    /// <summary>Get a claim.</summary>
    [HttpGet("claims/{claimId}")]
    [ProducesResponseType(typeof(Claim), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetClaim(string claimId) =>
        state.Claims.TryGetValue(claimId, out var c) ? Ok(c) : NotFoundProblem("Claim", claimId);

    /// <summary>Step 5 — Claim status with its history (poll target for flows).</summary>
    [HttpGet("claims/{claimId}/status")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetClaimStatus(string claimId) =>
        state.Claims.TryGetValue(claimId, out var c)
            ? Ok(new { c.Id, c.ClaimNumber, c.Status, c.ApprovedAmount, c.PayoutAmount, c.History })
            : NotFoundProblem("Claim", claimId);

    /// <summary>Step 4 — Assess the claim (sandbox adjuster). AUTO approves up to €5,000, otherwise IN_REVIEW.</summary>
    /// <response code="409">CLAIM_ALREADY_DECIDED.</response>
    [HttpPost("claims/{claimId}/assessment")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Claim), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public IActionResult AssessClaim(string claimId, [FromBody] AssessClaimRequest request)
    {
        lock (state.Sync)
        {
            if (!state.Claims.TryGetValue(claimId, out var c)) return NotFoundProblem("Claim", claimId);
            if (c.Status is not (ClaimStatus.SUBMITTED or ClaimStatus.IN_REVIEW))
                return ConflictProblem("CLAIM_ALREADY_DECIDED", $"Claim is already {c.Status}.");

            var decision = request.Decision;
            if (decision == ClaimDecision.AUTO && c.ClaimedAmount > AutoApproveLimit)
            {
                c.Status = ClaimStatus.IN_REVIEW;
                c.History.Add(new ClaimEvent { Status = c.Status, At = state.Now, Note = request.Note ?? "Above auto-approval limit; assigned to a loss adjuster." });
                return Ok(c);
            }

            if (decision == ClaimDecision.REJECT)
            {
                c.Status = ClaimStatus.REJECTED;
                c.History.Add(new ClaimEvent { Status = c.Status, At = state.Now, Note = request.Note ?? "Claim rejected." });
                return Ok(c);
            }

            var approved = Math.Min(request.ApprovedAmount ?? c.ClaimedAmount, c.ClaimedAmount);
            c.ApprovedAmount = Validators.Money(approved);
            c.PayoutAmount = Validators.Money(Math.Max(0, approved - c.Deductible));
            c.Status = ClaimStatus.APPROVED;
            c.History.Add(new ClaimEvent { Status = c.Status, At = state.Now, Note = request.Note ?? $"Approved; deductible €{c.Deductible} applied." });
            return Ok(c);
        }
    }

    /// <summary>Pay out an approved claim by SEPA credit transfer to the mandate IBAN.</summary>
    /// <response code="409">CLAIM_NOT_APPROVED.</response>
    [HttpPost("claims/{claimId}/payout")]
    [ProducesResponseType(typeof(Claim), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public IActionResult PayClaim(string claimId)
    {
        lock (state.Sync)
        {
            if (!state.Claims.TryGetValue(claimId, out var c)) return NotFoundProblem("Claim", claimId);
            if (c.Status != ClaimStatus.APPROVED) return ConflictProblem("CLAIM_NOT_APPROVED", $"Only APPROVED claims can be paid (current: {c.Status}).");
            c.PayoutIban = state.Policies[c.PolicyId].Mandate.Iban;
            c.Status = ClaimStatus.PAID;
            c.History.Add(new ClaimEvent { Status = c.Status, At = state.Now, Note = $"€{c.PayoutAmount:0.00} paid by SEPA Instant to {c.PayoutIban}." });
            return Ok(c);
        }
    }

    private static int Age(DateOnly dob, DateOnly at)
    {
        var age = at.Year - dob.Year;
        if (dob.AddYears(age) > at) age--;
        return age;
    }
}
