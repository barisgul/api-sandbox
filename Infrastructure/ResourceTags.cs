using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AuthSandbox.Infrastructure;

/// <summary>
/// Groups demo endpoints into per-resource tags ("Customers", "Accounts", …) in flow order,
/// so Swagger UI and importers show one folder per resource.
/// </summary>
public static class ResourceTags
{
    public record Tag(string Segment, string Name, string Description);

    private static readonly (string Route, string Domain, Tag[] Tags)[] Domains =
    [
        ("banking", "Banking",
        [
            new("customers", "Customers", "Onboarding with personal details and postal address."),
            new("kyc", "KYC / CDD", "Customer due diligence with multipart identity-document upload."),
            new("accounts", "Accounts", "Current, savings, term-deposit and business accounts: open, add balance, withdraw, close."),
            new("transfers", "Transfers", "Money transfers between accounts at this bank."),
            new("payee-verifications", "Verification of Payee", "IBAN-name check before a transfer."),
            new("payments", "Payments", "SEPA Credit Transfer and SEPA Instant."),
            new("payment-requests", "Payment Requests", "iDEAL-style checkout payments."),
            new("statements", "Statements", "camt.053-style JSON and CSV statements."),
            new("psd2-consents", "PSD2 Consents", "Berlin Group NextGenPSD2-style consent and SCA."),
            new("psd2-accounts", "PSD2 Account Information", "Accounts, balances and transactions with Consent-ID."),
        ]),
        ("insurance", "Insurance",
        [
            new("quotes", "Quotes", "Premium quotes incl. insurance premium tax."),
            new("policies", "Policies", "Policy issue and cancellation with SEPA mandate."),
            new("claims", "Claims", "Claims with multipart attachments, assessment and payout."),
        ]),
        ("travel", "Travel",
        [
            new("hotels", "Hotels", "Hotel search with VAT and tourist tax."),
            new("flights", "Flights", "Flight search."),
            new("offers", "Offers", "Offer details."),
            new("holds", "Holds", "15-minute price locks."),
            new("bookings", "Bookings", "Bookings with guest details and cancellation."),
            new("booking-payments", "Payments", "Card token, iDEAL or SEPA payment of a booking."),
            new("compensation-checks", "EU261 Compensation", "Flight delay / cancellation compensation check."),
        ]),
        ("ecommerce", "E-Commerce",
        [
            new("products", "Catalog", "Products with GTIN-13."),
            new("carts", "Carts", "Cart creation and items."),
            new("checkout", "Checkout & Orders", "Checkout with destination VAT and orders."),
            new("shipments", "Shipments", "Tracking numbers and delivery events."),
            new("returns", "Returns", "Returns within the 14-day withdrawal period."),
        ]),
    ];

    private const string SandboxTag = "Sandbox";

    /// <summary>Returns (tag, sort key) for a demo endpoint, or null for non-demo endpoints.</summary>
    public static (string Tag, string SortKey)? Resolve(ApiDescription api)
    {
        var segments = (api.RelativePath ?? "").Split('?')[0].Split('/');
        if (segments.Length < 3 || segments[0] != "api" || segments[1] != "sandbox") return null;
        var di = Array.FindIndex(Domains, d => d.Route == segments[2]);
        if (di < 0) return null;
        var (_, domain, tags) = Domains[di];
        var rest = segments.Skip(3).Where(s => !s.StartsWith('{')).ToList();

        string key = rest.FirstOrDefault() ?? "";
        if (key == "reset") return (SandboxTag, $"{di:D2}-99-{api.RelativePath}");
        if (key == "psd2") key = rest.Contains("consents") ? "psd2-consents" : "psd2-accounts";
        else
        {
            // Use the most specific known resource in the path, e.g. policies/{id}/claims → claims.
            var specific = rest.LastOrDefault(s => tags.Any(t => t.Segment == s));
            if (specific is not null) key = specific;
            if (key == "orders") key = "checkout";
            if (rest.Contains("payments") && segments[2] == "travel") key = "booking-payments";
        }

        var ti = Array.FindIndex(tags, t => t.Segment == key);
        if (ti < 0) ti = 0;
        return (tags[ti].Name, $"{di:D2}-{ti:D2}-{api.RelativePath}-{api.HttpMethod}");
    }

    /// <summary>Publishes the tag list (with descriptions) in flow order.</summary>
    public class DocumentFilter : IDocumentFilter
    {
        public void Apply(OpenApiDocument doc, DocumentFilterContext context)
        {
            var used = doc.Paths.Values.SelectMany(p => p.Operations.Values).SelectMany(o => o.Tags).Select(t => t.Name).ToHashSet();
            var ordered = new List<OpenApiTag>();
            foreach (var (_, domain, tags) in Domains)
            {
                ordered.AddRange(tags.Select(t => new OpenApiTag { Name = t.Name, Description = t.Description })
                    .Where(t => used.Contains(t.Name) && ordered.All(o => o.Name != t.Name)));
                var sandbox = SandboxTag;
                if (used.Contains(sandbox) && ordered.All(o => o.Name != sandbox))
                    ordered.Add(new OpenApiTag { Name = sandbox, Description = "Reset to the deterministic seed." });
            }
            ordered.AddRange(used.Where(u => ordered.All(o => o.Name != u)).Select(u => new OpenApiTag { Name = u }));
            doc.Tags = ordered.DistinctBy(t => t.Name).ToList();
        }
    }
}
