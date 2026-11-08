using AuthSandbox.Domain.Travel;
using AuthSandbox.Infrastructure.Demo;
using Microsoft.AspNetCore.Mvc;

namespace AuthSandbox.Controllers;

/// <summary>
/// Travel and booking demo (EU): search hotels/flights → hold (price lock) → book → pay → cancel/refund,
/// plus an EU261 flight compensation check. Times are ISO 8601 with the local UTC offset.
/// </summary>
[Route("api/sandbox/travel")]
[ApiExplorerSettings(GroupName = "v1-travel")]
[Tags("Travel")]
public class TravelSandboxController(TravelState state) : DemoControllerBase
{
    private const decimal NlAirPassengerTax = 29.40m;
    private const decimal AirportCharges = 18.00m;

    /// <summary>Reset travel data (offers, holds, bookings). Catalog data is static.</summary>
    [HttpPost("reset")]
    public IActionResult Reset()
    {
        state.Reset();
        return Ok(new { domain = "travel", status = "RESET", fixtures = state.Fixtures });
    }

    // ------------------------------------------------------------------ Search

    /// <summary>Step 1a — Search hotel offers (prices incl. accommodation VAT and tourist tax).</summary>
    [HttpGet("hotels/search")]
    [ProducesResponseType(typeof(IEnumerable<Offer>), 200)]
    public IActionResult SearchHotels([FromQuery] string city, [FromQuery] DateOnly checkIn, [FromQuery] DateOnly checkOut, [FromQuery] int guests = 2)
    {
        if (checkIn < state.Today) return FieldProblem(nameof(checkIn), "checkIn cannot be in the past.");
        if (checkOut <= checkIn) return FieldProblem(nameof(checkOut), "checkOut must be after checkIn.");
        var nights = checkOut.DayNumber - checkIn.DayNumber;
        if (nights > 30) return FieldProblem(nameof(checkOut), "Stays are limited to 30 nights.");
        if (guests is < 1 or > 9) return FieldProblem(nameof(guests), "guests must be between 1 and 9.");

        var offers = new List<Offer>();
        lock (state.Sync)
        {
            foreach (var hotel in TravelState.Hotels.Where(h => h.City.Equals(city, StringComparison.OrdinalIgnoreCase)))
            foreach (var room in hotel.Rooms.Where(r => r.MaxGuests >= guests))
            {
                var tax = TravelState.LodgingTaxes[hotel.City];
                var net = Validators.Money(room.NightlyNetRate * nights);
                var vat = Validators.Money(net * tax.VatRate / 100);
                var tourist = Validators.Money(net * tax.TouristTaxPercent / 100 + tax.TouristTaxPerPersonNight * guests * nights);
                var checkInTime = TravelState.Local(checkIn, new TimeOnly(15, 0), hotel.TimeZone);
                var offer = new Offer
                {
                    OfferId = state.NextId("off"), Type = OfferType.HOTEL, Title = $"{hotel.Name} — {room.Description}",
                    Guests = guests, Refundable = room.Refundable,
                    FreeCancellationUntil = room.Refundable ? checkInTime.AddDays(-room.FreeCancellationDays) : null,
                    HotelId = hotel.Id, RoomType = room.RoomType, CheckIn = checkIn, CheckOut = checkOut, Nights = nights,
                    ExpiresAt = state.Now + TravelState.OfferTtl,
                    Price = new PriceBreakdown
                    {
                        BaseAmount = net, VatRate = tax.VatRate, VatAmount = vat, TaxesAndFees = tourist, TotalAmount = net + vat + tourist,
                        Lines =
                        [
                            new { type = "ROOM", description = $"{nights} night(s) × €{room.NightlyNetRate:0.00}", amount = net },
                            new { type = "VAT", description = $"{hotel.Country} accommodation VAT {tax.VatRate}%", amount = vat },
                            new { type = "TOURIST_TAX", description = tax.Note, amount = tourist },
                        ],
                    },
                };
                state.Offers[offer.OfferId] = offer;
                offers.Add(offer);
            }
        }
        return Ok(offers);
    }

