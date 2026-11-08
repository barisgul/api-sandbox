using AuthSandbox.Domain.ECommerce;
using AuthSandbox.Infrastructure.Demo;
using Microsoft.AspNetCore.Mvc;

namespace AuthSandbox.Controllers;

/// <summary>
/// E-commerce demo (EU): GTIN catalog → cart → checkout with destination VAT → order →
/// PostNL/DHL-style shipment tracking → return within the 14-day withdrawal period.
/// </summary>
[Route("api/sandbox/ecommerce")]
[ApiExplorerSettings(GroupName = "v1-ecommerce")]
[Tags("E-Commerce")]
public class ECommerceSandboxController(ECommerceState state) : DemoControllerBase
{
    /// <summary>Reset catalog, stock, carts and orders to the deterministic seed (idempotent).</summary>
    [HttpPost("reset")]
    public IActionResult Reset()
    {
        state.Reset();
        return Ok(new { domain = "ecommerce", status = "RESET", fixtures = state.Fixtures });
    }

    // ------------------------------------------------------------------ Catalog

    /// <summary>Step 1 — Browse the catalog.</summary>
    [HttpGet("products")]
    [ProducesResponseType(typeof(IEnumerable<Product>), 200)]
    public IActionResult ListProducts([FromQuery] string? category, [FromQuery] string? q) =>
        Ok(state.Products.Values.Where(p =>
            (category is null || p.Category.Equals(category, StringComparison.OrdinalIgnoreCase)) &&
            (q is null || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Brand.Contains(q, StringComparison.OrdinalIgnoreCase))));

