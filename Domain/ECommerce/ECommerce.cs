using System.ComponentModel.DataAnnotations;
using AuthSandbox.Infrastructure.Demo;

namespace AuthSandbox.Domain.ECommerce;

public enum VatClass { STANDARD, REDUCED }
public enum CartStatus { OPEN, CHECKED_OUT }
public enum ShippingMethod { POSTNL_STANDARD, DHL_PARCEL, PICKUP_POINT }
public enum ShopPaymentMethod { IDEAL, CARD, KLARNA_PAY_LATER }
public enum OrderStatus { PAID, SHIPPED, DELIVERED, PARTIALLY_RETURNED, RETURNED }
public enum ShipmentStatus { LABEL_CREATED, IN_TRANSIT, OUT_FOR_DELIVERY, DELIVERED }

public class Product
{
    public string Id { get; set; } = "";
    /// <summary>GTIN-13 / EAN-13 with valid check digit.</summary>
    public string Gtin { get; set; } = "";
    public string Name { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal NetPrice { get; set; }
    public VatClass VatClass { get; set; }
    /// <summary>Consumer price incl. home-country VAT (21% or 9%).</summary>
    public decimal PriceInclVatNl { get; set; }
    public int Stock { get; set; }
    public int WeightGrams { get; set; }
}

[SchemaExample("""{ "country": "NL", "customerEmail": "sanne.visser@example.nl" }""")]
public class CreateCartRequest
{
    /// <summary>Destination country; prices use its VAT rates (EU OSS).</summary>
    [CountryCode] public string Country { get; set; } = "NL";
    [EmailAddress] public string? CustomerEmail { get; set; }
}

[SchemaExample("""{ "gtin": "8712345000011", "quantity": 2 }""")]
public class AddCartItemRequest
{
    public string? ProductId { get; set; }
    [Ean13] public string? Gtin { get; set; }
    [Range(1, 99)] public int Quantity { get; set; } = 1;
}

[SchemaExample("""{ "quantity": 3 }""")]
public class UpdateCartItemRequest
{
    /// <summary>0 removes the item.</summary>
    [Range(0, 99)] public int Quantity { get; set; }
}

public class CartItem
{
    public string ItemId { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string Gtin { get; set; } = "";
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; }
    public decimal LineTotal { get; set; }
}

public class VatLine
{
    public decimal Rate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal GrossAmount { get; set; }
}

public class Totals
{
    public decimal ItemsTotal { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal VatTotal { get; set; }
    public List<VatLine> VatBreakdown { get; set; } = [];
    public string Currency { get; set; } = "EUR";
    public bool PricesIncludeVat { get; set; } = true;
}

public class Cart
{
    public string Id { get; set; } = "";
    public CartStatus Status { get; set; }
    public string Country { get; set; } = "NL";
    public string? CustomerEmail { get; set; }
    public List<CartItem> Items { get; set; } = [];
    public Totals Totals { get; set; } = new();
    public string? OrderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ShopCustomer
{
    [Required, StringLength(50)] public string FirstName { get; set; } = "";
    [Required, StringLength(80)] public string LastName { get; set; } = "";
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, E164] public string Phone { get => _phone; set => _phone = Validators.NormalizePhone(value ?? ""); }
    private string _phone = "";
}

public class ShopPayment
{
    public ShopPaymentMethod Method { get; set; } = ShopPaymentMethod.IDEAL;
    /// <summary>For CARD: tok_visa_success, tok_mastercard_success, tok_card_declined.</summary>
    public string? CardToken { get; set; }
}

[SchemaExample("""
{
  "customer": { "firstName": "Sanne", "lastName": "Visser", "email": "sanne.visser@example.nl", "phone": "+31612345678" },
  "shippingAddress": { "street": "Keizersgracht", "houseNumber": 123, "houseNumberAddition": "A", "postcode": "1015 CJ", "city": "Amsterdam", "country": "NL" },
  "shippingMethod": "POSTNL_STANDARD",
  "payment": { "method": "IDEAL" },
  "acceptTerms": true
}
""")]
public class CheckoutRequest
{
    [Required] public ShopCustomer Customer { get; set; } = new();
    [Required] public PostalAddress ShippingAddress { get; set; } = new();
    public ShippingMethod ShippingMethod { get; set; } = ShippingMethod.POSTNL_STANDARD;
    [Required] public ShopPayment Payment { get; set; } = new();
    public bool AcceptTerms { get; set; }
}

public class OrderLine
{
    public int LineId { get; set; }
    public string ProductId { get; set; } = "";
    public string Gtin { get; set; } = "";
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
    public int ReturnedQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; }
    public decimal LineTotal { get; set; }
}

public class Order
{
    public string Id { get; set; } = "";
    public string OrderNumber { get; set; } = "";
    public OrderStatus Status { get; set; }
    public string CartId { get; set; } = "";
    public ShopCustomer Customer { get; set; } = new();
    public PostalAddress ShippingAddress { get; set; } = new();
    public ShippingMethod ShippingMethod { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
    public Totals Totals { get; set; } = new();
    public object Payment { get; set; } = new();
    public string? ShipmentId { get; set; }
    public string? TrackingNumber { get; set; }
    public DateTimeOffset PlacedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    /// <summary>End of the 14-day right of withdrawal (Directive 2011/83/EU, Art. 9).</summary>
    public DateOnly? WithdrawalPeriodEndsOn { get; set; }
    public List<string> ReturnIds { get; set; } = [];
}

[SchemaExample("""{ "carrier": "POSTNL" }""")]
public class CreateShipmentRequest
{
    /// <summary>POSTNL or DHL. Defaults from the order's shipping method.</summary>
    [RegularExpression("POSTNL|DHL")] public string? Carrier { get; set; }
}

public class ShipmentEvent
{
    public ShipmentStatus Status { get; set; }
    public DateTimeOffset At { get; set; }
    public string Location { get; set; } = "";
    public string Description { get; set; } = "";
}

public class Shipment
{
    public string Id { get; set; } = "";
    public string OrderId { get; set; } = "";
    public string Carrier { get; set; } = "";
    public string TrackingNumber { get; set; } = "";
    public string TrackingUrl { get; set; } = "";
    public ShipmentStatus Status { get; set; }
    public DateOnly EstimatedDelivery { get; set; }
    public List<ShipmentEvent> Events { get; set; } = [];
}

[SchemaExample("""{ "occurredAt": "2026-11-08T10:15:00+01:00" }""")]
public class AdvanceShipmentRequest
{
    /// <summary>Optional event time (not in the future). Back-date delivery to demo an expired withdrawal period.</summary>
    public DateTimeOffset? OccurredAt { get; set; }
}

public class ReturnLineRequest
{
    [Range(1, 1000)] public int LineId { get; set; }
    [Range(1, 99)] public int Quantity { get; set; } = 1;
    [StringLength(200)] public string? Reason { get; set; }
}

[SchemaExample("""{ "lines": [ { "lineId": 1, "quantity": 1, "reason": "Changed my mind" } ] }""")]
public class CreateReturnRequest
{
    [Required, MinLength(1)] public List<ReturnLineRequest> Lines { get; set; } = [];
}

public class ReturnRequest
{
    public string Id { get; set; } = "";
    public string RmaNumber { get; set; } = "";
    public string OrderId { get; set; } = "";
    public string Status { get; set; } = "REQUESTED";
    public List<ReturnLineRequest> Lines { get; set; } = [];
    public decimal ItemsRefund { get; set; }
    /// <summary>Standard delivery cost is refunded when the whole order is withdrawn (Art. 13).</summary>
    public decimal ShippingRefund { get; set; }
    public decimal RefundAmount { get; set; }
    public string Currency { get; set; } = "EUR";
    public DateOnly RefundDueBy { get; set; }
    public object ReturnLabel { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
}

public class ECommerceState : SandboxState
{
    public Dictionary<string, Product> Products { get; } = [];
    public Dictionary<string, Cart> Carts { get; } = [];
    public Dictionary<string, Order> Orders { get; } = [];
    public Dictionary<string, Shipment> Shipments { get; } = [];
    public Dictionary<string, ReturnRequest> Returns { get; } = [];

