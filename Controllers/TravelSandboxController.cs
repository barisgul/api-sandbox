using System;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace AuthSandbox.Controllers
{
    [ApiController]
    [Route("api/sandbox/travel")]
    [ApiExplorerSettings(GroupName = "v1-travel")]
    public class TravelSandboxController : ControllerBase
    {
        private static readonly ConcurrentDictionary<string, object> DataStore = new();

        public record CreatePassengerRequest(string FullName, string PassportNumber, string? FrequentFlyerNo = null, string Nationality = "NL");
        public record HoldSeatRequest(string FlightNumber, string SeatClass = "Economy", string PreferredSeat = "14A");
        public record BookFlightRequest(string PassengerId, string HoldId, string Origin = "AMS", string Destination = "JFK");
        public record BookHotelRequest(string Pnr, string HotelName = "Grand Hyatt Amsterdam", int Nights = 3);
        public record ConfirmItineraryRequest(string Pnr, string PaymentToken = "tok_visa_sample");

        /// <summary>
        /// Step 1: Register passenger profile
        /// </summary>
        [HttpPost("passengers")]
        public IActionResult CreatePassenger([FromBody] CreatePassengerRequest request)
        {
            var passengerId = $"PSG-{Random.Shared.Next(10000, 99999)}";
            var result = new
            {
                passengerId,
                fullName = request.FullName ?? "Jane Doe",
                passportNumber = request.PassportNumber ?? "N9821445",
                frequentFlyerNo = request.FrequentFlyerNo ?? "FF-88190",
                nationality = request.Nationality ?? "NL",
                createdAt = DateTime.UtcNow
            };
            DataStore[passengerId] = result;
            return Created($"/api/sandbox/travel/passengers/{passengerId}", result);
        }

        /// <summary>
        /// Step 2: Search flight & hold seat allocation
        /// </summary>
        [HttpPost("flights/hold-seats")]
        public IActionResult HoldSeat([FromBody] HoldSeatRequest request)
        {
            var holdId = $"HOLD-{Random.Shared.Next(1000, 9999)}";
            var result = new
            {
                holdId,
                flightNumber = request.FlightNumber ?? "KL602",
                seatClass = request.SeatClass ?? "Economy",
                assignedSeat = request.PreferredSeat ?? "14A",
                holdPrice = 450.00m,
                holdExpiresAt = DateTime.UtcNow.AddMinutes(15)
            };
            DataStore[holdId] = result;
            return Ok(result);
        }

        /// <summary>
        /// Step 3: Reserve flight booking with PNR
        /// </summary>
        [HttpPost("bookings/flight")]
        public IActionResult BookFlight([FromBody] BookFlightRequest request)
        {
            var bookingId = $"FL-{Random.Shared.Next(10000, 99999)}";
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var pnr = new string(Enumerable.Repeat(chars, 6).Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
            var result = new
            {
                bookingId,
                pnr,
                passengerId = request.PassengerId ?? "PSG-88219",
                holdId = request.HoldId ?? "HOLD-4412",
                origin = request.Origin ?? "AMS",
                destination = request.Destination ?? "JFK",
                totalPrice = 450.00m,
                bookingStatus = "RESERVED",
                reservedAt = DateTime.UtcNow
            };
            DataStore[pnr] = result;
            return Created($"/api/sandbox/travel/bookings/flight/{bookingId}", result);
        }

        /// <summary>
        /// Step 4: Book hotel room attached to PNR
        /// </summary>
        [HttpPost("bookings/hotel")]
        public IActionResult BookHotel([FromBody] BookHotelRequest request)
        {
            var hotelBookingId = $"HTL-{Random.Shared.Next(10000, 99999)}";
            var result = new
            {
                hotelBookingId,
                pnr = request.Pnr ?? "QX89KL",
                hotelName = request.HotelName ?? "Grand Hyatt Amsterdam",
                roomType = "Deluxe King Room",
                nights = request.Nights > 0 ? request.Nights : 3,
                pricePerNight = 180.00m,
                totalHotelPrice = (request.Nights > 0 ? request.Nights : 3) * 180.00m,
                status = "CONFIRMED"
            };
            DataStore[hotelBookingId] = result;
            return Created($"/api/sandbox/travel/bookings/hotel/{hotelBookingId}", result);
        }

        /// <summary>
        /// Step 5: Issue e-ticket and QR boarding pass for confirmed itinerary
        /// </summary>
        [HttpPost("itineraries/confirm")]
        public IActionResult ConfirmItinerary([FromBody] ConfirmItineraryRequest request)
        {
            var pnr = request.Pnr ?? "QX89KL";
            var ticketNumber = $"074-{Random.Shared.Next(1000000, 9999999)}";
            var result = new
            {
                itineraryId = $"ITIN-{Random.Shared.Next(10000, 99999)}",
                pnr,
                ticketNumber,
                totalAmountPaid = 990.00m,
                qrCodeUrl = $"https://api.sandbox.quixa.io/tickets/{pnr}.png",
                itineraryStatus = "CONFIRMED_AND_ISSUED",
                issuedAt = DateTime.UtcNow
            };
            return Ok(result);
        }
    }
}
