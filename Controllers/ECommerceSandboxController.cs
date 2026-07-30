using System;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace AuthSandbox.Controllers
{
    [ApiController]
    [Route("api/sandbox/ecommerce")]
    [ApiExplorerSettings(GroupName = "v1-ecommerce")]
    public class ECommerceSandboxController : ControllerBase
    {
        private static readonly ConcurrentDictionary<string, object> DataStore = new();

        public record CreateCartRequest(string CustomerId = "CUST-1049", string Currency = "USD");
        public record AddCartItemRequest(string CartId, string Sku = "PROD-991", string ItemName = "Wireless Noise-Canceling Headphones", int Quantity = 1, decimal UnitPrice = 149.99m);
        public record CheckoutOrderRequest(string CartId, string ShippingAddress = "123 Market St, San Francisco, CA");
        public record ProcessPaymentRequest(string OrderId, decimal Amount = 149.99m, string PaymentMethod = "CreditCard");
        public record DispatchShipmentRequest(string OrderId, string WarehouseCode = "WH-WEST-01");

        /// <summary>
        /// Step 1: Initialize shopping cart
        /// </summary>
        [HttpPost("cart")]
        public IActionResult CreateCart([FromBody] CreateCartRequest request)
        {
            var cartId = $"CRT-{Random.Shared.Next(10000, 99999)}";
            var result = new
            {
                cartId,
                customerId = request.CustomerId ?? "CUST-1049",
                currency = request.Currency ?? "USD",
                itemCount = 0,
                subtotal = 0.00m,
                status = "OPEN",
                createdAt = DateTime.UtcNow
            };
            DataStore[cartId] = result;
            return Created($"/api/sandbox/ecommerce/cart/{cartId}", result);
        }

        /// <summary>
        /// Step 2: Add item to cart & reserve stock
        /// </summary>
        [HttpPost("cart/items")]
        public IActionResult AddCartItem([FromBody] AddCartItemRequest request)
        {
            var reservationId = $"RES-{Random.Shared.Next(1000, 9999)}";
            var result = new
            {
                cartId = request.CartId ?? "CRT-90124",
                reservationId,
                item = new
                {
                    sku = request.Sku ?? "PROD-991",
                    name = request.ItemName ?? "Wireless Noise-Canceling Headphones",
                    quantity = request.Quantity > 0 ? request.Quantity : 1,
                    unitPrice = request.UnitPrice > 0 ? request.UnitPrice : 149.99m
                },
                subtotal = (request.Quantity > 0 ? request.Quantity : 1) * (request.UnitPrice > 0 ? request.UnitPrice : 149.99m),
                stockReserved = true,
                reservationExpiresAt = DateTime.UtcNow.AddMinutes(30)
            };
            DataStore[reservationId] = result;
            return Ok(result);
        }

        /// <summary>
        /// Step 3: Checkout cart to create pending order
        /// </summary>
        [HttpPost("orders")]
        public IActionResult CreateOrder([FromBody] CheckoutOrderRequest request)
        {
            var orderId = $"ORD-{Random.Shared.Next(10000, 99999)}";
            var result = new
            {
                orderId,
                cartId = request.CartId ?? "CRT-90124",
                shippingAddress = request.ShippingAddress ?? "123 Market St, San Francisco, CA",
                totalAmount = 149.99m,
                tax = 12.00m,
                grandTotal = 161.99m,
                orderStatus = "PENDING_PAYMENT",
                createdAt = DateTime.UtcNow
            };
            DataStore[orderId] = result;
            return Created($"/api/sandbox/ecommerce/orders/{orderId}", result);
        }

        /// <summary>
        /// Step 4: Process order payment
        /// </summary>
        [HttpPost("orders/payments")]
        public IActionResult ProcessPayment([FromBody] ProcessPaymentRequest request)
        {
            var paymentId = $"PAY-{Random.Shared.Next(100000, 999999)}";
            var authCode = $"AUTH-{Random.Shared.Next(1000, 9999)}";
            var result = new
            {
                paymentId,
                orderId = request.OrderId ?? "ORD-44910",
                amountPaid = request.Amount > 0 ? request.Amount : 161.99m,
                paymentMethod = request.PaymentMethod ?? "CreditCard",
                authorizationCode = authCode,
                paymentStatus = "SUCCESS",
                paidAt = DateTime.UtcNow
            };
            DataStore[paymentId] = result;
            return Ok(result);
        }

        /// <summary>
        /// Step 5: Create warehouse parcel dispatch & courier tracking
        /// </summary>
        [HttpPost("shipments/dispatch")]
        public IActionResult DispatchShipment([FromBody] DispatchShipmentRequest request)
        {
            var shipmentId = $"SHP-{Random.Shared.Next(10000, 99999)}";
            var trackingNumber = $"TRK-{Random.Shared.Next(10000000, 99999999)}";
            var result = new
            {
                shipmentId,
                orderId = request.OrderId ?? "ORD-44910",
                warehouseCode = request.WarehouseCode ?? "WH-WEST-01",
                carrier = "FedEx Express",
                trackingNumber,
                estimatedDeliveryDate = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-dd"),
                shipmentStatus = "DISPATCHED_AND_IN_TRANSIT",
                dispatchedAt = DateTime.UtcNow
            };
            DataStore[shipmentId] = result;
            return Ok(result);
        }
    }
}