    /// <summary>Standard / reduced B2C VAT rates per EU destination (OSS).</summary>
    public static readonly Dictionary<string, (decimal Standard, decimal Reduced)> VatRates = new()
    {
        ["NL"] = (21m, 9m), ["BE"] = (21m, 6m), ["DE"] = (19m, 7m), ["FR"] = (20m, 5.5m), ["ES"] = (21m, 10m),
        ["IT"] = (22m, 10m), ["AT"] = (20m, 10m), ["LU"] = (17m, 8m), ["IE"] = (23m, 13.5m), ["PT"] = (23m, 6m),
    };

    public static readonly Dictionary<ShippingMethod, (string Carrier, decimal Price)> Shipping = new()
    {
        [ShippingMethod.POSTNL_STANDARD] = ("POSTNL", 4.95m),
        [ShippingMethod.DHL_PARCEL] = ("DHL", 5.95m),
        [ShippingMethod.PICKUP_POINT] = ("POSTNL", 3.95m),
    };

    public const decimal FreeShippingThreshold = 50m;

    public ECommerceState(TimeProvider clock) : base(clock) => Reset();

    protected override void Clear() { Products.Clear(); Carts.Clear(); Orders.Clear(); Shipments.Clear(); Returns.Clear(); }

    protected override void Seed()
    {
        var catalog = new (string id, string name, string brand, string category, decimal net, VatClass vat, int stock, int grams)[]
        {
            ("prd_headphones", "Wireless noise-cancelling headphones", "Polder Audio", "electronics", 164.46m, VatClass.STANDARD, 25, 280),
            ("prd_ebike_lock", "Folding e-bike lock (ART-2)", "Fietsveilig", "bike-accessories", 57.81m, VatClass.STANDARD, 40, 1250),
            ("prd_raincoat", "Waterproof cycling raincoat", "Regenwolk", "clothing", 70.25m, VatClass.STANDARD, 15, 600),
            ("prd_cookbook", "Baking cookbook (hardcover)", "Uitgeverij Deeg", "books", 22.94m, VatClass.REDUCED, 60, 900),
            ("prd_stroopwafels", "Stroopwafels, 8-pack", "Goudse Bakkers", "food", 3.66m, VatClass.REDUCED, 500, 300),
            ("prd_coffee", "Organic coffee beans 1 kg", "Grachtenbrander", "food", 16.97m, VatClass.REDUCED, 3, 1000),
        };
        var i = 0;
        foreach (var c in catalog)
        {
            var first12 = $"871234500{++i:D3}";
            var (standard, reduced) = VatRates["NL"];
            Products[c.id] = new Product
            {
                Id = c.id, Gtin = first12 + Validators.Ean13CheckDigit(first12), Name = c.name, Brand = c.brand, Category = c.category,
                NetPrice = c.net, VatClass = c.vat, Stock = c.stock, WeightGrams = c.grams,
                PriceInclVatNl = Validators.Money(c.net * (1 + (c.vat == VatClass.STANDARD ? standard : reduced) / 100)),
            };
        }
    }

