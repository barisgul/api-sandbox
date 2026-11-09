using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using AuthSandbox.Infrastructure.Demo;
using Microsoft.AspNetCore.Mvc;

namespace AuthSandbox.Domain.Banking;

public enum KycStatus { NOT_STARTED, APPROVED, MANUAL_REVIEW, REJECTED }
public enum IdentityDocumentType { PASSPORT, ID_CARD, RESIDENCE_PERMIT }
public enum CddLevel { SIMPLIFIED, STANDARD, ENHANCED }
public enum RiskRating { LOW, MEDIUM, HIGH }
/// <summary>CURRENT = everyday payment account; SAVINGS and TERM_DEPOSIT are linked to a CURRENT account.</summary>
public enum AccountProduct { CURRENT, SAVINGS, TERM_DEPOSIT, BUSINESS }
public enum AccountStatus { ACTIVE, CLOSED }
public enum CreditDebit { CRDT, DBIT }
public enum VopResult { MATCH, CLOSE_MATCH, NO_MATCH, NOT_POSSIBLE }
/// <summary>ISO 20022 payment status codes (pain.002).</summary>
public enum PaymentStatus { RCVD, ACSP, ACSC, RJCT }
public enum PaymentScheme { SCT, SCT_INST }
public enum PaymentRequestStatus { OPEN, PAID, CANCELLED, EXPIRED, FAILED }
public enum PaymentRequestOutcome { SUCCESS, CANCELLED, FAILURE }

// ----------------------------------------------------------------- Customers

[SchemaExample("""
{
  "firstName": "Sanne", "lastName": "Visser", "dateOfBirth": "1991-04-23",
  "email": "sanne.visser@example.nl", "phone": "+31612345678",
  "nationality": "NL",
  "address": { "street": "Keizersgracht", "houseNumber": 123, "houseNumberAddition": "A",
               "postcode": "1015 CJ", "city": "Amsterdam", "country": "NL" }
}
""")]
public class CreateCustomerRequest
{
    [Required, StringLength(50)] public string FirstName { get; set; } = "";
    [Required, StringLength(80)] public string LastName { get; set; } = "";
    [Required] public DateOnly? DateOfBirth { get; set; }
    [Required, EmailAddress] public string Email { get; set; } = "";
    /// <summary>E.164, e.g. +31612345678.</summary>
    [Required, E164] public string Phone { get => _phone; set => _phone = Validators.NormalizePhone(value ?? ""); }
    private string _phone = "";
    /// <summary>ISO 3166-1 alpha-2.</summary>
    [Required, CountryCode] public string Nationality { get; set; } = "NL";
    [Required] public PostalAddress Address { get; set; } = new();
}

