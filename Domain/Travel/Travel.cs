using System.ComponentModel.DataAnnotations;
using AuthSandbox.Infrastructure.Demo;

namespace AuthSandbox.Domain.Travel;

public enum OfferType { HOTEL, FLIGHT }
public enum FareClass { LIGHT, STANDARD, FLEX }
public enum HoldStatus { HELD, BOOKED, EXPIRED }
public enum BookingStatus { PENDING_PAYMENT, CONFIRMED, CANCELLED }
public enum TravelPaymentMethod { CARD, IDEAL, SEPA_CREDIT_TRANSFER }

public record Airport(string Code, string Name, string City, string Country, string TimeZone, double Lat, double Lon);
public record Room(string RoomType, string Description, decimal NightlyNetRate, int MaxGuests, bool Refundable, int FreeCancellationDays);
public record Hotel(string Id, string Name, string City, string Country, int Stars, string TimeZone, PostalAddress Address, Room[] Rooms);
public record Route(string FlightNumber, string Carrier, string CarrierName, string CarrierCountry, string Origin, string Destination,
    TimeOnly DepartureLocal, int DurationMinutes);

/// <summary>Accommodation VAT and tourist tax per country / city (sandbox values).</summary>
public record LodgingTax(decimal VatRate, decimal TouristTaxPercent, decimal TouristTaxPerPersonNight, string Note);

public class PriceBreakdown
{
    public decimal BaseAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TaxesAndFees { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "EUR";
    public List<object> Lines { get; set; } = [];
}

public class Offer
{
    public string OfferId { get; set; } = "";
    public OfferType Type { get; set; }
    public string Title { get; set; } = "";
    public int Guests { get; set; }
    public bool Refundable { get; set; }
    public DateTimeOffset? FreeCancellationUntil { get; set; }
    public PriceBreakdown Price { get; set; } = new();
    public DateTimeOffset ExpiresAt { get; set; }

    // Hotel
    public string? HotelId { get; set; }
    public string? RoomType { get; set; }
    public DateOnly? CheckIn { get; set; }
    public DateOnly? CheckOut { get; set; }
    public int? Nights { get; set; }