    /// <summary>Get a product by id or GTIN.</summary>
    [HttpGet("products/{productIdOrGtin}")]
    [ProducesResponseType(typeof(Product), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetProduct(string productIdOrGtin) =>
        Find(productIdOrGtin) is { } p ? Ok(p) : NotFoundProblem("Product", productIdOrGtin);

    private Product? Find(string idOrGtin) =>
        state.Products.GetValueOrDefault(idOrGtin) ?? state.Products.Values.FirstOrDefault(p => p.Gtin == idOrGtin);

    // ------------------------------------------------------------------ Cart

    /// <summary>Step 2 — Create a cart for a destination country.</summary>
    /// <response code="422">COUNTRY_NOT_SUPPORTED (the shop ships within the EU only).</response>
    [HttpPost("carts")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Cart), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public IActionResult CreateCart([FromBody] CreateCartRequest request)
    {
        if (!ECommerceState.VatRates.ContainsKey(request.Country))
            return RuleProblem("COUNTRY_NOT_SUPPORTED", "Delivery is available to these EU countries only.", new { supported = ECommerceState.VatRates.Keys });
        lock (state.Sync)
        {
            var cart = new Cart
            {
                Id = state.NextId("cart"), Status = CartStatus.OPEN, Country = request.Country, CustomerEmail = request.CustomerEmail,
                CreatedAt = state.Now,
            };
            cart.Totals = state.Recalculate(cart.Items, cart.Country, null);
            state.Carts[cart.Id] = cart;
            return Created($"/api/sandbox/ecommerce/carts/{cart.Id}", cart);
        }
    }

    /// <summary>Get a cart.</summary>
    [HttpGet("carts/{cartId}")]
    [ProducesResponseType(typeof(Cart), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetCart(string cartId) =>
        state.Carts.TryGetValue(cartId, out var c) ? Ok(c) : NotFoundProblem("Cart", cartId);

    /// <summary>Step 3 — Add a product to the cart by productId or GTIN.</summary>
    /// <response code="409">CART_NOT_OPEN.</response>
    /// <response code="422">INSUFFICIENT_STOCK (try prd_coffee with quantity 5).</response>
    [HttpPost("carts/{cartId}/items")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Cart), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public IActionResult AddItem(string cartId, [FromBody] AddCartItemRequest request)
    {
        if (request.ProductId is null && request.Gtin is null) return FieldProblem(nameof(request.ProductId), "Provide productId or gtin.");
        lock (state.Sync)
        {
            if (!state.Carts.TryGetValue(cartId, out var cart)) return NotFoundProblem("Cart", cartId);
            if (cart.Status != CartStatus.OPEN) return ConflictProblem("CART_NOT_OPEN", "The cart has already been checked out.");
            var key = request.ProductId ?? request.Gtin!;
            if (Find(key) is not { } product) return NotFoundProblem("Product", key);

            var item = cart.Items.FirstOrDefault(i => i.ProductId == product.Id);
            var newQuantity = (item?.Quantity ?? 0) + request.Quantity;
            if (newQuantity > product.Stock)
                return RuleProblem("INSUFFICIENT_STOCK", $"Only {product.Stock} unit(s) of '{product.Name}' in stock.",
                    new { product.Id, available = product.Stock, requested = newQuantity });

            if (item is null)
                cart.Items.Add(new CartItem { ItemId = state.NextId("item"), ProductId = product.Id, Gtin = product.Gtin, Name = product.Name, Quantity = newQuantity });
            else item.Quantity = newQuantity;
            cart.Totals = state.Recalculate(cart.Items, cart.Country, null);
            return Created($"/api/sandbox/ecommerce/carts/{cart.Id}", cart);
        }
    }

    /// <summary>Change the quantity of a cart item (0 removes it).</summary>
    [HttpPatch("carts/{cartId}/items/{itemId}")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Cart), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public IActionResult UpdateItem(string cartId, string itemId, [FromBody] UpdateCartItemRequest request)
    {
        lock (state.Sync)
        {
            if (!state.Carts.TryGetValue(cartId, out var cart)) return NotFoundProblem("Cart", cartId);
            if (cart.Status != CartStatus.OPEN) return ConflictProblem("CART_NOT_OPEN", "The cart has already been checked out.");
            var item = cart.Items.FirstOrDefault(i => i.ItemId == itemId);
            if (item is null) return NotFoundProblem("CartItem", itemId);
            var product = state.Products[item.ProductId];
            if (request.Quantity > product.Stock)
                return RuleProblem("INSUFFICIENT_STOCK", $"Only {product.Stock} unit(s) in stock.", new { available = product.Stock });
            if (request.Quantity == 0) cart.Items.Remove(item); else item.Quantity = request.Quantity;
            cart.Totals = state.Recalculate(cart.Items, cart.Country, null);
            return Ok(cart);
        }
    }

    /// <summary>Remove an item from the cart.</summary>
    [HttpDelete("carts/{cartId}/items/{itemId}")]
    [ProducesResponseType(typeof(Cart), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public IActionResult RemoveItem(string cartId, string itemId)
    {
        lock (state.Sync)
        {
            if (!state.Carts.TryGetValue(cartId, out var cart)) return NotFoundProblem("Cart", cartId);
            if (cart.Status != CartStatus.OPEN) return ConflictProblem("CART_NOT_OPEN", "The cart has already been checked out.");
            if (cart.Items.RemoveAll(i => i.ItemId == itemId) == 0) return NotFoundProblem("CartItem", itemId);
            cart.Totals = state.Recalculate(cart.Items, cart.Country, null);
            return Ok(cart);
        }
    }

    // ------------------------------------------------------------------ Checkout & orders

    /// <summary>Step 4 — Checkout: recalculates VAT for the shipping country, takes payment and creates the order.</summary>
    /// <response code="409">CART_ALREADY_CHECKED_OUT.</response>
    /// <response code="422">CART_EMPTY, TERMS_NOT_ACCEPTED, COUNTRY_NOT_SUPPORTED, INSUFFICIENT_STOCK, PAYMENT_DECLINED.</response>
    [HttpPost("carts/{cartId}/checkout")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Order), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public IActionResult Checkout(string cartId, [FromBody] CheckoutRequest request)
    {
        lock (state.Sync)
        {
            if (!state.Carts.TryGetValue(cartId, out var cart)) return NotFoundProblem("Cart", cartId);
            if (cart.Status != CartStatus.OPEN)
                return ConflictProblem("CART_ALREADY_CHECKED_OUT", "This cart was already checked out.", new { cart.OrderId });
            if (cart.Items.Count == 0) return RuleProblem("CART_EMPTY", "Add at least one item before checking out.");
            if (!request.AcceptTerms) return RuleProblem("TERMS_NOT_ACCEPTED", "The terms and conditions must be accepted.");
            var country = request.ShippingAddress.Country;
            if (!ECommerceState.VatRates.ContainsKey(country))
                return RuleProblem("COUNTRY_NOT_SUPPORTED", "Delivery is available to these EU countries only.", new { supported = ECommerceState.VatRates.Keys });
            foreach (var i in cart.Items)
                if (state.Products[i.ProductId].Stock < i.Quantity)
                    return RuleProblem("INSUFFICIENT_STOCK", $"'{i.Name}' is no longer available in the requested quantity.");

            object payment;
            switch (request.Payment.Method)
            {
                case ShopPaymentMethod.CARD when request.Payment.CardToken == "tok_card_declined":
                    return RuleProblem("PAYMENT_DECLINED", "The card issuer declined the payment.");
                case ShopPaymentMethod.CARD when request.Payment.CardToken is not ("tok_visa_success" or "tok_mastercard_success"):
                    return FieldProblem("payment.cardToken", "Use a sandbox card token: tok_visa_success, tok_mastercard_success or tok_card_declined.");
                case ShopPaymentMethod.CARD:
                    payment = new { method = "CARD", status = "AUTHORISED_AND_CAPTURED", scaCompleted = true, last4 = request.Payment.CardToken == "tok_visa_success" ? "4242" : "4444" };
                    break;
                case ShopPaymentMethod.KLARNA_PAY_LATER:
                    payment = new { method = "KLARNA_PAY_LATER", status = "AUTHORISED", dueInDays = 30 };
                    break;
                default:
                    payment = new { method = "IDEAL", status = "PAID", issuer = "Sandbox Bank" };
                    break;
            }

            var totals = state.Recalculate(cart.Items, country, request.ShippingMethod);
            var year = state.Today.Year;
            var order = new Order
            {
                Id = state.NextId("ord"), OrderNumber = $"ORD-{year}-{state.NextNumber($"ORD-{year}"):D6}", Status = OrderStatus.PAID,
                CartId = cart.Id, Customer = request.Customer, ShippingAddress = request.ShippingAddress.Normalized(),
                ShippingMethod = request.ShippingMethod, Totals = totals, PlacedAt = state.Now,
                Lines = cart.Items.Select((i, idx) => new OrderLine
                {
                    LineId = idx + 1, ProductId = i.ProductId, Gtin = i.Gtin, Name = i.Name, Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice, VatRate = i.VatRate, LineTotal = i.LineTotal,
                }).ToList(),
            };
            order.Payment = new { amount = totals.GrandTotal, currency = "EUR", details = payment };
            foreach (var i in cart.Items) state.Products[i.ProductId].Stock -= i.Quantity;
            cart.Status = CartStatus.CHECKED_OUT;
            cart.OrderId = order.Id;
            cart.Country = country;
            cart.Totals = totals;
            state.Orders[order.Id] = order;
            return Created($"/api/sandbox/ecommerce/orders/{order.Id}", order);
        }
    }

    /// <summary>Get an order.</summary>
    [HttpGet("orders/{orderId}")]
    [ProducesResponseType(typeof(Order), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetOrder(string orderId) =>
        state.Orders.TryGetValue(orderId, out var o) ? Ok(o) : NotFoundProblem("Order", orderId);

    // ------------------------------------------------------------------ Shipping

    /// <summary>Step 5 — Create a shipment and tracking number (PostNL / DHL style).</summary>
    /// <response code="409">ALREADY_SHIPPED.</response>
    [HttpPost("orders/{orderId}/shipments")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Shipment), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public IActionResult CreateShipment(string orderId, [FromBody] CreateShipmentRequest request)
    {
        lock (state.Sync)
        {
            if (!state.Orders.TryGetValue(orderId, out var order)) return NotFoundProblem("Order", orderId);
            if (order.ShipmentId is not null) return ConflictProblem("ALREADY_SHIPPED", "A shipment already exists for this order.", new { order.ShipmentId });

            var carrier = request.Carrier ?? ECommerceState.Shipping[order.ShippingMethod].Carrier;
            var n = state.NextNumber("parcel");
            var tracking = carrier == "DHL" ? $"JVGL0{n + 6_200_000_000L:D15}" : $"3SQXDM{n + 100_000_000:D9}";
            var shipment = new Shipment
            {
                Id = state.NextId("shp"), OrderId = order.Id, Carrier = carrier, TrackingNumber = tracking,
                TrackingUrl = $"{Request.Scheme}://{Request.Host}/api/sandbox/ecommerce/shipments/{tracking}",
                Status = ShipmentStatus.LABEL_CREATED, EstimatedDelivery = Text.NextBusinessDay(state.Today),
                Events = [new ShipmentEvent { Status = ShipmentStatus.LABEL_CREATED, At = state.Now, Location = "Webshop warehouse, Tilburg", Description = "Shipping label created" }],
            };
            state.Shipments[shipment.Id] = shipment;
            order.ShipmentId = shipment.Id;
            order.TrackingNumber = tracking;
            order.Status = OrderStatus.SHIPPED;
            return Created($"/api/sandbox/ecommerce/shipments/{shipment.Id}", shipment);
        }
    }

    /// <summary>Track a shipment by shipment id or tracking number.</summary>
    [HttpGet("shipments/{shipmentIdOrTracking}")]
    [ProducesResponseType(typeof(Shipment), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetShipment(string shipmentIdOrTracking) =>
        FindShipment(shipmentIdOrTracking) is { } s ? Ok(s) : NotFoundProblem("Shipment", shipmentIdOrTracking);

    private Shipment? FindShipment(string key) =>
        state.Shipments.GetValueOrDefault(key) ?? state.Shipments.Values.FirstOrDefault(s => s.TrackingNumber == key);

    /// <summary>Sandbox: advance the shipment to its next status (IN_TRANSIT → OUT_FOR_DELIVERY → DELIVERED).</summary>
    /// <response code="409">ALREADY_DELIVERED.</response>
    [HttpPost("shipments/{shipmentIdOrTracking}/advance")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Shipment), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public IActionResult AdvanceShipment(string shipmentIdOrTracking, [FromBody] AdvanceShipmentRequest request)
    {
        lock (state.Sync)
        {
            if (FindShipment(shipmentIdOrTracking) is not { } s) return NotFoundProblem("Shipment", shipmentIdOrTracking);
            if (s.Status == ShipmentStatus.DELIVERED) return ConflictProblem("ALREADY_DELIVERED", "The parcel has already been delivered.");
            var at = request.OccurredAt ?? state.Now;
            if (at > state.Now.AddMinutes(1)) return FieldProblem(nameof(request.OccurredAt), "occurredAt cannot be in the future.");

            var order = state.Orders[s.OrderId];
            var next = s.Status + 1;
            var (location, description) = next switch
            {
                ShipmentStatus.IN_TRANSIT => ("Sorting centre, Nieuwegein", "Parcel sorted and in transit"),
                ShipmentStatus.OUT_FOR_DELIVERY => ($"Delivery depot, {order.ShippingAddress.City}", "Out for delivery"),
                _ => ($"{order.ShippingAddress.Postcode} {order.ShippingAddress.City}", "Delivered at the front door"),
            };
            s.Status = next;
            s.Events.Add(new ShipmentEvent { Status = next, At = at, Location = location, Description = description });
            if (next == ShipmentStatus.DELIVERED)
            {
                order.Status = OrderStatus.DELIVERED;
                order.DeliveredAt = at;
                order.WithdrawalPeriodEndsOn = DateOnly.FromDateTime(at.UtcDateTime).AddDays(14);
            }
            return Ok(s);
        }
    }

    // ------------------------------------------------------------------ Returns

    /// <summary>Step 6 — Return items within the 14-day EU right of withdrawal.</summary>
    /// <response code="422">ORDER_NOT_DELIVERED, WITHDRAWAL_PERIOD_EXPIRED, RETURN_QUANTITY_EXCEEDED.</response>
    [HttpPost("orders/{orderId}/returns")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ReturnRequest), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public IActionResult CreateReturn(string orderId, [FromBody] CreateReturnRequest request)
    {
        lock (state.Sync)
        {
            if (!state.Orders.TryGetValue(orderId, out var order)) return NotFoundProblem("Order", orderId);
            if (order.DeliveredAt is null) return RuleProblem("ORDER_NOT_DELIVERED", "Items can be returned after delivery.");
            if (state.Today > order.WithdrawalPeriodEndsOn)
                return RuleProblem("WITHDRAWAL_PERIOD_EXPIRED", "The 14-day withdrawal period has ended.", new { order.WithdrawalPeriodEndsOn });

            decimal itemsRefund = 0;
            foreach (var r in request.Lines)
            {
                var line = order.Lines.FirstOrDefault(l => l.LineId == r.LineId);
                if (line is null) return FieldProblem("lines", $"Order line {r.LineId} does not exist.");
                if (r.Quantity > line.Quantity - line.ReturnedQuantity)
                    return RuleProblem("RETURN_QUANTITY_EXCEEDED", $"Only {line.Quantity - line.ReturnedQuantity} unit(s) of line {r.LineId} can be returned.");
                itemsRefund += line.UnitPrice * r.Quantity;
            }
            foreach (var r in request.Lines)
            {
                var line = order.Lines.First(l => l.LineId == r.LineId);
                line.ReturnedQuantity += r.Quantity;
                state.Products[line.ProductId].Stock += r.Quantity;
            }

            var fullyReturned = order.Lines.All(l => l.ReturnedQuantity == l.Quantity);
            var shippingRefund = fullyReturned ? order.Totals.ShippingCost : 0m;
            var rma = new ReturnRequest
            {
                Id = state.NextId("ret"), RmaNumber = $"RMA-{state.Today.Year}-{state.NextNumber("rma"):D5}", OrderId = order.Id,
                Lines = request.Lines, ItemsRefund = itemsRefund, ShippingRefund = shippingRefund, RefundAmount = itemsRefund + shippingRefund,
                RefundDueBy = state.Today.AddDays(14), CreatedAt = state.Now,
                ReturnLabel = new { carrier = "POSTNL", trackingNumber = $"3SQXRT{state.NextNumber("parcel") + 200_000_000:D9}", dropOff = "Any PostNL point" },
            };
            state.Returns[rma.Id] = rma;
            order.ReturnIds.Add(rma.Id);
            order.Status = fullyReturned ? OrderStatus.RETURNED : OrderStatus.PARTIALLY_RETURNED;
            return Created($"/api/sandbox/ecommerce/returns/{rma.Id}", rma);
        }
    }

    /// <summary>Get a return.</summary>
    [HttpGet("returns/{returnId}")]
    [ProducesResponseType(typeof(ReturnRequest), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public IActionResult GetReturn(string returnId) =>
        state.Returns.TryGetValue(returnId, out var r) ? Ok(r) : NotFoundProblem("Return", returnId);
}