    public override object Fixtures => new
    {
        products = Products.Values.Select(p => new { p.Id, p.Gtin, p.Name, p.PriceInclVatNl, p.Stock }),
        lowStockProduct = "prd_coffee",
        cardTokens = new[] { "tok_visa_success", "tok_mastercard_success", "tok_card_declined" },
    };

    public decimal RateFor(string country, VatClass vatClass)
    {
        var r = VatRates.GetValueOrDefault(country, VatRates["NL"]);
        return vatClass == VatClass.STANDARD ? r.Standard : r.Reduced;
    }

    /// <summary>Recalculates VAT-inclusive cart totals for a destination country.</summary>
    public Totals Recalculate(List<CartItem> items, string country, ShippingMethod? method)
    {
        foreach (var item in items)
        {
            var p = Products[item.ProductId];
            item.VatRate = RateFor(country, p.VatClass);
            item.UnitPrice = Validators.Money(p.NetPrice * (1 + item.VatRate / 100));
            item.LineTotal = item.UnitPrice * item.Quantity;
        }
        var itemsTotal = items.Sum(i => i.LineTotal);
        var shipping = method is null || itemsTotal >= FreeShippingThreshold || items.Count == 0 ? 0m : Shipping[method.Value].Price;
        var standard = RateFor(country, VatClass.STANDARD);

        var groups = items.GroupBy(i => i.VatRate).Select(g => (rate: g.Key, gross: g.Sum(i => i.LineTotal))).ToList();
        if (shipping > 0)
        {
            var idx = groups.FindIndex(g => g.rate == standard);
            if (idx >= 0) groups[idx] = (standard, groups[idx].gross + shipping); else groups.Add((standard, shipping));
        }
        var breakdown = groups.OrderByDescending(g => g.rate).Select(g =>
        {
            var net = Validators.Money(g.gross / (1 + g.rate / 100));
            return new VatLine { Rate = g.rate, GrossAmount = g.gross, NetAmount = net, VatAmount = g.gross - net };
        }).ToList();

        return new Totals
        {
            ItemsTotal = itemsTotal, ShippingCost = shipping, GrandTotal = itemsTotal + shipping,
            VatTotal = breakdown.Sum(b => b.VatAmount), VatBreakdown = breakdown,
        };
    }
}