    /// <summary>Step 1b — Search flight offers (LIGHT / STANDARD / FLEX fares).</summary>
    [HttpGet("flights/search")]
    [ProducesResponseType(typeof(IEnumerable<Offer>), 200)]
    public IActionResult SearchFlights([FromQuery] string origin, [FromQuery] string destination, [FromQuery] DateOnly date,
        [FromQuery] int passengers = 1)
    {
        origin = origin.ToUpperInvariant();
        destination = destination.ToUpperInvariant();
        if (!TravelState.Airports.ContainsKey(origin)) return FieldProblem(nameof(origin), "Unknown IATA airport code.");
        if (!TravelState.Airports.ContainsKey(destination)) return FieldProblem(nameof(destination), "Unknown IATA airport code.");
        if (date < state.Today) return FieldProblem(nameof(date), "date cannot be in the past.");
        if (passengers is < 1 or > 9) return FieldProblem(nameof(passengers), "passengers must be between 1 and 9.");

        var from = TravelState.Airports[origin];
        var to = TravelState.Airports[destination];
        var distance = TravelState.DistanceKm(from, to);
        var offers = new List<Offer>();
        lock (state.Sync)
        {
            foreach (var route in TravelState.Routes.Where(r => r.Origin == origin && r.Destination == destination))
            foreach (var fare in Enum.GetValues<FareClass>())
            {
                var departure = TravelState.Local(date, route.DepartureLocal, from.TimeZone);
                var arrivalUtc = departure.UtcDateTime.AddMinutes(route.DurationMinutes);
                var tz = TimeZoneInfo.FindSystemTimeZoneById(to.TimeZone);
                var arrival = new DateTimeOffset(arrivalUtc).ToOffset(tz.GetUtcOffset(arrivalUtc));

                var farePerPax = Validators.Money(39m + (decimal)distance * 0.06m + fare switch { FareClass.STANDARD => 35m, FareClass.FLEX => 110m, _ => 0m });
                var taxesPerPax = (from.Country == "NL" ? NlAirPassengerTax : 0m) + AirportCharges;
                var baseAmount = farePerPax * passengers;
                var taxes = taxesPerPax * passengers;
                var offer = new Offer
                {
                    OfferId = state.NextId("off"), Type = OfferType.FLIGHT, Title = $"{route.CarrierName} {route.FlightNumber} {origin}→{destination} ({fare})",
                    Guests = passengers, Refundable = fare != FareClass.LIGHT,
                    FreeCancellationUntil = fare == FareClass.FLEX ? departure.AddHours(-24) : null,
                    FlightNumber = route.FlightNumber, Carrier = route.Carrier, Origin = origin, Destination = destination,
                    DepartureTime = departure, ArrivalTime = arrival, DurationMinutes = route.DurationMinutes,
                    FareClass = fare, CheckedBagIncluded = fare != FareClass.LIGHT, ExpiresAt = state.Now + TravelState.OfferTtl,
                    Price = new PriceBreakdown
                    {
                        BaseAmount = baseAmount, VatRate = 0, VatAmount = 0, TaxesAndFees = taxes, TotalAmount = baseAmount + taxes,
                        Lines =
                        [
                            new { type = "FARE", description = $"{passengers} × {fare} fare", amount = baseAmount },
                            new { type = "TAX", description = from.Country == "NL" ? "Air passenger tax" : "Departure taxes", amount = (taxesPerPax - AirportCharges) * passengers },
                            new { type = "FEE", description = "Airport & security charges", amount = AirportCharges * passengers },
                            new { type = "VAT", description = "International passenger transport is zero-rated", amount = 0m },
                        ],
                    },
                };
                state.Offers[offer.OfferId] = offer;
                offers.Add(offer);
            }
        }
        return Ok(offers);
    }