public class Customer
{
    public string Id { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FullName => $"{FirstName} {LastName}";
    public DateOnly DateOfBirth { get; set; }
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Nationality { get; set; } = "NL";
    public PostalAddress Address { get; set; } = new();
    public KycStatus KycStatus { get; set; }
    public string? KycCaseId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

// ----------------------------------------------------------------- KYC / CDD

/// <summary>multipart/form-data KYC submission (EU AMLD customer due diligence).</summary>
public class KycSubmissionForm
{
    /// <summary>PASSPORT, ID_CARD (EU/EEA nationals only) or RESIDENCE_PERMIT.</summary>
    [FromForm(Name = "documentType")] [Required] public IdentityDocumentType? DocumentType { get; set; }
    /// <example>NX4K29PL7</example>
    [FromForm(Name = "documentNumber")] [Required, StringLength(20, MinimumLength = 6)] public string DocumentNumber { get; set; } = "";
    /// <summary>ISO 3166-1 alpha-2 of the issuing state.</summary>
    /// <example>NL</example>
    [FromForm(Name = "issuingCountry")] [Required, CountryCode] public string IssuingCountry { get; set; } = "";
    /// <example>2031-06-30</example>
    [FromForm(Name = "expiryDate")] [Required] public DateOnly? ExpiryDate { get; set; }
    /// <summary>Scan of a SYNTHETIC document. PDF, PNG or JPEG, max 5 MB.</summary>
    [FromForm(Name = "document")] [Required] public IFormFile? Document { get; set; }
}

public class KycCase
{
    public string Id { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public KycStatus Status { get; set; }
    public CddLevel CddLevel { get; set; }
    public RiskRating RiskRating { get; set; }
    public KycScreening Screening { get; set; } = new();
    public KycDocument Document { get; set; } = new();
    public List<string> Reasons { get; set; } = [];
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}

public class KycScreening
{
    public bool PepMatch { get; set; }
    public bool SanctionsMatch { get; set; }
    public bool AdverseMedia { get; set; }
    public string ListsChecked { get; set; } = "EU Consolidated Financial Sanctions List, UN SC, OFAC SDN (simulated)";
}

public class KycDocument
{
    public IdentityDocumentType Type { get; set; }
    public string NumberMasked { get; set; } = "";
    public string IssuingCountry { get; set; } = "";
    public DateOnly ExpiryDate { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
}

// ----------------------------------------------------------------- Accounts

[SchemaExample("""{ "customerId": "cus_1006", "product": "CURRENT", "bankCode": "KNAB", "currency": "EUR", "name": "Everyday account" }""")]
public class OpenAccountRequest
{
    [Required] public string CustomerId { get; set; } = "";
    /// <summary>CURRENT, SAVINGS, TERM_DEPOSIT or BUSINESS.</summary>
    public AccountProduct Product { get; set; } = AccountProduct.CURRENT;
    /// <summary>Synthetic bank code used in the IBAN: KNAB, ABNA, INGB, RABO, SNSB, BUNQ.</summary>
    [StringLength(4, MinimumLength = 4)] public string BankCode { get; set; } = "KNAB";
    [CurrencyCode] public string Currency { get; set; } = "EUR";
    [StringLength(70)] public string? Name { get; set; }
    /// <summary>SAVINGS / TERM_DEPOSIT: the customer's CURRENT account to link. Defaults to their first CURRENT account.</summary>
    public string? LinkedAccountId { get; set; }
    /// <summary>TERM_DEPOSIT only: 3, 6, 12 or 24 months.</summary>
    public int? TermMonths { get; set; }
    /// <summary>TERM_DEPOSIT only: amount moved from the linked account at opening (min €1,000).</summary>
    [Range(typeof(decimal), "0", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal? OpeningAmount { get; set; }
}

public class Account
{
    public string Id { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public AccountProduct Product { get; set; }
    public string Name { get; set; } = "";
    public string OwnerName { get; set; } = "";
    /// <summary>Electronic format, e.g. NL91ABNA0417164300.</summary>
    public string Iban { get; set; } = "";
    /// <summary>Print format, e.g. NL91 ABNA 0417 1643 00.</summary>
    public string IbanDisplay => Validators.FormatIban(Iban);
    public string Bic { get; set; } = "";
    public string Currency { get; set; } = "EUR";
    public decimal Balance { get; set; }
    /// <summary>Annual interest rate in percent (savings and term deposits).</summary>
    public decimal? InterestRate { get; set; }
    public string? LinkedAccountId { get; set; }
    public int? TermMonths { get; set; }
    public DateOnly? MaturityDate { get; set; }
    public AccountStatus Status { get; set; } = AccountStatus.ACTIVE;
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    [JsonIgnore] public List<BankTransaction> Transactions { get; } = [];
}

[SchemaExample("""{ "amount": 2500.00, "description": "Salaris november" }""")]
public class DepositRequest
{
    [Range(typeof(decimal), "0.01", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal Amount { get; set; }
    [StringLength(140)] public string? Description { get; set; }
}

[SchemaExample("""{ "amount": 200.00, "description": "ATM withdrawal" }""")]
public class WithdrawalRequest
{
    [Range(typeof(decimal), "0.01", "100000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal Amount { get; set; }
    [StringLength(140)] public string? Description { get; set; }
}

[SchemaExample("""{ "fromAccountId": "acc_seed_sanne", "toAccountId": "acc_1008", "amount": 500.00, "description": "Monthly savings" }""")]
public class InternalTransferRequest
{
    [Required] public string FromAccountId { get; set; } = "";
    [Required] public string ToAccountId { get; set; } = "";
    [Range(typeof(decimal), "0.01", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal Amount { get; set; }
    [StringLength(140)] public string? Description { get; set; }
}

public class InternalTransfer
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "COMPLETED";
    public string FromAccountId { get; set; } = "";
    public string FromIban { get; set; } = "";
    public string ToAccountId { get; set; } = "";
    public string ToIban { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public string? Description { get; set; }
    public decimal FromBalanceAfter { get; set; }
    public decimal ToBalanceAfter { get; set; }
    public DateTimeOffset ExecutedAt { get; set; }
}

public class BankTransaction
{
    public string Id { get; set; } = "";
    public string AccountId { get; set; } = "";
    public DateOnly BookingDate { get; set; }
    public DateOnly ValueDate { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public CreditDebit CreditDebitIndicator { get; set; }
    public string? CounterpartyName { get; set; }
    public string? CounterpartyIban { get; set; }
    public string EndToEndId { get; set; } = "NOTPROVIDED";
    public string? RemittanceInformation { get; set; }
    /// <summary>ISO 20022 Bank Transaction Code (Domain-Family-SubFamily).</summary>
    public string BankTransactionCode { get; set; } = "PMNT-RCDT-ESCT";
    public decimal BalanceAfter { get; set; }
    public string? PaymentId { get; set; }
}

// ----------------------------------------------------------------- Verification of Payee

[SchemaExample("""{ "iban": "NL91ABNA0417164300", "name": "Daan de Boer" }""")]
public class PayeeVerificationRequest
{
    [Required, Iban] public string Iban { get; set; } = "";
    [Required, StringLength(70)] public string Name { get; set; } = "";
}

public class PayeeVerification
{
    public string Id { get; set; } = "";
    public string Iban { get; set; } = "";
    public string ProvidedName { get; set; } = "";
    public VopResult Result { get; set; }
    /// <summary>Only returned for CLOSE_MATCH, as required by the EU Instant Payments Regulation.</summary>
    public string? SuggestedName { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
}

// ----------------------------------------------------------------- SEPA

[SchemaExample("""
{
  "debtorAccountId": "acc_seed_sanne", "creditorIban": "NL91ABNA0417164300", "creditorName": "Daan de Boer",
  "amount": 125.50, "currency": "EUR", "remittanceInformation": "Huur december", "endToEndId": "E2E-2026-0001",
  "instant": true, "payeeVerificationId": "vop_1001"
}
""")]
public class SepaCreditTransferRequest
{
    [Required] public string DebtorAccountId { get; set; } = "";
    [Required, Iban] public string CreditorIban { get; set; } = "";
    [Required, StringLength(70)] public string CreditorName { get; set; } = "";
    [Bic] public string? CreditorBic { get; set; }
    [Range(typeof(decimal), "0.01", "999999999.99", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal Amount { get; set; }
    [CurrencyCode] public string Currency { get; set; } = "EUR";
    /// <summary>Unstructured remittance information, max 140 characters.</summary>
    [StringLength(140)] public string? RemittanceInformation { get; set; }
    /// <summary>Max 35 characters; defaults to NOTPROVIDED (SEPA convention).</summary>
    [StringLength(35)] public string? EndToEndId { get; set; }
    /// <summary>true = SEPA Instant Credit Transfer (settles in seconds).</summary>
    public bool Instant { get; set; }
    public DateOnly? RequestedExecutionDate { get; set; }
    /// <summary>Optional result of POST /payee-verifications made before this transfer.</summary>
    public string? PayeeVerificationId { get; set; }
}

public class Payment
{
    public string Id { get; set; } = "";
    public PaymentScheme Scheme { get; set; }
    public PaymentStatus Status { get; set; }
    public string DebtorAccountId { get; set; } = "";
    public string DebtorIban { get; set; } = "";
    public string DebtorName { get; set; } = "";
    public string CreditorIban { get; set; } = "";
    public string CreditorName { get; set; } = "";
    public string? CreditorBic { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public string? RemittanceInformation { get; set; }
    public string EndToEndId { get; set; } = "NOTPROVIDED";
    public DateOnly RequestedExecutionDate { get; set; }
    public DateOnly ExpectedSettlementDate { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
    public DateTimeOffset? SettledAt { get; set; }
    public VopResult? PayeeVerificationResult { get; set; }
    [JsonIgnore] public string? IdempotencyFingerprint { get; set; }
}

// ----------------------------------------------------------------- iDEAL-style payment requests

[SchemaExample("""{ "creditorAccountId": "acc_seed_daan", "amount": 49.95, "description": "Bestelling 2026-0042", "reference": "ORD-2026-0042", "returnUrl": "https://shop.example.nl/return" }""")]
public class CreatePaymentRequestRequest
{
    [Required] public string CreditorAccountId { get; set; } = "";
    [Range(typeof(decimal), "0.01", "50000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal Amount { get; set; }
    [CurrencyCode] public string Currency { get; set; } = "EUR";
    [Required, StringLength(35)] public string Description { get; set; } = "";
    [StringLength(35)] public string? Reference { get; set; }
    [Url] public string? ReturnUrl { get; set; }
}

[SchemaExample("""{ "outcome": "SUCCESS", "payerAccountId": "acc_seed_sanne" }""")]
public class CompletePaymentRequestRequest
{
    public PaymentRequestOutcome Outcome { get; set; } = PaymentRequestOutcome.SUCCESS;
    /// <summary>Sandbox account that "pays" in the bank app. Optional: an external payer is simulated otherwise.</summary>
    public string? PayerAccountId { get; set; }
}

public class PaymentRequest
{
    public string Id { get; set; } = "";
    public string Method { get; set; } = "IDEAL";
    public PaymentRequestStatus Status { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public string Description { get; set; } = "";
    public string? Reference { get; set; }
    public string CreditorAccountId { get; set; } = "";
    public string CreditorName { get; set; } = "";
    public string CreditorIban { get; set; } = "";
    public string CheckoutUrl { get; set; } = "";
    public string? ReturnUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? PayerName { get; set; }
    public string? PayerIban { get; set; }
}

// ----------------------------------------------------------------- PSD2 (Berlin Group NextGenPSD2)

public class AccountReference
{
    [Required, Iban] public string Iban { get; set; } = "";
    [CurrencyCode] public string? Currency { get; set; }
}

public class ConsentAccess
{
    public List<AccountReference> Accounts { get; set; } = [];
    public List<AccountReference> Balances { get; set; } = [];
    public List<AccountReference> Transactions { get; set; } = [];
}

[SchemaExample("""
{
  "access": {
    "accounts": [ { "iban": "NL33KNAB0417164301" } ],
    "balances": [ { "iban": "NL33KNAB0417164301" } ],
    "transactions": [ { "iban": "NL33KNAB0417164301" } ]
  },
  "recurringIndicator": true, "validUntil": "2027-02-01", "frequencyPerDay": 4, "combinedServiceIndicator": false
}
""")]
public class ConsentRequest
{
    [Required] public ConsentAccess Access { get; set; } = new();
    public bool RecurringIndicator { get; set; } = true;
    [Required] public DateOnly? ValidUntil { get; set; }
    [Range(1, 4)] public int FrequencyPerDay { get; set; } = 4;
    public bool CombinedServiceIndicator { get; set; }
}

public class Consent
{
    public string ConsentId { get; set; } = "";
    /// <summary>received, valid, rejected, expired, terminatedByTpp.</summary>
    public string ConsentStatus { get; set; } = "received";
    public ConsentAccess Access { get; set; } = new();
    public bool RecurringIndicator { get; set; }
    public DateOnly ValidUntil { get; set; }
    public int FrequencyPerDay { get; set; }
    public DateOnly LastActionDate { get; set; }
    [JsonPropertyName("_links")] public Dictionary<string, Link> Links { get; set; } = [];
}

public record Link(string Href);

[SchemaExample("""{ "psuId": "sanne.visser", "outcome": "APPROVE" }""")]
public class ConsentAuthorisationRequest
{
    public string? PsuId { get; set; }
    /// <summary>Sandbox SCA outcome: APPROVE or REJECT.</summary>
    [RegularExpression("APPROVE|REJECT")] public string Outcome { get; set; } = "APPROVE";
}