    // Flight
    public string? FlightNumber { get; set; }
    public string? Carrier { get; set; }
    public string? Origin { get; set; }
    public string? Destination { get; set; }
    public DateTimeOffset? DepartureTime { get; set; }
    public DateTimeOffset? ArrivalTime { get; set; }
    public int? DurationMinutes { get; set; }
    public FareClass? FareClass { get; set; }
    public bool? CheckedBagIncluded { get; set; }
}

[SchemaExample("""{ "offerId": "off_1001" }""")]
public class CreateHoldRequest
{
    [Required] public string OfferId { get; set; } = "";
}

public class Hold
{
    public string Id { get; set; } = "";
    public string OfferId { get; set; } = "";
    public HoldStatus Status { get; set; }
    public PriceBreakdown LockedPrice { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public class Traveller
{
    [StringLength(10)] public string? Title { get; set; }
    [Required, StringLength(50)] public string FirstName { get; set; } = "";
    [Required, StringLength(80)] public string LastName { get; set; } = "";
    [Required] public DateOnly? DateOfBirth { get; set; }
    [Required, CountryCode] public string Nationality { get; set; } = "NL";
}

public class Contact
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, E164] public string Phone { get; set; } = "";
}

[SchemaExample("""
{
  "holdId": "hold_1002",
  "contact": { "email": "sanne.visser@example.nl", "phone": "+31612345678" },
  "travellers": [
    { "title": "Ms", "firstName": "Sanne", "lastName": "Visser", "dateOfBirth": "1991-04-23", "nationality": "NL" },
    { "title": "Mr", "firstName": "Daan", "lastName": "de Boer", "dateOfBirth": "1986-11-02", "nationality": "NL" }
  ],
  "specialRequests": "Late check-in around 23:00"
}
""")]
public class CreateBookingRequest
{
    [Required] public string HoldId { get; set; } = "";
    [Required] public Contact Contact { get; set; } = new();
    [Required, MinLength(1), MaxLength(9)] public List<Traveller> Travellers { get; set; } = [];
    [StringLength(500)] public string? SpecialRequests { get; set; }
}

[SchemaExample("""{ "method": "CARD", "cardToken": "tok_visa_success" }""")]
public class BookingPaymentRequest
{
    public TravelPaymentMethod Method { get; set; } = TravelPaymentMethod.CARD;
    /// <summary>Sandbox tokens: tok_visa_success, tok_mastercard_success, tok_card_declined, tok_insufficient_funds.</summary>
    public string? CardToken { get; set; }
    /// <summary>Payer IBAN for SEPA_CREDIT_TRANSFER.</summary>
    [Iban] public string? Iban { get; set; }
}

public class BookingPayment
{
    public string Id { get; set; } = "";
    public TravelPaymentMethod Method { get; set; }
    public string Status { get; set; } = "SUCCEEDED";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public object? Instructions { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Refund
{
    public string Id { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public string Policy { get; set; } = "";
    public string Status { get; set; } = "PENDING";
    public DateOnly ExpectedBy { get; set; }
}

public class Booking
{
    public string Id { get; set; } = "";
    public string Reference { get; set; } = "";
    public OfferType Type { get; set; }
    public BookingStatus Status { get; set; }
    public Offer Offer { get; set; } = new();
    public Contact Contact { get; set; } = new();
    public List<Traveller> Travellers { get; set; } = [];
    public string? SpecialRequests { get; set; }
    public PriceBreakdown Price { get; set; } = new();
    public DateTimeOffset PaymentDueBy { get; set; }
    public BookingPayment? Payment { get; set; }
    public List<string> TicketNumbers { get; set; } = [];
    public string? HotelConfirmationNumber { get; set; }
    public Refund? Refund { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}

[SchemaExample("""{ "reason": "Change of plans" }""")]
public class CancelBookingRequest
{
    [StringLength(200)] public string? Reason { get; set; }
}

[SchemaExample("""
{
  "flightNumber": "QX101", "departureAirport": "AMS", "arrivalAirport": "BCN", "carrierCountry": "NL",
  "scheduledArrival": "2026-11-01T09:25:00+01:00", "actualArrival": "2026-11-01T13:10:00+01:00",
  "cancelled": false, "extraordinaryCircumstances": false
}
""")]
public class CompensationCheckRequest
{
    [Required, StringLength(8)] public string FlightNumber { get; set; } = "";
    /// <summary>IATA airport code.</summary>
    [Required, StringLength(3, MinimumLength = 3)] public string DepartureAirport { get; set; } = "";
    [Required, StringLength(3, MinimumLength = 3)] public string ArrivalAirport { get; set; } = "";
    /// <summary>Country of the operating carrier's licence (ISO 3166-1 alpha-2).</summary>
    [Required, CountryCode] public string CarrierCountry { get; set; } = "NL";
    [Required] public DateTimeOffset? ScheduledArrival { get; set; }
    public DateTimeOffset? ActualArrival { get; set; }
    public bool Cancelled { get; set; }
    /// <summary>Days of notice the passenger received before departure (cancellations only).</summary>
    [Range(0, 365)] public int? CancellationNoticeDays { get; set; }
    /// <summary>e.g. severe weather, ATC strike, security risk (Art. 5(3)).</summary>
    public bool ExtraordinaryCircumstances { get; set; }
}

public class TravelState : SandboxState
{
    public Dictionary<string, Offer> Offers { get; } = [];
    public Dictionary<string, Hold> Holds { get; } = [];
    public Dictionary<string, Booking> Bookings { get; } = [];

    public static readonly TimeSpan OfferTtl = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan HoldTtl = TimeSpan.FromMinutes(15);

    public static readonly Dictionary<string, Airport> Airports = new Airport[]
    {
        new("AMS", "Amsterdam Schiphol", "Amsterdam", "NL", "Europe/Amsterdam", 52.3086, 4.7639),
        new("RTM", "Rotterdam The Hague", "Rotterdam", "NL", "Europe/Amsterdam", 51.9569, 4.4372),
        new("EIN", "Eindhoven", "Eindhoven", "NL", "Europe/Amsterdam", 51.4500, 5.3747),
        new("BCN", "Barcelona El Prat", "Barcelona", "ES", "Europe/Madrid", 41.2971, 2.0785),
        new("MAD", "Madrid Barajas", "Madrid", "ES", "Europe/Madrid", 40.4936, -3.5668),
        new("CDG", "Paris Charles de Gaulle", "Paris", "FR", "Europe/Paris", 49.0097, 2.5479),
        new("FCO", "Rome Fiumicino", "Rome", "IT", "Europe/Rome", 41.8003, 12.2389),
        new("BER", "Berlin Brandenburg", "Berlin", "DE", "Europe/Berlin", 52.3667, 13.5033),
        new("LIS", "Lisbon Humberto Delgado", "Lisbon", "PT", "Europe/Lisbon", 38.7813, -9.1359),
        new("DUB", "Dublin", "Dublin", "IE", "Europe/Dublin", 53.4213, -6.2701),
        new("ATH", "Athens", "Athens", "GR", "Europe/Athens", 37.9364, 23.9445),
        new("CPH", "Copenhagen Kastrup", "Copenhagen", "DK", "Europe/Copenhagen", 55.6180, 12.6508),
        new("LHR", "London Heathrow", "London", "GB", "Europe/London", 51.4700, -0.4543),
        new("IST", "Istanbul", "Istanbul", "TR", "Europe/Istanbul", 41.2753, 28.7519),
        new("JFK", "New York JFK", "New York", "US", "America/New_York", 40.6413, -73.7781),
        new("DXB", "Dubai", "Dubai", "AE", "Asia/Dubai", 25.2532, 55.3657),
    }.ToDictionary(a => a.Code);

    public static readonly Dictionary<string, LodgingTax> LodgingTaxes = new()
    {
        ["Amsterdam"] = new(21m, 12.5m, 0m, "Accommodation VAT 21%; Amsterdam tourist tax 12.5% of room price"),
        ["Barcelona"] = new(10m, 0m, 6.25m, "ES VAT 10%; Catalan tourist tax incl. Barcelona surcharge, per person per night"),
        ["Paris"] = new(10m, 0m, 5.20m, "FR VAT 10%; taxe de séjour (4*) per person per night"),
        ["Berlin"] = new(7m, 7.5m, 0m, "DE VAT 7%; Berlin City Tax 7.5% of room price"),
        ["Rome"] = new(10m, 0m, 7.50m, "IT VAT 10%; contributo di soggiorno (4*) per person per night"),
    };

    public static readonly Hotel[] Hotels =
    [
        new("htl_ams_canal", "Canal View Hotel Amsterdam", "Amsterdam", "NL", 4, "Europe/Amsterdam",
            new PostalAddress { Street = "Prinsengracht", HouseNumber = 315, Postcode = "1016 GZ", City = "Amsterdam", Country = "NL" },
            [
                new("DOUBLE_STANDARD", "Standard double room, canal side", 149m, 2, true, 2),
                new("DOUBLE_NONREF", "Standard double room — non-refundable rate", 129m, 2, false, 0),
                new("FAMILY", "Family room with bunk beds", 229m, 4, true, 3),
            ]),
        new("htl_bcn_gotic", "Gòtic Courtyard Barcelona", "Barcelona", "ES", 4, "Europe/Madrid",
            new PostalAddress { Street = "Carrer de Ferran", HouseNumber = 12, Postcode = "08002", City = "Barcelona", Country = "ES" },
            [new("DOUBLE_STANDARD", "Double room, courtyard view", 135m, 2, true, 1), new("SUITE", "Junior suite with terrace", 260m, 3, true, 7)]),
        new("htl_par_marais", "Le Petit Marais Paris", "Paris", "FR", 4, "Europe/Paris",
            new PostalAddress { Street = "Rue des Rosiers", HouseNumber = 21, Postcode = "75004", City = "Paris", Country = "FR" },
            [new("DOUBLE_STANDARD", "Cosy double room", 175m, 2, true, 2)]),
        new("htl_ber_spree", "Spree Loft Berlin", "Berlin", "DE", 3, "Europe/Berlin",
            new PostalAddress { Street = "Stralauer Allee", HouseNumber = 3, Postcode = "10245", City = "Berlin", Country = "DE" },
            [new("DOUBLE_STANDARD", "Loft double room", 99m, 2, true, 1), new("DOUBLE_NONREF", "Loft double — non-refundable", 85m, 2, false, 0)]),
        new("htl_rom_trastevere", "Trastevere Garden Rome", "Rome", "IT", 4, "Europe/Rome",
            new PostalAddress { Street = "Via della Lungaretta", HouseNumber = 90, Postcode = "00153", City = "Rome", Country = "IT" },
            [new("DOUBLE_STANDARD", "Garden double room", 155m, 2, true, 3)]),
    ];

    public static readonly Route[] Routes = BuildRoutes();

    private static Route[] BuildRoutes()
    {
        var outbound = new Route[]
        {
            new("QX101", "QX", "Quixa Air", "NL", "AMS", "BCN", new(7, 15), 130),
            new("QX103", "QX", "Quixa Air", "NL", "AMS", "BCN", new(18, 40), 130),
            new("QX201", "QX", "Quixa Air", "NL", "AMS", "CDG", new(8, 0), 80),
            new("QX301", "QX", "Quixa Air", "NL", "AMS", "FCO", new(9, 30), 135),
            new("QX401", "QX", "Quixa Air", "NL", "AMS", "LIS", new(10, 5), 170),
            new("QX501", "QX", "Quixa Air", "NL", "AMS", "JFK", new(13, 0), 505),
            new("QX701", "QX", "Quixa Air", "NL", "AMS", "BER", new(7, 0), 75),
            new("QX801", "QX", "Quixa Air", "NL", "RTM", "ATH", new(6, 30), 205),
            new("QZ601", "QZ", "Bosphorus Demo Air", "TR", "IST", "AMS", new(11, 0), 230),
        };
        return outbound.Concat(outbound.Select(r => r with
        {
            FlightNumber = r.Carrier + (int.Parse(r.FlightNumber[2..]) + 1),
            Origin = r.Destination, Destination = r.Origin,
            DepartureLocal = r.DepartureLocal.AddHours(r.DurationMinutes > 300 ? 9 : 4),
        })).ToArray();
    }

    public TravelState(TimeProvider clock) : base(clock) => Reset();

    protected override void Clear() { Offers.Clear(); Holds.Clear(); Bookings.Clear(); }

    protected override void Seed() { }

    public override object Fixtures => new
    {
        cities = Hotels.Select(h => h.City).Distinct(),
        hotels = Hotels.Select(h => new { h.Id, h.Name, h.City, rooms = h.Rooms.Select(r => r.RoomType) }),
        routes = Routes.Select(r => new { r.FlightNumber, r.Origin, r.Destination, departureLocal = r.DepartureLocal.ToString("HH:mm") }),
        airports = Airports.Keys,
        cardTokens = new[] { "tok_visa_success", "tok_mastercard_success", "tok_card_declined", "tok_insufficient_funds" },
    };

    public static DateTimeOffset Local(DateOnly date, TimeOnly time, string timeZone)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        var local = date.ToDateTime(time);
        return new DateTimeOffset(local, tz.GetUtcOffset(local));
    }

    public static double DistanceKm(Airport a, Airport b)
    {
        const double r = 6371.0;
        double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(b.Lat - a.Lat);
        var dLon = Rad(b.Lon - a.Lon);
        var h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Rad(a.Lat)) * Math.Cos(Rad(b.Lat)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * r * Math.Asin(Math.Sqrt(h));
    }

    /// <summary>EU261 applies in EU member states plus Iceland, Norway and Switzerland.</summary>
    public static bool IsEu261Territory(string country) => Validators.EuCountries.Contains(country) || country is "IS" or "NO" or "CH";
}
