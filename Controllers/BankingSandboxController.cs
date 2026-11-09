using AuthSandbox.Domain.Banking;
using AuthSandbox.Infrastructure.Demo;
using Microsoft.AspNetCore.Mvc;

namespace AuthSandbox.Controllers;

/// <summary>
/// Retail banking demo: onboarding (personal details + postal address) → KYC/CDD with document upload →
/// account opening (IBAN) → Verification of Payee → SEPA (Instant) Credit Transfer →
/// iDEAL-style payment requests → camt.053-style statements.
/// </summary>
[Route("api/sandbox/banking")]
[ApiExplorerSettings(GroupName = "v1-banking")]
[Tags("Banking")]
public class BankingSandboxController(BankingState state) : DemoControllerBase
{
    /// <summary>Reset all banking data to the deterministic seed (idempotent).</summary>
    [HttpPost("reset")]
    public IActionResult Reset()
    {
        state.Reset();
        return Ok(new { domain = "banking", status = "RESET", fixtures = state.Fixtures });
    }

    // ------------------------------------------------------------------ Customers

    /// <summary>Step 1 — Onboard a customer (personal details, postal address).</summary>
    /// <response code="201">Customer created with kycStatus NOT_STARTED.</response>
    /// <response code="409">Email address already registered (code EMAIL_ALREADY_REGISTERED).</response>
    /// <response code="422">Customer is under 18 (code MINIMUM_AGE_NOT_MET).</response>
    [HttpPost("customers")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Customer), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public IActionResult CreateCustomer([FromBody] CreateCustomerRequest request)
    {
        lock (state.Sync)
        {
            if (state.Customers.Values.Any(c => c.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase)))
                return ConflictProblem("EMAIL_ALREADY_REGISTERED", "A customer with this email address already exists.");

            var dob = request.DateOfBirth!.Value;
            if (dob.AddYears(18) > state.Today)
                return RuleProblem("MINIMUM_AGE_NOT_MET", "Customers must be at least 18 years old to open a retail account.");

            var customer = new Customer
            {
                Id = state.NextId("cus"),
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                DateOfBirth = dob,
                Email = request.Email,
                Phone = request.Phone,
                Nationality = request.Nationality,
                Address = request.Address.Normalized(),
                KycStatus = KycStatus.NOT_STARTED,
                CreatedAt = state.Now,
            };
            state.Customers[customer.Id] = customer;
            return Created($"/api/sandbox/banking/customers/{customer.Id}", customer);
        }
    }

    /// <summary>Get a customer.</summary>
    [HttpGet("customers/{customerId}")]
    [ProducesResponseType(typeof(Customer), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetCustomer(string customerId) =>
        state.Customers.TryGetValue(customerId, out var c) ? Ok(c) : NotFoundProblem("Customer", customerId);

    /// <summary>
    /// Step 2 — Submit KYC / CDD with an identity document upload (multipart/form-data).
    /// </summary>
    /// <remarks>
    /// Screening triggers for negative demos (based on the customer's last name):
    /// "Sanction" → REJECTED (sanctions hit), "Pep" → MANUAL_REVIEW with ENHANCED due diligence.
    /// Upload only synthetic documents.
    /// </remarks>
    /// <response code="200">KYC case decided.</response>
    /// <response code="409">KYC already approved (KYC_ALREADY_APPROVED).</response>
    /// <response code="422">Document expired, too large, wrong type or not accepted for nationality.</response>
    [HttpPost("customers/{customerId}/kyc")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [ProducesResponseType(typeof(KycCase), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<IActionResult> SubmitKyc(string customerId, [FromForm] KycSubmissionForm form)
    {
        if (!state.Customers.TryGetValue(customerId, out var customer)) return NotFoundProblem("Customer", customerId);
        if (customer.KycStatus == KycStatus.APPROVED)
            return ConflictProblem("KYC_ALREADY_APPROVED", "Customer due diligence is already completed.", new { customer.KycCaseId });

        var (upload, uploadError) = await ReadUploadAsync(form.Document!, "document");
        if (uploadError is not null) return uploadError;

        if (form.ExpiryDate!.Value < state.Today)
            return RuleProblem("DOCUMENT_EXPIRED", "The identity document has expired.", new { form.ExpiryDate });

        var isEuEea = Validators.EuCountries.Contains(customer.Nationality) || customer.Nationality is "IS" or "LI" or "NO";
        if (form.DocumentType == IdentityDocumentType.ID_CARD && !isEuEea)
            return RuleProblem("DOCUMENT_TYPE_NOT_ACCEPTED",
                "A national ID card is only accepted for EU/EEA nationals; provide a PASSPORT or RESIDENCE_PERMIT.");

        var kyc = new KycCase
        {
            Id = state.NextId("kyc"),
            CustomerId = customer.Id,
            SubmittedAt = state.Now,
            DecidedAt = state.Now,
            Document = new KycDocument
            {
                Type = form.DocumentType!.Value,
                NumberMasked = Text.Mask(form.DocumentNumber.ToUpperInvariant(), 3),
                IssuingCountry = form.IssuingCountry,
                ExpiryDate = form.ExpiryDate.Value,
                FileName = upload!.FileName,
                ContentType = upload.ContentType,
                SizeBytes = upload.SizeBytes,
                Sha256 = upload.Sha256,
            },
        };

        switch (customer.LastName.ToLowerInvariant())
        {
            case "sanction":
                kyc.Status = KycStatus.REJECTED; kyc.RiskRating = RiskRating.HIGH; kyc.CddLevel = CddLevel.ENHANCED;
                kyc.Screening.SanctionsMatch = true;
                kyc.Reasons.Add("Name matches an entry on the EU consolidated sanctions list (simulated).");
                break;
            case "pep":
                kyc.Status = KycStatus.MANUAL_REVIEW; kyc.RiskRating = RiskRating.HIGH; kyc.CddLevel = CddLevel.ENHANCED;
                kyc.DecidedAt = null; kyc.Screening.PepMatch = true;
                kyc.Reasons.Add("Politically exposed person — enhanced due diligence required (AMLD Art. 20).");
                break;
            default:
                kyc.Status = KycStatus.APPROVED;
                kyc.RiskRating = isEuEea ? RiskRating.LOW : RiskRating.MEDIUM;
                kyc.CddLevel = CddLevel.STANDARD;
                kyc.Reasons.Add("Identity verified; no screening hits.");
                break;
        }

        lock (state.Sync)
        {
            state.KycCases[kyc.Id] = kyc;
            customer.KycStatus = kyc.Status;
            customer.KycCaseId = kyc.Id;
        }
        return Ok(kyc);
    }

    /// <summary>Get the latest KYC case of a customer.</summary>
    [HttpGet("customers/{customerId}/kyc")]
    [ProducesResponseType(typeof(KycCase), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetKyc(string customerId)
    {
        if (!state.Customers.TryGetValue(customerId, out var c)) return NotFoundProblem("Customer", customerId);
        return c.KycCaseId is not null && state.KycCases.TryGetValue(c.KycCaseId, out var k)
            ? Ok(k) : NotFoundProblem("KycCase", customerId);
    }

    // ------------------------------------------------------------------ Accounts

    /// <summary>Step 3 — Open an account by type: CURRENT, SAVINGS, TERM_DEPOSIT or BUSINESS (generates a valid IBAN).</summary>
    /// <remarks>
    /// SAVINGS and TERM_DEPOSIT are linked to one of the customer's CURRENT accounts. A TERM_DEPOSIT needs
    /// <c>termMonths</c> (3, 6, 12, 24) and an <c>openingAmount</c> of at least €1,000, moved from the linked account.
    /// </remarks>
    /// <response code="422">KYC_NOT_APPROVED, CURRENCY_NOT_SUPPORTED, CURRENT_ACCOUNT_REQUIRED, INVALID_TERM, MINIMUM_OPENING_AMOUNT, INSUFFICIENT_FUNDS.</response>
    [HttpPost("accounts")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Account), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public IActionResult OpenAccount([FromBody] OpenAccountRequest request)
    {
        var bankCode = request.BankCode.ToUpperInvariant();
        if (!Validators.NlBankBics.TryGetValue(bankCode, out var bic))
            return FieldProblem(nameof(request.BankCode), $"Unknown bank code. Use one of: {string.Join(", ", Validators.NlBankBics.Keys)}.");

        lock (state.Sync)
        {
            if (!state.Customers.TryGetValue(request.CustomerId, out var customer)) return NotFoundProblem("Customer", request.CustomerId);
            if (customer.KycStatus != KycStatus.APPROVED)
                return RuleProblem("KYC_NOT_APPROVED", "Customer due diligence must be APPROVED before opening an account.",
                    new { customer.KycStatus });
            if (request.Currency != "EUR")
                return RuleProblem("CURRENCY_NOT_SUPPORTED", "Retail accounts are held in EUR only.");

            Account? linked = null;
            var needsLink = request.Product is AccountProduct.SAVINGS or AccountProduct.TERM_DEPOSIT;
            if (needsLink)
            {
                linked = request.LinkedAccountId is null
                    ? state.Accounts.Values.FirstOrDefault(a => a.CustomerId == customer.Id && a.Product == AccountProduct.CURRENT && a.Status == AccountStatus.ACTIVE)
                    : state.Accounts.GetValueOrDefault(request.LinkedAccountId);
                if (linked is null || linked.CustomerId != customer.Id || linked.Product != AccountProduct.CURRENT || linked.Status != AccountStatus.ACTIVE)
                    return RuleProblem("CURRENT_ACCOUNT_REQUIRED", $"A {request.Product} account must be linked to an active CURRENT account of the same customer.");
            }

            DateOnly? maturity = null;
            decimal? rate = null;
            if (request.Product == AccountProduct.TERM_DEPOSIT)
            {
                if (request.TermMonths is not (3 or 6 or 12 or 24))
                    return RuleProblem("INVALID_TERM", "termMonths must be 3, 6, 12 or 24 for a TERM_DEPOSIT.");
                if (request.OpeningAmount is null or < 1000m)
                    return RuleProblem("MINIMUM_OPENING_AMOUNT", "A TERM_DEPOSIT needs an openingAmount of at least €1,000.");
                if (linked!.Balance < request.OpeningAmount)
                    return RuleProblem("INSUFFICIENT_FUNDS", "The linked account balance is too low for the opening amount.", new { availableBalance = linked.Balance });
                maturity = state.Today.AddMonths(request.TermMonths.Value);
                rate = request.TermMonths switch { 3 => 1.90m, 6 => 2.10m, 12 => 2.40m, _ => 2.60m };
            }
            else if (request.Product == AccountProduct.SAVINGS) rate = 1.75m;

            var account = new Account
            {
                Id = state.NextId("acc"),
                CustomerId = customer.Id,
                Product = request.Product,
                Name = request.Name ?? request.Product switch
                {
                    AccountProduct.SAVINGS => "Savings account",
                    AccountProduct.TERM_DEPOSIT => $"Term deposit {request.TermMonths} months",
                    AccountProduct.BUSINESS => "Business account",
                    _ => "Current account",
                },
                OwnerName = customer.FullName,
                Iban = Validators.BuildNlIban(bankCode, state.NextAccountNumber()),
                Bic = bic,
                Currency = "EUR",
                InterestRate = rate,
                LinkedAccountId = linked?.Id,
                TermMonths = request.Product == AccountProduct.TERM_DEPOSIT ? request.TermMonths : null,
                MaturityDate = maturity,
                OpenedAt = state.Now,
            };
            state.Accounts[account.Id] = account;
            if (request.Product == AccountProduct.TERM_DEPOSIT)
                MoveMoney(linked!, account, request.OpeningAmount!.Value, "Term deposit opening");
            return Created($"/api/sandbox/banking/accounts/{account.Id}", account);
        }
    }

    /// <summary>Get an account with its current balance.</summary>
    [HttpGet("accounts/{accountId}")]
    [ProducesResponseType(typeof(Account), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetAccount(string accountId) =>
        state.Accounts.TryGetValue(accountId, out var a) ? Ok(a) : NotFoundProblem("Account", accountId);

    /// <summary>List all accounts of a customer.</summary>
    [HttpGet("customers/{customerId}/accounts")]
    [ProducesResponseType(typeof(IEnumerable<Account>), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult ListCustomerAccounts(string customerId) =>
        state.Customers.ContainsKey(customerId)
            ? Ok(state.Accounts.Values.Where(a => a.CustomerId == customerId))
            : NotFoundProblem("Customer", customerId);

    /// <summary>Step 4 — Add balance: book an incoming credit (sandbox top-up) on the account.</summary>
    /// <response code="409">ACCOUNT_CLOSED.</response>
    /// <response code="422">DEPOSIT_NOT_ALLOWED (term deposits are funded once, at opening).</response>
    [HttpPost("accounts/{accountId}/deposits")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BankTransaction), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public IActionResult Deposit(string accountId, [FromBody] DepositRequest request)
    {
        lock (state.Sync)
        {
            if (!state.Accounts.TryGetValue(accountId, out var account)) return NotFoundProblem("Account", accountId);
            if (account.Status == AccountStatus.CLOSED) return ConflictProblem("ACCOUNT_CLOSED", "The account is closed.");
            if (account.Product == AccountProduct.TERM_DEPOSIT)
                return RuleProblem("DEPOSIT_NOT_ALLOWED", "Term deposits are funded once, at opening.");
            var tx = state.Book(account, Validators.Money(request.Amount), CreditDebit.CRDT, state.Today, "Quixa Sandbox Funding",
                "NL20INGB0001234567", request.Description ?? "Sandbox top-up", "PMNT-RCDT-ESCT");
            return Created($"/api/sandbox/banking/accounts/{accountId}/transactions/{tx.Id}", tx);
        }
    }

    /// <summary>Withdraw money from an account (cash / ATM).</summary>
    /// <response code="409">ACCOUNT_CLOSED.</response>
    /// <response code="422">INSUFFICIENT_FUNDS, WITHDRAWAL_NOT_ALLOWED (savings), TERM_DEPOSIT_LOCKED.</response>
    [HttpPost("accounts/{accountId}/withdrawals")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BankTransaction), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public IActionResult Withdraw(string accountId, [FromBody] WithdrawalRequest request)
    {
        lock (state.Sync)
        {
            if (!state.Accounts.TryGetValue(accountId, out var account)) return NotFoundProblem("Account", accountId);
            if (account.Status == AccountStatus.CLOSED) return ConflictProblem("ACCOUNT_CLOSED", "The account is closed.");
            if (account.Product == AccountProduct.SAVINGS)
                return RuleProblem("WITHDRAWAL_NOT_ALLOWED", "Savings accounts have no cash access; transfer to the linked current account first.");
            if (account.Product == AccountProduct.TERM_DEPOSIT && state.Today < account.MaturityDate)
                return RuleProblem("TERM_DEPOSIT_LOCKED", "Funds are locked until the maturity date.", new { account.MaturityDate });
            var amount = Validators.Money(request.Amount);
            if (amount > account.Balance)
                return RuleProblem("INSUFFICIENT_FUNDS", "The balance is too low for this withdrawal.", new { availableBalance = account.Balance });
            var tx = state.Book(account, amount, CreditDebit.DBIT, state.Today, "ATM Sandbox", null, request.Description ?? "Cash withdrawal", "PMNT-CCRD-CWDL");
            return Created($"/api/sandbox/banking/accounts/{accountId}/transactions/{tx.Id}", tx);
        }
    }

    /// <summary>List booked transactions of an account (newest first).</summary>
    [HttpGet("accounts/{accountId}/transactions")]
    [ProducesResponseType(typeof(IEnumerable<BankTransaction>), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetTransactions(string accountId) =>
        state.Accounts.TryGetValue(accountId, out var a)
            ? Ok(a.Transactions.AsEnumerable().Reverse())
            : NotFoundProblem("Account", accountId);

    /// <summary>Close an account. The balance must be zero.</summary>
    /// <response code="409">ACCOUNT_CLOSED or ACCOUNT_HAS_BALANCE.</response>
    [HttpPost("accounts/{accountId}/close")]
    [ProducesResponseType(typeof(Account), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public IActionResult CloseAccount(string accountId)
    {
        lock (state.Sync)
        {
            if (!state.Accounts.TryGetValue(accountId, out var account)) return NotFoundProblem("Account", accountId);
            if (account.Status == AccountStatus.CLOSED) return ConflictProblem("ACCOUNT_CLOSED", "The account is already closed.");
            if (account.Balance != 0)
                return ConflictProblem("ACCOUNT_HAS_BALANCE", "Transfer or withdraw the remaining balance before closing.", new { account.Balance });
            account.Status = AccountStatus.CLOSED;
            account.ClosedAt = state.Now;
            return Ok(account);
        }
    }

    // ------------------------------------------------------------------ Internal transfers

    /// <summary>Step 5 — Transfer money between accounts at this bank (booked instantly).</summary>
    /// <remarks>
    /// Savings accounts can only send money to their linked current account. Term deposits can only pay out
    /// on or after the maturity date. Use SEPA credit transfers for accounts at other banks.
    /// </remarks>
    /// <response code="409">ACCOUNT_CLOSED.</response>
    /// <response code="422">SAME_ACCOUNT, INSUFFICIENT_FUNDS, SAVINGS_TRANSFER_RESTRICTED, TERM_DEPOSIT_LOCKED, DEPOSIT_NOT_ALLOWED.</response>
    [HttpPost("transfers")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(InternalTransfer), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public IActionResult Transfer([FromBody] InternalTransferRequest request)
    {
        lock (state.Sync)
        {
            if (!state.Accounts.TryGetValue(request.FromAccountId, out var from)) return NotFoundProblem("Account", request.FromAccountId);
            if (!state.Accounts.TryGetValue(request.ToAccountId, out var to)) return NotFoundProblem("Account", request.ToAccountId);
            if (from.Id == to.Id) return RuleProblem("SAME_ACCOUNT", "Source and destination account must differ.");
            if (from.Status == AccountStatus.CLOSED || to.Status == AccountStatus.CLOSED)
                return ConflictProblem("ACCOUNT_CLOSED", "One of the accounts is closed.");
            if (from.Product == AccountProduct.SAVINGS && to.Id != from.LinkedAccountId)
                return RuleProblem("SAVINGS_TRANSFER_RESTRICTED", "Savings accounts can only transfer to their linked current account.",
                    new { from.LinkedAccountId });
            if (from.Product == AccountProduct.TERM_DEPOSIT && state.Today < from.MaturityDate)
                return RuleProblem("TERM_DEPOSIT_LOCKED", "Funds are locked until the maturity date.", new { from.MaturityDate });
            if (to.Product == AccountProduct.TERM_DEPOSIT)
                return RuleProblem("DEPOSIT_NOT_ALLOWED", "Term deposits are funded once, at opening.");
            var amount = Validators.Money(request.Amount);
            if (amount > from.Balance)
                return RuleProblem("INSUFFICIENT_FUNDS", "The balance is too low for this transfer.", new { availableBalance = from.Balance });

            var transfer = MoveMoney(from, to, amount, request.Description);
            return Created($"/api/sandbox/banking/transfers/{transfer.Id}", transfer);
        }
    }

    /// <summary>Get an internal transfer.</summary>
    [HttpGet("transfers/{transferId}")]
    [ProducesResponseType(typeof(InternalTransfer), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetTransfer(string transferId) =>
        state.Transfers.TryGetValue(transferId, out var t) ? Ok(t) : NotFoundProblem("Transfer", transferId);

    private InternalTransfer MoveMoney(Account from, Account to, decimal amount, string? description)
    {
        var id = state.NextId("trf");
        state.Book(from, amount, CreditDebit.DBIT, state.Today, to.OwnerName, to.Iban, description, "ACMT-MCOP-INTR", id, id);
        state.Book(to, amount, CreditDebit.CRDT, state.Today, from.OwnerName, from.Iban, description, "ACMT-MCOP-INTR", id, id);
        var t = new InternalTransfer
        {
            Id = id, FromAccountId = from.Id, FromIban = from.Iban, ToAccountId = to.Id, ToIban = to.Iban, Amount = amount,
            Description = description, FromBalanceAfter = from.Balance, ToBalanceAfter = to.Balance, ExecutedAt = state.Now,
        };
        state.Transfers[id] = t;
        return t;
    }

    // ------------------------------------------------------------------ Verification of Payee

    /// <summary>Step 6 — Verification of Payee (IBAN-name check, EU Instant Payments Regulation).</summary>
    /// <remarks>Result is MATCH, CLOSE_MATCH (with suggestedName), NO_MATCH or NOT_POSSIBLE (IBAN of an unreachable bank).</remarks>
    [HttpPost("payee-verifications")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PayeeVerification), 200)]
    public IActionResult VerifyPayee([FromBody] PayeeVerificationRequest request)
    {
        var iban = Validators.NormalizeIban(request.Iban);
        var registered = state.FindByIban(iban)?.OwnerName ?? state.ExternalPayees.GetValueOrDefault(iban);

        VopResult result;
        string? suggestion = null;
        if (!iban.StartsWith("NL") || registered is null) result = VopResult.NOT_POSSIBLE;
        else
        {
            var a = Text.NormalizeName(request.Name);
            var b = Text.NormalizeName(registered);
            if (a == b) result = VopResult.MATCH;
            else if (Text.Levenshtein(a, b) <= 2 || InitialsMatch(a, b)) { result = VopResult.CLOSE_MATCH; suggestion = registered; }
            else result = VopResult.NO_MATCH;
        }

        lock (state.Sync)
        {
            var vop = new PayeeVerification
            {
                Id = state.NextId("vop"), Iban = iban, ProvidedName = request.Name, Result = result,
                SuggestedName = suggestion, CheckedAt = state.Now,
            };
            state.PayeeVerifications[vop.Id] = vop;
            return Ok(vop);
        }
    }

    private static bool InitialsMatch(string provided, string registered)
    {
        var p = provided.Split(' ');
        var r = registered.Split(' ');
        return p.Length == r.Length && p.Length > 1 && p[^1] == r[^1] &&
               p.Zip(r).Take(p.Length - 1).All(x => x.First == x.Second || (x.First.Length == 1 && x.Second.StartsWith(x.First)));
    }

    // ------------------------------------------------------------------ SEPA Credit Transfer

    /// <summary>Step 7 — Initiate a SEPA Credit Transfer or SEPA Instant Credit Transfer.</summary>
    /// <remarks>
    /// Send an <c>Idempotency-Key</c> header to make retries safe: the same key with the same body returns the
    /// original payment (200); the same key with a different body returns 409 IDEMPOTENCY_KEY_REUSED.
    /// Instant transfers settle immediately (ACSC); regular transfers are ACSP until the next business day.
    /// </remarks>
    /// <response code="201">Payment accepted.</response>
    /// <response code="422">INSUFFICIENT_FUNDS, CURRENCY_NOT_SUPPORTED, SAME_ACCOUNT or EXECUTION_DATE_IN_PAST.</response>
    [HttpPost("payments/sepa-credit-transfers")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Payment), 201)]
    [ProducesResponseType(typeof(Payment), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public IActionResult CreateSepaCreditTransfer([FromBody] SepaCreditTransferRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        var creditorIban = Validators.NormalizeIban(request.CreditorIban);
        var fingerprint = $"{request.DebtorAccountId}|{creditorIban}|{request.Amount}|{request.Currency}|{request.Instant}|{request.EndToEndId}";

        lock (state.Sync)
        {
            if (idempotencyKey is not null && state.IdempotencyKeys.TryGetValue(idempotencyKey, out var existingId))
            {
                var existing = state.Payments[existingId];
                return existing.IdempotencyFingerprint == fingerprint
                    ? Ok(existing)
                    : ConflictProblem("IDEMPOTENCY_KEY_REUSED", "This Idempotency-Key was already used with a different request.");
            }

            if (!state.Accounts.TryGetValue(request.DebtorAccountId, out var debtor)) return NotFoundProblem("Account", request.DebtorAccountId);
            if (debtor.Status == AccountStatus.CLOSED) return ConflictProblem("ACCOUNT_CLOSED", "The debtor account is closed.");
            if (debtor.Product is AccountProduct.SAVINGS or AccountProduct.TERM_DEPOSIT)
                return RuleProblem("ACCOUNT_TYPE_NOT_ALLOWED", "SEPA transfers can only be made from CURRENT or BUSINESS accounts.");
            if (request.Currency != "EUR")
                return RuleProblem("CURRENCY_NOT_SUPPORTED", "SEPA credit transfers are denominated in EUR only.");
            if (creditorIban == debtor.Iban)
                return RuleProblem("SAME_ACCOUNT", "Debtor and creditor account must differ.");
            var executionDate = request.RequestedExecutionDate ?? state.Today;
            if (executionDate < state.Today)
                return RuleProblem("EXECUTION_DATE_IN_PAST", "requestedExecutionDate cannot be in the past.");
            var amount = Validators.Money(request.Amount);
            if (amount > debtor.Balance)
                return RuleProblem("INSUFFICIENT_FUNDS", "The available balance is too low for this transfer.",
                    new { availableBalance = debtor.Balance, requestedAmount = amount });

            VopResult? vopResult = null;
            if (request.PayeeVerificationId is not null)
            {
                if (!state.PayeeVerifications.TryGetValue(request.PayeeVerificationId, out var vop))
                    return NotFoundProblem("PayeeVerification", request.PayeeVerificationId);
                if (vop.Iban != creditorIban)
                    return RuleProblem("PAYEE_VERIFICATION_MISMATCH", "The payee verification was made for a different IBAN.");
                vopResult = vop.Result;
            }

            var endToEndId = string.IsNullOrWhiteSpace(request.EndToEndId) ? "NOTPROVIDED" : request.EndToEndId;
            var payment = new Payment
            {
                Id = state.NextId("pmt"),
                Scheme = request.Instant ? PaymentScheme.SCT_INST : PaymentScheme.SCT,
                DebtorAccountId = debtor.Id, DebtorIban = debtor.Iban, DebtorName = debtor.OwnerName,
                CreditorIban = creditorIban, CreditorName = request.CreditorName,
                CreditorBic = request.CreditorBic ?? Validators.BicForIban(creditorIban),
                Amount = amount, Currency = "EUR", RemittanceInformation = request.RemittanceInformation, EndToEndId = endToEndId,
                RequestedExecutionDate = executionDate, AcceptedAt = state.Now, PayeeVerificationResult = vopResult,
                IdempotencyFingerprint = fingerprint,
            };

            if (request.Instant)
            {
                payment.Status = PaymentStatus.ACSC;
                payment.SettledAt = state.Now;
                payment.ExpectedSettlementDate = state.Today;
            }
            else
            {
                payment.Status = PaymentStatus.ACSP;
                payment.ExpectedSettlementDate = executionDate == state.Today ? Text.NextBusinessDay(executionDate) : executionDate;
            }

            state.Book(debtor, amount, CreditDebit.DBIT, executionDate, payment.CreditorName, creditorIban,
                payment.RemittanceInformation, "PMNT-ICDT-ESCT", endToEndId, payment.Id);
            if (state.FindByIban(creditorIban) is { } creditor)
                state.Book(creditor, amount, CreditDebit.CRDT, payment.ExpectedSettlementDate, debtor.OwnerName, debtor.Iban,
                    payment.RemittanceInformation, "PMNT-RCDT-ESCT", endToEndId, payment.Id);

            state.Payments[payment.Id] = payment;
            if (idempotencyKey is not null) state.IdempotencyKeys[idempotencyKey] = payment.Id;
            return Created($"/api/sandbox/banking/payments/{payment.Id}", payment);
        }
    }

    /// <summary>Get a payment and its ISO 20022 status.</summary>
    [HttpGet("payments/{paymentId}")]
    [ProducesResponseType(typeof(Payment), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetPayment(string paymentId) =>
        state.Payments.TryGetValue(paymentId, out var p) ? Ok(p) : NotFoundProblem("Payment", paymentId);

    // ------------------------------------------------------------------ iDEAL-style payment requests

    /// <summary>Create an iDEAL-style payment request (checkout link valid for 15 minutes).</summary>
    [HttpPost("payment-requests")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PaymentRequest), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public IActionResult CreatePaymentRequest([FromBody] CreatePaymentRequestRequest request)
    {
        lock (state.Sync)
        {
            if (!state.Accounts.TryGetValue(request.CreditorAccountId, out var creditor))
                return NotFoundProblem("Account", request.CreditorAccountId);
            if (request.Currency != "EUR") return RuleProblem("CURRENCY_NOT_SUPPORTED", "iDEAL payments are EUR only.");

            var pr = new PaymentRequest
            {
                Id = state.NextId("preq"), Status = PaymentRequestStatus.OPEN, Amount = Validators.Money(request.Amount),
                Description = request.Description, Reference = request.Reference, CreditorAccountId = creditor.Id,
                CreditorName = creditor.OwnerName, CreditorIban = creditor.Iban, ReturnUrl = request.ReturnUrl,
                CreatedAt = state.Now, ExpiresAt = state.Now.AddMinutes(15),
            };
            pr.CheckoutUrl = $"{Request.Scheme}://{Request.Host}/api/sandbox/banking/payment-requests/{pr.Id}";
            state.PaymentRequests[pr.Id] = pr;
            return Created($"/api/sandbox/banking/payment-requests/{pr.Id}", pr);
        }
    }

    /// <summary>Get a payment request (status becomes EXPIRED after 15 minutes).</summary>
    [HttpGet("payment-requests/{paymentRequestId}")]
    [ProducesResponseType(typeof(PaymentRequest), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetPaymentRequest(string paymentRequestId)
    {
        if (!state.PaymentRequests.TryGetValue(paymentRequestId, out var pr)) return NotFoundProblem("PaymentRequest", paymentRequestId);
        ApplyExpiry(pr);
        return Ok(pr);
    }

    /// <summary>Sandbox: simulate the payer approving / cancelling in their banking app.</summary>
    /// <response code="409">Payment request is no longer OPEN.</response>
    /// <response code="422">PAYMENT_REQUEST_EXPIRED or INSUFFICIENT_FUNDS.</response>
    [HttpPost("payment-requests/{paymentRequestId}/complete")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PaymentRequest), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public IActionResult CompletePaymentRequest(string paymentRequestId, [FromBody] CompletePaymentRequestRequest request)
    {
        lock (state.Sync)
        {
            if (!state.PaymentRequests.TryGetValue(paymentRequestId, out var pr)) return NotFoundProblem("PaymentRequest", paymentRequestId);
            ApplyExpiry(pr);
            if (pr.Status == PaymentRequestStatus.EXPIRED)
                return RuleProblem("PAYMENT_REQUEST_EXPIRED", "The payment request expired before it was completed.", new { pr.ExpiresAt });
            if (pr.Status != PaymentRequestStatus.OPEN)
                return ConflictProblem("PAYMENT_REQUEST_NOT_OPEN", $"Payment request is already {pr.Status}.");

            switch (request.Outcome)
            {
                case PaymentRequestOutcome.CANCELLED: pr.Status = PaymentRequestStatus.CANCELLED; return Ok(pr);
                case PaymentRequestOutcome.FAILURE: pr.Status = PaymentRequestStatus.FAILED; return Ok(pr);
            }

            var creditor = state.Accounts[pr.CreditorAccountId];
            string payerName = "J. Consument", payerIban = "NL69INGB0123456789";
            if (request.PayerAccountId is not null)
            {
                if (!state.Accounts.TryGetValue(request.PayerAccountId, out var payer)) return NotFoundProblem("Account", request.PayerAccountId);
                if (payer.Balance < pr.Amount)
                    return RuleProblem("INSUFFICIENT_FUNDS", "The payer's balance is too low.", new { availableBalance = payer.Balance });
                state.Book(payer, pr.Amount, CreditDebit.DBIT, state.Today, creditor.OwnerName, creditor.Iban, pr.Description,
                    "PMNT-ICDT-ESCT", pr.Reference ?? pr.Id, pr.Id);
                payerName = payer.OwnerName; payerIban = payer.Iban;
            }
            state.Book(creditor, pr.Amount, CreditDebit.CRDT, state.Today, payerName, payerIban, pr.Description,
                "PMNT-RCDT-ESCT", pr.Reference ?? pr.Id, pr.Id);
            pr.Status = PaymentRequestStatus.PAID;
            pr.PaidAt = state.Now;
            pr.PayerName = payerName;
            pr.PayerIban = payerIban;
            return Ok(pr);
        }
    }

    private void ApplyExpiry(PaymentRequest pr)
    {
        if (pr.Status == PaymentRequestStatus.OPEN && state.Now > pr.ExpiresAt) pr.Status = PaymentRequestStatus.EXPIRED;
    }

    // ------------------------------------------------------------------ Statements

    /// <summary>Step 8 — Account statement, camt.053-style JSON or CSV (<c>?format=csv</c> or <c>Accept: text/csv</c>).</summary>
    [HttpGet("accounts/{accountId}/statements")]
    [Produces("application/json", "text/csv")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetStatement(string accountId, [FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate,
        [FromQuery] string? format)
    {
        if (!state.Accounts.TryGetValue(accountId, out var account)) return NotFoundProblem("Account", accountId);
        var to = toDate ?? state.Today;
        var from = fromDate ?? to.AddDays(-30);
        if (from > to) return FieldProblem(nameof(fromDate), "fromDate must be on or before toDate.");

        List<BankTransaction> entries;
        decimal opening;
        lock (state.Sync)
        {
            entries = account.Transactions.Where(t => t.BookingDate >= from && t.BookingDate <= to).ToList();
            var before = account.Transactions.Where(t => t.BookingDate < from).ToList();
            opening = before.Count > 0 ? before[^1].BalanceAfter : 0m;
        }
        var closing = entries.Count > 0 ? entries[^1].BalanceAfter : opening;

        var wantsCsv = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase) ||
                       Request.Headers.Accept.ToString().Contains("text/csv");
        if (wantsCsv)
        {
            var csv = Text.Csv(
                ["bookingDate", "valueDate", "amount", "currency", "creditDebitIndicator", "counterpartyName", "counterpartyIban", "endToEndId", "remittanceInformation", "balanceAfter"],
                entries.Select(t => new object?[]
                {
                    t.BookingDate.ToString("yyyy-MM-dd"), t.ValueDate.ToString("yyyy-MM-dd"), t.Amount, t.Currency, t.CreditDebitIndicator,
                    t.CounterpartyName, t.CounterpartyIban, t.EndToEndId, t.RemittanceInformation, t.BalanceAfter,
                }));
            return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"statement-{account.Iban}-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
        }

        static object Bal(string code, decimal amount, DateOnly date) => new
        {
            type = code,
            amount = new { value = Math.Abs(amount), currency = "EUR" },
            creditDebitIndicator = amount >= 0 ? "CRDT" : "DBIT",
            date,
        };

        return Ok(new
        {
            messageType = "camt.053.001.08",
            statement = new
            {
                id = $"STMT-{account.Id}-{to:yyyyMMdd}",
                electronicSequenceNumber = to.DayNumber - from.DayNumber,
                creationDateTime = state.Now,
                fromToDate = new { fromDate = from, toDate = to },
                account = new { iban = account.Iban, currency = account.Currency, owner = account.OwnerName, servicer = new { bic = account.Bic } },
                balances = new[] { Bal("OPBD", opening, from), Bal("CLBD", closing, to) },
                transactionsSummary = new
                {
                    numberOfEntries = entries.Count,
                    totalCredits = entries.Where(e => e.CreditDebitIndicator == CreditDebit.CRDT).Sum(e => e.Amount),
                    totalDebits = entries.Where(e => e.CreditDebitIndicator == CreditDebit.DBIT).Sum(e => e.Amount),
                },
                entries = entries.Select(t => new
                {
                    entryReference = t.Id,
                    amount = new { value = t.Amount, currency = t.Currency },
                    creditDebitIndicator = t.CreditDebitIndicator,
                    status = "BOOK",
                    bookingDate = t.BookingDate,
                    valueDate = t.ValueDate,
                    bankTransactionCode = t.BankTransactionCode,
                    endToEndId = t.EndToEndId,
                    relatedParty = new { name = t.CounterpartyName, iban = t.CounterpartyIban },
                    remittanceInformation = new { unstructured = t.RemittanceInformation },
                }),
            },
        });
    }
}