    /// <summary>Get an offer.</summary>
    [HttpGet("offers/{offerId}")]
    [ProducesResponseType(typeof(Offer), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetOffer(string offerId) =>
        state.Offers.TryGetValue(offerId, out var o) ? Ok(o) : NotFoundProblem("Offer", offerId);

    // ------------------------------------------------------------------ Hold

    /// <summary>Step 2 — Hold an offer: locks the price for 15 minutes.</summary>
    /// <response code="422">OFFER_EXPIRED (offers are valid for 30 minutes after search).</response>
    [HttpPost("holds")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Hold), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public IActionResult CreateHold([FromBody] CreateHoldRequest request)
    {
        lock (state.Sync)
        {
            if (!state.Offers.TryGetValue(request.OfferId, out var offer)) return NotFoundProblem("Offer", request.OfferId);
            if (state.Now > offer.ExpiresAt) return RuleProblem("OFFER_EXPIRED", "The offer has expired; search again.", new { offer.ExpiresAt });
            var hold = new Hold
            {
                Id = state.NextId("hold"), OfferId = offer.OfferId, Status = HoldStatus.HELD, LockedPrice = offer.Price,
                CreatedAt = state.Now, ExpiresAt = state.Now + TravelState.HoldTtl,
            };
            state.Holds[hold.Id] = hold;
            return Created($"/api/sandbox/travel/holds/{hold.Id}", hold);
        }
    }

    /// <summary>Get a hold (status becomes EXPIRED after 15 minutes).</summary>
    [HttpGet("holds/{holdId}")]
    [ProducesResponseType(typeof(Hold), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetHold(string holdId)
    {
        if (!state.Holds.TryGetValue(holdId, out var h)) return NotFoundProblem("Hold", holdId);
        if (h.Status == HoldStatus.HELD && state.Now > h.ExpiresAt) h.Status = HoldStatus.EXPIRED;
        return Ok(h);
    }

    // ------------------------------------------------------------------ Booking

    /// <summary>Step 3 — Book a held offer with guest / passenger details.</summary>
    /// <response code="409">HOLD_ALREADY_BOOKED.</response>
    /// <response code="422">HOLD_EXPIRED, TRAVELLER_COUNT_MISMATCH, ADULT_REQUIRED.</response>
    [HttpPost("bookings")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Booking), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public IActionResult CreateBooking([FromBody] CreateBookingRequest request)
    {
        lock (state.Sync)
        {
            if (!state.Holds.TryGetValue(request.HoldId, out var hold)) return NotFoundProblem("Hold", request.HoldId);
            if (hold.Status == HoldStatus.BOOKED) return ConflictProblem("HOLD_ALREADY_BOOKED", "This hold was already converted into a booking.");
            if (hold.Status == HoldStatus.EXPIRED || state.Now > hold.ExpiresAt)
            {
                hold.Status = HoldStatus.EXPIRED;
                return RuleProblem("HOLD_EXPIRED", "The price lock expired; create a new hold.", new { hold.ExpiresAt });
            }
            var offer = state.Offers[hold.OfferId];
            if (request.Travellers.Count != offer.Guests)
                return RuleProblem("TRAVELLER_COUNT_MISMATCH", $"The offer is for {offer.Guests} traveller(s); {request.Travellers.Count} provided.");
            var travelDate = offer.CheckIn ?? DateOnly.FromDateTime(offer.DepartureTime!.Value.DateTime);
            if (!request.Travellers.Any(t => t.DateOfBirth!.Value.AddYears(18) <= travelDate))
                return RuleProblem("ADULT_REQUIRED", "At least one traveller must be 18 or older.");

            var booking = new Booking
            {
                Id = state.NextId("bkg"), Reference = Text.Reference(state.NextNumber("ref") + 7), Type = offer.Type,
                Status = BookingStatus.PENDING_PAYMENT, Offer = offer, Contact = request.Contact, Travellers = request.Travellers,
                SpecialRequests = request.SpecialRequests, Price = hold.LockedPrice, PaymentDueBy = hold.ExpiresAt, CreatedAt = state.Now,
            };
            hold.Status = HoldStatus.BOOKED;
            state.Bookings[booking.Id] = booking;
            return Created($"/api/sandbox/travel/bookings/{booking.Id}", booking);
        }
    }

    /// <summary>Get a booking.</summary>
    [HttpGet("bookings/{bookingId}")]
    [ProducesResponseType(typeof(Booking), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetBooking(string bookingId) =>
        state.Bookings.TryGetValue(bookingId, out var b) ? Ok(b) : NotFoundProblem("Booking", bookingId);

    /// <summary>Step 4 — Pay a booking by card token, iDEAL or SEPA credit transfer.</summary>
    /// <response code="409">BOOKING_ALREADY_PAID or BOOKING_CANCELLED.</response>
    /// <response code="422">PAYMENT_DECLINED, INSUFFICIENT_FUNDS, PAYMENT_DEADLINE_PASSED.</response>
    [HttpPost("bookings/{bookingId}/payments")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Booking), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public IActionResult PayBooking(string bookingId, [FromBody] BookingPaymentRequest request)
    {
        lock (state.Sync)
        {
            if (!state.Bookings.TryGetValue(bookingId, out var b)) return NotFoundProblem("Booking", bookingId);
            if (b.Status == BookingStatus.CANCELLED) return ConflictProblem("BOOKING_CANCELLED", "The booking was cancelled.");
            if (b.Status == BookingStatus.CONFIRMED) return ConflictProblem("BOOKING_ALREADY_PAID", "The booking is already paid.");
            if (state.Now > b.PaymentDueBy)
                return RuleProblem("PAYMENT_DEADLINE_PASSED", "The price lock expired before payment.", new { b.PaymentDueBy });

            var payment = new BookingPayment { Id = state.NextId("bpay"), Method = request.Method, Amount = b.Price.TotalAmount, CreatedAt = state.Now };
            switch (request.Method)
            {
                case TravelPaymentMethod.CARD:
                    switch (request.CardToken)
                    {
                        case null or "": return FieldProblem(nameof(request.CardToken), "cardToken is required for CARD payments.");
                        case "tok_card_declined": return RuleProblem("PAYMENT_DECLINED", "The card issuer declined the payment (do_not_honor).");
                        case "tok_insufficient_funds": return RuleProblem("INSUFFICIENT_FUNDS", "The card has insufficient funds.");
                        case "tok_visa_success": payment.CardBrand = "VISA"; payment.CardLast4 = "4242"; break;
                        case "tok_mastercard_success": payment.CardBrand = "MASTERCARD"; payment.CardLast4 = "4444"; break;
                        default: return FieldProblem(nameof(request.CardToken), "Unknown sandbox card token.");
                    }
                    break;
                case TravelPaymentMethod.IDEAL:
                    payment.Instructions = new { redirectUrl = $"{Request.Scheme}://{Request.Host}/api/sandbox/travel/bookings/{b.Id}", issuer = "Sandbox Bank" };
                    break;
                case TravelPaymentMethod.SEPA_CREDIT_TRANSFER:
                    if (request.Iban is null) return FieldProblem(nameof(request.Iban), "iban is required for SEPA_CREDIT_TRANSFER.");
                    payment.Status = "SETTLED_INSTANT";
                    payment.Instructions = new
                    {
                        creditorName = "Quixa Travel B.V.", creditorIban = "NL20INGB0001234567", creditorBic = "INGBNL2A",
                        remittanceInformation = $"Booking {b.Reference}", debtorIban = Validators.NormalizeIban(request.Iban),
                    };
                    break;
            }

            b.Payment = payment;
            b.Status = BookingStatus.CONFIRMED;
            if (b.Type == OfferType.FLIGHT)
                b.TicketNumbers = b.Travellers.Select((_, i) => $"999{(state.NextNumber("ticket") + 4_200_000_000L + i):D10}").ToList();
            else
                b.HotelConfirmationNumber = $"{b.Offer.HotelId![4..7].ToUpperInvariant()}-{state.NextNumber("hotel-conf") + 50000}";
            return Ok(b);
        }
    }

    /// <summary>Step 5 — Cancel a booking and calculate the refund.</summary>
    /// <remarks>
    /// Hotels: full refund until freeCancellationUntil, otherwise none. Flights: FLEX full refund until 24h before
    /// departure, STANDARD refund minus €70 fee, LIGHT refunds government taxes and airport charges only.
    /// </remarks>
    /// <response code="409">BOOKING_ALREADY_CANCELLED.</response>
    [HttpPost("bookings/{bookingId}/cancellation")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Booking), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public IActionResult CancelBooking(string bookingId, [FromBody] CancelBookingRequest request)
    {
        lock (state.Sync)
        {
            if (!state.Bookings.TryGetValue(bookingId, out var b)) return NotFoundProblem("Booking", bookingId);
            if (b.Status == BookingStatus.CANCELLED) return ConflictProblem("BOOKING_ALREADY_CANCELLED", "The booking is already cancelled.");

            var paid = b.Status == BookingStatus.CONFIRMED ? b.Price.TotalAmount : 0m;
            var o = b.Offer;
            (decimal amount, string policy) refund = (paid, o.Type, o.FareClass) switch
            {
                (0m, _, _) => (0m, "Unpaid booking — nothing to refund."),
                (_, OfferType.HOTEL, _) when o.Refundable && state.Now <= o.FreeCancellationUntil => (paid, "Free cancellation."),
                (_, OfferType.HOTEL, _) => (0m, "Non-refundable rate or free-cancellation deadline passed."),
                (_, OfferType.FLIGHT, FareClass.FLEX) when state.Now <= o.FreeCancellationUntil => (paid, "FLEX fare — full refund."),
                (_, OfferType.FLIGHT, FareClass.STANDARD) or (_, OfferType.FLIGHT, FareClass.FLEX) =>
                    (Math.Max(0, paid - 70m * o.Guests), "Refund minus €70 change/cancellation fee per passenger."),
                _ => (b.Price.TaxesAndFees, "LIGHT fare — only government taxes and airport charges are refunded."),
            };

            b.Status = BookingStatus.CANCELLED;
            b.CancelledAt = state.Now;
            b.Refund = new Refund
            {
                Id = state.NextId("rfd"), Amount = Validators.Money(refund.amount),
                Policy = refund.policy + (request.Reason is null ? "" : $" Reason: {request.Reason}"),
                Status = refund.amount > 0 ? "PENDING" : "NOT_APPLICABLE",
                ExpectedBy = state.Today.AddDays(7),
            };
            return Ok(b);
        }
    }

    // ------------------------------------------------------------------ EU261

    /// <summary>EU261 compensation check (Regulation (EC) No 261/2004) for a delayed or cancelled flight.</summary>
    /// <remarks>
    /// €250 up to 1,500 km; €400 intra-EU above 1,500 km or 1,500–3,500 km; €600 above 3,500 km
    /// (halved for 3–4 h delays on those flights). Requires ≥ 3 h arrival delay (CJEU Sturgeon) or a cancellation
    /// with less than 14 days' notice. Applies to departures from the EU/EEA/CH, and to arrivals there on an EU carrier.
    /// </remarks>
    /// <response code="422">UNKNOWN_AIRPORT.</response>
    [HttpPost("flights/compensation-checks")]
    [Consumes("application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public IActionResult CheckCompensation([FromBody] CompensationCheckRequest request)
    {
        if (!TravelState.Airports.TryGetValue(request.DepartureAirport.ToUpperInvariant(), out var dep) ||
            !TravelState.Airports.TryGetValue(request.ArrivalAirport.ToUpperInvariant(), out var arr))
            return RuleProblem("UNKNOWN_AIRPORT", "Airport not in the sandbox catalog.", new { known = TravelState.Airports.Keys });
        if (!request.Cancelled && request.ActualArrival is null)
            return FieldProblem(nameof(request.ActualArrival), "actualArrival is required unless the flight was cancelled.");

        var reasons = new List<string>();
        var euCarrier = TravelState.IsEu261Territory(request.CarrierCountry);
        var inScope = TravelState.IsEu261Territory(dep.Country) || (TravelState.IsEu261Territory(arr.Country) && euCarrier);
        var distance = Math.Round(TravelState.DistanceKm(dep, arr));
        var intraEu = TravelState.IsEu261Territory(dep.Country) && TravelState.IsEu261Territory(arr.Country);
        var (band, amount) = distance <= 1500 ? ("UP_TO_1500_KM", 250m)
            : intraEu || distance <= 3500 ? (intraEu && distance > 3500 ? "INTRA_EU_OVER_1500_KM" : "1500_TO_3500_KM", 400m)
            : ("OVER_3500_KM", 600m);

        var delayMinutes = request.ActualArrival is null ? (int?)null
            : (int)Math.Round((request.ActualArrival.Value - request.ScheduledArrival!.Value).TotalMinutes);

        var eligible = true;
        if (!inScope)
        {
            eligible = false;
            reasons.Add("Out of scope: departure outside the EU/EEA/CH and the carrier is not an EU/EEA/CH carrier.");
        }
        if (request.ExtraordinaryCircumstances)
        {
            eligible = false;
            reasons.Add("Extraordinary circumstances (Art. 5(3)) release the carrier from compensation; care and assistance still apply.");
        }
        if (request.Cancelled)
        {
            if ((request.CancellationNoticeDays ?? 0) >= 14)
            {
                eligible = false;
                reasons.Add("Cancellation notified at least 14 days before departure (Art. 5(1)(c)(i)).");
            }
            else reasons.Add("Cancellation with less than 14 days' notice.");
        }
        else if (delayMinutes < 180)
        {
            eligible = false;
            reasons.Add($"Arrival delay of {delayMinutes} minutes is below the 3-hour threshold.");
        }
        else
        {
            reasons.Add($"Arrival delay of {delayMinutes} minutes (≥ 3 hours).");
            if (band == "OVER_3500_KM" && delayMinutes < 240)
            {
                amount /= 2;
                reasons.Add("Long-haul delay between 3 and 4 hours — compensation reduced by 50% (Art. 7(2)(c)).");
            }
        }

        return Ok(new
        {
            regulation = "Regulation (EC) No 261/2004",
            request.FlightNumber,
            route = new { from = dep.Code, to = arr.Code, distanceKm = distance, distanceBand = band, intraEu },
            delayMinutes,
            request.Cancelled,
            eligible,
            compensation = new { amount = eligible ? amount : 0m, currency = "EUR", perPassenger = true },
            reasons,
            checkedAt = state.Now,
        });
    }
}
