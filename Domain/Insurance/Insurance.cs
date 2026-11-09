using System.ComponentModel.DataAnnotations;
using AuthSandbox.Infrastructure.Demo;
using Microsoft.AspNetCore.Mvc;

namespace AuthSandbox.Domain.Insurance;

public enum InsuranceProduct { CAR, HOME_CONTENTS, TRAVEL }
public enum CarCoverage { WA, WA_PLUS, ALL_RISK }
public enum TravelArea { EUROPE, WORLD }
public enum PaymentFrequency { MONTHLY, YEARLY }
public enum QuoteStatus { QUOTED, ACCEPTED, EXPIRED }
public enum PolicyStatus { ACTIVE, CANCELLED }
public enum ClaimCategory { COLLISION, THEFT, WATER_DAMAGE, FIRE, STORM, BAGGAGE_LOSS, MEDICAL, OTHER }
public enum ClaimStatus { SUBMITTED, IN_REVIEW, APPROVED, REJECTED, PAID }
public enum ClaimDecision { AUTO, APPROVE, REJECT }

public class Policyholder
{
    [Required, StringLength(50)] public string FirstName { get; set; } = "";
    [Required, StringLength(80)] public string LastName { get; set; } = "";
    [Required] public DateOnly? DateOfBirth { get; set; }
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, E164] public string Phone { get => _phone; set => _phone = Validators.NormalizePhone(value ?? ""); }
    private string _phone = "";
    [Required] public PostalAddress Address { get; set; } = new();
}

public class CarDetails
{
    /// <summary>License plate, e.g. "K-123-AB".</summary>
    [Required, RegularExpression(@"^[A-Z0-9]{1,3}-[A-Z0-9]{1,3}-[A-Z0-9]{1,3}$")] public string LicensePlate { get; set; } = "";
    [Range(typeof(decimal), "1000", "250000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal CatalogValue { get; set; }
    [Range(1980, 2030)] public int BuildYear { get; set; }
    public CarCoverage Coverage { get; set; } = CarCoverage.WA_PLUS;
    /// <summary>Schadevrije jaren (no-claims years), drives the bonus-malus discount.</summary>
    [Range(0, 40)] public int ClaimFreeYears { get; set; }
}

public class HomeContentsDetails
{
    [Range(typeof(decimal), "5000", "250000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal InsuredAmount { get; set; }
    [Range(1, 12)] public int HouseholdSize { get; set; } = 1;
}

public class TravelDetails
{
    public TravelArea Area { get; set; } = TravelArea.EUROPE;
    [Range(1, 9)] public int Persons { get; set; } = 1;
}

[SchemaExample("""
{
  "product": "CAR", "startDate": "2026-12-01", "paymentFrequency": "MONTHLY", "deductible": 250,
  "policyholder": {
    "firstName": "Sanne", "lastName": "Visser", "dateOfBirth": "1991-04-23",
    "email": "sanne.visser@example.nl", "phone": "+31612345678",
    "address": { "street": "Keizersgracht", "houseNumber": 123, "postcode": "1015 CJ", "city": "Amsterdam", "country": "NL" }
  },
  "car": { "licensePlate": "K-123-AB", "catalogValue": 32500, "buildYear": 2022, "coverage": "ALL_RISK", "claimFreeYears": 6 }
}
""")]
public class QuoteRequest : IValidatableObject
{
    public InsuranceProduct Product { get; set; }
    [Required] public Policyholder Policyholder { get; set; } = new();
    [Required] public DateOnly? StartDate { get; set; }
    public PaymentFrequency PaymentFrequency { get; set; } = PaymentFrequency.MONTHLY;
    /// <summary>Eigen risico in EUR.</summary>
    [Range(0, 2500)] public int Deductible { get; set; } = 250;
    public CarDetails? Car { get; set; }
    public HomeContentsDetails? HomeContents { get; set; }
    public TravelDetails? Travel { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Product == InsuranceProduct.CAR && Car is null) yield return new("'car' is required for product CAR.", [nameof(Car)]);
        if (Product == InsuranceProduct.HOME_CONTENTS && HomeContents is null)
            yield return new("'homeContents' is required for product HOME_CONTENTS.", [nameof(HomeContents)]);
        if (Product == InsuranceProduct.TRAVEL && Travel is null) yield return new("'travel' is required for product TRAVEL.", [nameof(Travel)]);
    }
}

public class Premium
{
    public decimal NetPremium { get; set; }
    /// <summary>Insurance premium tax (21%).</summary>
    public decimal InsuranceTax { get; set; }
    public decimal InsuranceTaxRate { get; set; }
    public decimal GrossPremium { get; set; }
    public decimal AnnualGrossPremium { get; set; }
    public string Currency { get; set; } = "EUR";
    public PaymentFrequency Frequency { get; set; }
}

public class Quote
{
    public string Id { get; set; } = "";
    public InsuranceProduct Product { get; set; }
    public QuoteStatus Status { get; set; }
    public string PolicyholderName { get; set; } = "";
    public Policyholder Policyholder { get; set; } = new();
    public DateOnly StartDate { get; set; }
    public int Deductible { get; set; }
    public decimal CoverageLimit { get; set; }
    public List<string> CoveredCategories { get; set; } = [];
    public object? Details { get; set; }
    public Premium Premium { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ValidUntil { get; set; }
}

[SchemaExample("""{ "quoteId": "quo_1001", "iban": "NL33KNAB0417164301", "accountHolderName": "Sanne Visser", "acceptTerms": true }""")]
public class IssuePolicyRequest
{
    [Required] public string QuoteId { get; set; } = "";
    /// <summary>IBAN for the SEPA Core direct debit mandate.</summary>
    [Required, Iban] public string Iban { get; set; } = "";
    [Required, StringLength(70)] public string AccountHolderName { get; set; } = "";
    public bool AcceptTerms { get; set; }
}

public class SepaMandate
{
    public string MandateId { get; set; } = "";
    /// <summary>Synthetic SEPA creditor identifier (incassant-ID).</summary>
    public string CreditorId { get; set; } = "NL98ZZZ999999990000";
    public string Iban { get; set; } = "";
    public string AccountHolderName { get; set; } = "";
    public string Scheme { get; set; } = "CORE";
    public string SequenceType { get; set; } = "RCUR";
    public DateOnly SignatureDate { get; set; }
}

public class Policy
{
    public string Id { get; set; } = "";
    public string PolicyNumber { get; set; } = "";
    public string QuoteId { get; set; } = "";
    public InsuranceProduct Product { get; set; }
    public PolicyStatus Status { get; set; }
    public string PolicyholderName { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    /// <summary>14-day withdrawal period for distance contracts (EU Directive 2002/65/EC).</summary>
    public DateOnly CoolingOffUntil { get; set; }
    public int Deductible { get; set; }
    public decimal CoverageLimit { get; set; }
    public List<string> CoveredCategories { get; set; } = [];
    public Premium Premium { get; set; } = new();
    public SepaMandate Mandate { get; set; } = new();
    public DateOnly FirstCollectionDate { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateOnly? CancelledAt { get; set; }
    public decimal? RefundAmount { get; set; }
}

[SchemaExample("""{ "reason": "Vehicle sold", "effectiveDate": "2027-01-01" }""")]
public class CancelPolicyRequest
{
    [StringLength(200)] public string? Reason { get; set; }
    public DateOnly? EffectiveDate { get; set; }
}

/// <summary>multipart/form-data claim submission.</summary>
public class ClaimForm
{
    /// <example>2026-11-05</example>
    [FromForm(Name = "incidentDate")] [Required] public DateOnly? IncidentDate { get; set; }
    [FromForm(Name = "category")] [Required] public ClaimCategory? Category { get; set; }
    /// <example>Rear-ended at a traffic light on the A10; bumper and tailgate damaged.</example>
    [FromForm(Name = "description")] [Required, StringLength(2000, MinimumLength = 10)] public string Description { get; set; } = "";
    /// <example>1850.00</example>
    [FromForm(Name = "claimedAmount")] [Range(typeof(decimal), "1", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal ClaimedAmount { get; set; }
    /// <summary>Required for THEFT (proces-verbaal number).</summary>
    [FromForm(Name = "policeReportNumber")] [StringLength(30)] public string? PoliceReportNumber { get; set; }
    /// <summary>Photo or invoice (synthetic). PDF, PNG or JPEG, max 5 MB.</summary>
    [FromForm(Name = "attachment")] [Required] public IFormFile? Attachment { get; set; }
}

public class ClaimEvent
{
    public ClaimStatus Status { get; set; }
    public DateTimeOffset At { get; set; }
    public string? Note { get; set; }
}

public class Claim
{
    public string Id { get; set; } = "";
    public string ClaimNumber { get; set; } = "";
    public string PolicyId { get; set; } = "";
    public string PolicyNumber { get; set; } = "";
    public ClaimStatus Status { get; set; }
    public ClaimCategory Category { get; set; }
    public DateOnly IncidentDate { get; set; }
    public string Description { get; set; } = "";
    public decimal ClaimedAmount { get; set; }
    public int Deductible { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public decimal? PayoutAmount { get; set; }
    public string? PayoutIban { get; set; }
    public string? PoliceReportNumber { get; set; }
    public List<UploadedFileInfo> Attachments { get; set; } = [];
    public List<ClaimEvent> History { get; set; } = [];
    public DateTimeOffset SubmittedAt { get; set; }
}

[SchemaExample("""{ "decision": "AUTO", "note": "Damage photos consistent with description." }""")]
public class AssessClaimRequest
{
    /// <summary>AUTO: approve up to €5,000, otherwise IN_REVIEW. APPROVE / REJECT force a decision.</summary>
    public ClaimDecision Decision { get; set; } = ClaimDecision.AUTO;
    [Range(typeof(decimal), "0", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal? ApprovedAmount { get; set; }
    [StringLength(500)] public string? Note { get; set; }
}

public class InsuranceState : SandboxState
{
    public Dictionary<string, Quote> Quotes { get; } = [];
    public Dictionary<string, Policy> Policies { get; } = [];
    public Dictionary<string, Claim> Claims { get; } = [];

    public const decimal InsuranceTaxRate = 21m;

    public InsuranceState(TimeProvider clock) : base(clock) => Reset();

    protected override void Clear() { Quotes.Clear(); Policies.Clear(); Claims.Clear(); }

    protected override void Seed()
    {
        var start = Today.AddDays(-60);
        var premium = InsurancePricing.Gross(41.80m, PaymentFrequency.MONTHLY);
        Policies["pol_seed_car"] = new Policy
        {
            Id = "pol_seed_car", PolicyNumber = "POL-2026-000001", QuoteId = "quo_seed_car", Product = InsuranceProduct.CAR,
            Status = PolicyStatus.ACTIVE, PolicyholderName = "Sanne Visser", StartDate = start, EndDate = start.AddYears(1).AddDays(-1),
            CoolingOffUntil = start.AddDays(14), Deductible = 250, CoverageLimit = 32500m,
            CoveredCategories = InsurancePricing.Categories(InsuranceProduct.CAR, CarCoverage.ALL_RISK), Premium = premium,
            Mandate = new SepaMandate
            {
                MandateId = "MNDT-POL-2026-000001", Iban = "NL33KNAB0417164301", AccountHolderName = "Sanne Visser",
                SignatureDate = start.AddDays(-3),
            },
            FirstCollectionDate = start, IssuedAt = Now.AddDays(-63),
        };
        NextNumber("POL-2026");
    }

    public override object Fixtures => new
    {
        policies = Policies.Values.Select(p => new { p.Id, p.PolicyNumber, p.Product, p.Status }),
        triggers = new
        {
            autoApproveLimit = 5000m,
            expiredQuote = "quotes are valid for 30 days",
        },
    };
}

public static class InsurancePricing
{
    public static List<string> Categories(InsuranceProduct product, CarCoverage? car = null) => product switch
    {
        InsuranceProduct.CAR => car switch
        {
            CarCoverage.WA => ["COLLISION"],
            CarCoverage.WA_PLUS => ["COLLISION", "THEFT", "FIRE", "STORM"],
            _ => ["COLLISION", "THEFT", "FIRE", "STORM", "OTHER"],
        },
        InsuranceProduct.HOME_CONTENTS => ["THEFT", "WATER_DAMAGE", "FIRE", "STORM", "OTHER"],
        _ => ["BAGGAGE_LOSS", "MEDICAL", "THEFT", "OTHER"],
    };

    public static Premium Gross(decimal monthlyNet, PaymentFrequency frequency)
    {
        var net = frequency == PaymentFrequency.YEARLY ? Validators.Money(monthlyNet * 12 * 0.97m) : Validators.Money(monthlyNet);
        var tax = Validators.Money(net * InsuranceState.InsuranceTaxRate / 100);
        return new Premium
        {
            NetPremium = net, InsuranceTax = tax, InsuranceTaxRate = InsuranceState.InsuranceTaxRate, GrossPremium = net + tax,
            AnnualGrossPremium = frequency == PaymentFrequency.YEARLY ? net + tax : Validators.Money((net + tax) * 12),
            Frequency = frequency,
        };
    }

    /// <summary>Simplified, deterministic monthly net premium.</summary>
    public static decimal MonthlyNet(QuoteRequest r, int age)
    {
        decimal monthly;
        switch (r.Product)
        {
            case InsuranceProduct.CAR:
                var car = r.Car!;
                monthly = car.Coverage switch
                {
                    CarCoverage.WA => 28m,
                    CarCoverage.WA_PLUS => 42m,
                    _ => 30m + car.CatalogValue * 0.0012m,
                };
                monthly *= 1 - Math.Min(car.ClaimFreeYears * 0.05m, 0.75m);   // bonus-malus
                if (age < 25) monthly *= 1.35m;
                break;
            case InsuranceProduct.HOME_CONTENTS:
                var h = r.HomeContents!;
                var regionFactor = r.Policyholder.Address.Postcode.Length > 0 && r.Policyholder.Address.Postcode[0] is '1' or '3' ? 1.2m : 1.0m;
                monthly = h.InsuredAmount * 0.00011m * regionFactor + h.HouseholdSize * 0.5m;
                break;
            default:
                var t = r.Travel!;
                monthly = t.Persons * (t.Area == TravelArea.EUROPE ? 3.5m : 6.25m);
                break;
        }
        monthly *= 1 - r.Deductible / 2500m * 0.15m;
        return Validators.Money(Math.Max(monthly, 4.5m));
    }
}
