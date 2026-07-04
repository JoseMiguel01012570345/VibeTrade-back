using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Affiliates.Interfaces;
using VibeTrade.Backend.Features.Debts.Interfaces;
using VibeTrade.Backend.Features.Logistics.Interfaces;
using VibeTrade.Backend.Features.Market.Entities;
using VibeTrade.Backend.Features.Notifications.NotificationInterfaces;
using VibeTrade.Backend.Features.Orders.Dtos;
using VibeTrade.Backend.Features.Orders.Entities;
using VibeTrade.Backend.Features.Orders.Interfaces;
using VibeTrade.Backend.Features.Trust;
using VibeTrade.Backend.Features.Trust.Interfaces;

namespace VibeTrade.Backend.Features.Orders;

/// <summary>
/// Implementa el ciclo de vida del Pedido reusando la lógica del checkout de referencia
/// (validación de carrito, cálculo de tarifa por distancia, descuento de stock, número público)
/// adaptada al dominio de VibeTrade (tiendas con moneda propia, IDs string).
/// </summary>
public sealed class OrderService(
    AppDbContext db,
    ITrustScoreLedgerService trustLedger,
    INotificationService notifications,
    IOrderRouteLifecycleService orderRouteLifecycle,
    IAffiliateService affiliates,
    IDebtsService debts) : IOrderService
{
    public async Task<(CheckoutPreviewResponse? Value, OrderError? Error)> PreviewAsync(
        string buyerUserId,
        CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var (ctx, error) = await BuildCheckoutContextAsync(request, cancellationToken).ConfigureAwait(false);
        if (error is not null)
            return (null, error);

        var previewLines = ctx!.Lines
            .Select(l => new CheckoutPreviewLine(
                l.Product.Id,
                l.Product.Name,
                l.Quantity,
                l.UnitPrice,
                OrderPricing.Round(l.UnitPrice * l.Quantity),
                ctx.CurrencyCode))
            .ToList();

        return (
            new CheckoutPreviewResponse(
                ctx.Store.Id,
                ctx.CurrencyCode,
                ctx.Subtotal,
                ctx.DeliveryFee,
                ctx.Total,
                ctx.Store.PricePerKm,
                ctx.RouteDistanceKm,
                previewLines),
            null);
    }

    public async Task<(CreateOrderResponse? Value, OrderError? Error)> CreateAsync(
        string buyerUserId,
        CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var buyer = (buyerUserId ?? "").Trim();
        if (buyer.Length < 2)
            return (null, new OrderError("unauthorized", "Sesión no válida."));

        var (ctx, error) = await BuildCheckoutContextAsync(request, cancellationToken).ConfigureAwait(false);
        if (error is not null)
            return (null, error);

        var now = DateTimeOffset.UtcNow;
        var publicNumber = await NextPublicNumberAsync(cancellationToken).ConfigureAwait(false);

        var (_, affiliateCommission, _) = await affiliates.ResolveCommissionAsync(
            request.AffiliateCode,
            ctx!.Subtotal,
            ctx.DeliveryFee,
            ctx.CurrencyCode,
            cancellationToken).ConfigureAwait(false);

        var order = new OrderRow
        {
            Id = Guid.NewGuid().ToString("N"),
            PublicNumber = publicNumber,
            BuyerUserId = buyer,
            StoreId = ctx!.Store.Id,
            SellerUserId = ctx.Store.OwnerUserId,
            Status = OrderStatuses.Procesado,
            CustomerFirstName = (request.CustomerFirstName ?? "").Trim(),
            CustomerLastName = (request.CustomerLastName ?? "").Trim(),
            PhonePrimary = (request.PhonePrimary ?? "").Trim(),
            PhoneSecondary = (request.PhoneSecondary ?? "").Trim(),
            DeliveryMode = ctx.DeliveryMode,
            DeliveryAddress = (request.DeliveryAddress ?? "").Trim(),
            DeliveryLatitude = request.DeliveryLatitude,
            DeliveryLongitude = request.DeliveryLongitude,
            CurrencyCode = ctx.CurrencyCode,
            Subtotal = ctx.Subtotal,
            DeliveryFee = ctx.DeliveryFee,
            Total = ctx.Total,
            PricePerKmSnapshot = ctx.Store.PricePerKm,
            RouteDistanceKm = ctx.RouteDistanceKm,
            PaymentStatus = OrderPaymentStatuses.Held,
            PaymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? null : request.PaymentMethod!.Trim(),
            PaymentReference = $"VTPAY-{Guid.NewGuid():N}",
            PaymentHeldAtUtc = now,
            ClientEvidenceDecision = OrderClientEvidenceDecisions.None,
            AffiliateCodeSnapshot = string.IsNullOrWhiteSpace(request.AffiliateCode) ? null : request.AffiliateCode!.Trim(),
            AffiliateCommissionAmount = affiliateCommission > 0 ? affiliateCommission : null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        foreach (var l in ctx.Lines)
        {
            order.Lines.Add(new OrderLineRow
            {
                Id = Guid.NewGuid().ToString("N"),
                OrderId = order.Id,
                ProductId = l.Product.Id,
                StoreId = ctx.Store.Id,
                ProductName = l.Product.Name,
                TechnicalSpecs = l.Product.TechnicalSpecs ?? "",
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                CurrencyCode = ctx.CurrencyCode,
            });

            if (l.Product.StockQuantity is int stock)
                l.Product.StockQuantity = Math.Max(0, stock - l.Quantity);
            l.Product.UnitsSold += l.Quantity;
            l.Product.UpdatedAt = now;
        }

        db.Orders.Add(order);

        await TrustAwardHelper.TryAwardUserTrustAsync(
            db,
            trustLedger,
            buyer,
            TrustCompletionBonuses.BuyerOnPurchaseCompleted,
            TrustCompletionBonuses.BuyerPurchaseReason,
            cancellationToken).ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await SafeNotifyAsync(
            ctx.Store.OwnerUserId,
            "Nuevo pedido",
            $"Recibiste el pedido {order.PublicNumber}.",
            deepLink: $"/almacen/pedidos/{order.Id}",
            cancellationToken).ConfigureAwait(false);

        return (
            new CreateOrderResponse(
                order.Id,
                order.PublicNumber,
                order.Status,
                order.CurrencyCode,
                order.Subtotal,
                order.DeliveryFee,
                order.Total),
            null);
    }

    public async Task<(OrderDetailDto? Value, OrderError? Error)> GetForSellerAsync(
        string userId,
        string orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadWithLinesAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null)
            return (null, new OrderError("order_not_found", "No se encontró el pedido."));
        if (!IsSeller(order, userId))
            return (null, new OrderError("forbidden", "Solo el vendedor puede ver este pedido."));
        return (MapDetail(order), null);
    }

    public async Task<IReadOnlyList<OrderSummaryDto>> ListForStoreAsync(
        string userId,
        string storeId,
        CancellationToken cancellationToken = default)
    {
        var uid = (userId ?? "").Trim();
        var sid = (storeId ?? "").Trim();
        if (uid.Length < 2 || sid.Length < 2)
            return Array.Empty<OrderSummaryDto>();

        var owns = await db.Stores.AsNoTracking()
            .AnyAsync(s => s.Id == sid && s.OwnerUserId == uid, cancellationToken)
            .ConfigureAwait(false);
        if (!owns)
            return Array.Empty<OrderSummaryDto>();

        var rows = await db.Orders.AsNoTracking()
            .Where(o => o.StoreId == sid)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(MapSummary).ToList();
    }

    public async Task<(OrderDetailDto? Value, OrderError? Error)> AdvanceStatusAsync(
        string userId,
        string orderId,
        string toStatus,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadWithLinesAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null)
            return (null, new OrderError("order_not_found", "No se encontró el pedido."));
        if (!IsSeller(order, userId))
            return (null, new OrderError("forbidden", "Solo el vendedor puede avanzar el pedido."));
        if (order.IsInvalidated)
            return (null, new OrderError("order_invalidated", "El pedido está invalidado."));

        var target = (toStatus ?? "").Trim();
        if (!OrderStateGraph.CanTransition(order.Status, target))
            return (null, new OrderError("invalid_transition", $"No se puede pasar de «{order.Status}» a «{target}»."));

        // La transición a «Entregado» ocurre cuando el comprador acepta la evidencia (ver DecideClientEvidence).
        if (target == OrderStatuses.Entregado)
            return (null, new OrderError("use_evidence_flow", "El pedido se marca entregado cuando el comprador acepta la evidencia."));

        var now = DateTimeOffset.UtcNow;
        order.Status = target;
        order.UpdatedAtUtc = now;

        // En «En tránsito» se disparan las deudas de almacén/afiliado (ver Features/Debts, que consumen estos flags).
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (target == OrderStatuses.EnTransito)
        {
            await orderRouteLifecycle.OnOrderInTransitAsync(order.Id, cancellationToken).ConfigureAwait(false);
            await debts.RecordWarehouseAndAffiliateDebtsAsync(order.Id, cancellationToken).ConfigureAwait(false);
        }

        await SafeNotifyAsync(
            order.BuyerUserId,
            "Tu pedido está en camino",
            $"El pedido {order.PublicNumber} salió del almacén.",
            deepLink: $"/rastreo/{order.PublicNumber}",
            cancellationToken).ConfigureAwait(false);

        return (MapDetail(order), null);
    }

    public async Task<(OrderDetailDto? Value, OrderError? Error)> UploadClientEvidenceAsync(
        string userId,
        string orderId,
        IReadOnlyList<string> urls,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadWithLinesAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null)
            return (null, new OrderError("order_not_found", "No se encontró el pedido."));
        if (!IsSeller(order, userId))
            return (null, new OrderError("forbidden", "Solo el vendedor puede subir evidencia."));
        if (order.IsInvalidated)
            return (null, new OrderError("order_invalidated", "El pedido está invalidado."));
        if (order.Status != OrderStatuses.EnTransito)
            return (null, new OrderError("evidence_not_allowed", "Solo se puede subir evidencia con el pedido en tránsito."));

        // La evidencia tienda → cliente se bloquea hasta que todos los tramos de la hoja de ruta estén resueltos.
        if (!await orderRouteLifecycle.AllMerchandiseLegsResolvedAsync(order.Id, cancellationToken).ConfigureAwait(false))
            return (null, new OrderError("legs_pending", "Aún hay tramos de la hoja de ruta sin resolver."));

        var clean = (urls ?? Array.Empty<string>())
            .Select(u => (u ?? "").Trim())
            .Where(u => u.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (clean.Count == 0)
            return (null, new OrderError("evidence_empty", "Adjunta al menos una evidencia."));

        var now = DateTimeOffset.UtcNow;
        order.ClientEvidenceUrls = clean;
        order.ClientEvidenceNote = string.IsNullOrWhiteSpace(note) ? null : note!.Trim();
        order.ClientEvidenceSubmittedAtUtc = now;
        order.ClientEvidenceDecision = OrderClientEvidenceDecisions.Pending;
        order.ClientEvidenceRejectReason = null;
        order.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await SafeNotifyAsync(
            order.BuyerUserId,
            "Confirma la entrega",
            $"La tienda subió la evidencia de entrega del pedido {order.PublicNumber}.",
            deepLink: $"/rastreo/{order.PublicNumber}",
            cancellationToken).ConfigureAwait(false);

        return (MapDetail(order), null);
    }

    public async Task<(OrderDetailDto? Value, OrderError? Error)> InvalidateAsync(
        string userId,
        string orderId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadWithLinesAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null)
            return (null, new OrderError("order_not_found", "No se encontró el pedido."));
        if (!IsSeller(order, userId))
            return (null, new OrderError("forbidden", "Solo el vendedor puede invalidar el pedido."));
        if (order.Status == OrderStatuses.Entregado)
            return (null, new OrderError("order_delivered", "No se puede invalidar un pedido entregado."));
        if (order.IsInvalidated)
            return (null, new OrderError("already_invalidated", "El pedido ya está invalidado."));

        var now = DateTimeOffset.UtcNow;
        order.IsInvalidated = true;
        order.InvalidatedAtUtc = now;
        order.InvalidatedReason = string.IsNullOrWhiteSpace(reason) ? null : reason!.Trim();
        if (order.PaymentStatus == OrderPaymentStatuses.Held)
        {
            order.PaymentStatus = OrderPaymentStatuses.Refunded;
            order.PaymentRefundedAtUtc = now;
        }
        order.UpdatedAtUtc = now;

        // Penalización de confianza a la tienda por invalidar un pedido de mercancía (wiki cap. 10).
        await TrustAwardHelper.TryAwardStoreTrustAsync(
            db,
            trustLedger,
            order.StoreId,
            TrustPenalties.StoreInvalidateOrder,
            TrustPenalties.StoreInvalidateOrderReason,
            cancellationToken).ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await SafeNotifyAsync(
            order.BuyerUserId,
            "Pedido cancelado",
            $"El pedido {order.PublicNumber} fue invalidado y tu pago fue reembolsado.",
            deepLink: $"/rastreo/{order.PublicNumber}",
            cancellationToken).ConfigureAwait(false);

        return (MapDetail(order), null);
    }

    public async Task<IReadOnlyList<OrderSummaryDto>> ListMyOrdersAsync(
        string buyerUserId,
        CancellationToken cancellationToken = default)
    {
        var buyer = (buyerUserId ?? "").Trim();
        if (buyer.Length < 2)
            return Array.Empty<OrderSummaryDto>();

        var rows = await db.Orders.AsNoTracking()
            .Where(o => o.BuyerUserId == buyer)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(MapSummary).ToList();
    }

    public async Task<(OrderTrackingDto? Value, OrderError? Error)> GetTrackingByPublicNumberAsync(
        string buyerUserId,
        string publicNumber,
        CancellationToken cancellationToken = default)
    {
        var buyer = (buyerUserId ?? "").Trim();
        var key = (publicNumber ?? "").Trim();
        if (key.Length == 0)
            return (null, new OrderError("order_not_found", "No se encontró el pedido."));

        var order = await db.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.PublicNumber == key, cancellationToken)
            .ConfigureAwait(false);
        if (order is null)
            return (null, new OrderError("order_not_found", "No se encontró el pedido."));
        if (!string.Equals(order.BuyerUserId, buyer, StringComparison.Ordinal))
            return (null, new OrderError("forbidden", "Este pedido no te pertenece."));
        var storeName = await StoreNameForAsync(order.StoreId, cancellationToken).ConfigureAwait(false);
        return (MapTracking(order, storeName), null);
    }

    public async Task<(OrderTrackingDto? Value, OrderError? Error)> DecideClientEvidenceAsync(
        string buyerUserId,
        string orderId,
        bool accept,
        string? rejectReason,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadWithLinesAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null)
            return (null, new OrderError("order_not_found", "No se encontró el pedido."));
        if (!string.Equals(order.BuyerUserId, (buyerUserId ?? "").Trim(), StringComparison.Ordinal))
            return (null, new OrderError("forbidden", "Este pedido no te pertenece."));
        if (order.ClientEvidenceDecision != OrderClientEvidenceDecisions.Pending)
            return (null, new OrderError("no_pending_evidence", "No hay evidencia pendiente de revisión."));

        var now = DateTimeOffset.UtcNow;
        if (accept)
        {
            order.ClientEvidenceDecision = OrderClientEvidenceDecisions.Accepted;
            order.ClientEvidenceDecidedAtUtc = now;
            order.Status = OrderStatuses.Entregado;
            if (order.PaymentStatus == OrderPaymentStatuses.Held)
            {
                order.PaymentStatus = OrderPaymentStatuses.Released;
                order.PaymentReleasedAtUtc = now;
            }
            order.UpdatedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Al aceptarse la evidencia, se generan y auto-liquidan las deudas de transportista de los tramos.
            await debts.RecordCarrierDebtsOnDeliveredAsync(order.Id, cancellationToken).ConfigureAwait(false);

            await SafeNotifyAsync(
                order.SellerUserId,
                "Entrega confirmada",
                $"El comprador aceptó la entrega del pedido {order.PublicNumber}. Pago liberado.",
                deepLink: $"/almacen/pedidos/{order.Id}",
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            order.ClientEvidenceDecision = OrderClientEvidenceDecisions.Rejected;
            order.ClientEvidenceDecidedAtUtc = now;
            order.ClientEvidenceRejectReason = string.IsNullOrWhiteSpace(rejectReason) ? null : rejectReason!.Trim();
            order.UpdatedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await SafeNotifyAsync(
                order.SellerUserId,
                "Evidencia rechazada",
                $"El comprador rechazó la evidencia del pedido {order.PublicNumber}.",
                deepLink: $"/almacen/pedidos/{order.Id}",
                cancellationToken).ConfigureAwait(false);
        }

        var storeName = await StoreNameForAsync(order.StoreId, cancellationToken).ConfigureAwait(false);
        return (MapTracking(order, storeName), null);
    }

    private sealed record CheckoutLine(StoreProductRow Product, int Quantity, decimal UnitPrice);

    private sealed record CheckoutContext(
        StoreRow Store,
        string CurrencyCode,
        string DeliveryMode,
        decimal Subtotal,
        decimal DeliveryFee,
        decimal Total,
        double? RouteDistanceKm,
        IReadOnlyList<CheckoutLine> Lines);

    private async Task<(CheckoutContext? Value, OrderError? Error)> BuildCheckoutContextAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request?.Lines is not { Count: > 0 })
            return (null, new OrderError("empty_cart", "El carrito no tiene productos."));

        var deliveryMode = (request.DeliveryMode ?? "").Trim();
        if (!OrderDeliveryModes.IsKnown(deliveryMode))
            return (null, new OrderError("invalid_delivery_mode", "Modalidad de entrega no válida."));

        var normalized = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in request.Lines)
        {
            var pid = (line.ProductId ?? "").Trim();
            if (pid.Length == 0 || line.Quantity <= 0)
                return (null, new OrderError("invalid_line", "Hay una línea de carrito no válida."));
            normalized[pid] = normalized.TryGetValue(pid, out var q) ? q + line.Quantity : line.Quantity;
        }

        var ids = normalized.Keys.ToArray();
        var products = await db.StoreProducts
            .Where(p => ids.Contains(p.Id) && p.Published)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (products.Count != ids.Length)
            return (null, new OrderError("product_unavailable", "Uno o más productos no están disponibles."));

        var storeIds = products.Select(p => p.StoreId).Distinct(StringComparer.Ordinal).ToArray();
        if (storeIds.Length != 1)
            return (null, new OrderError("multiple_stores", "El pedido debe ser de una sola tienda."));

        var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == storeIds[0], cancellationToken)
            .ConfigureAwait(false);
        if (store is null)
            return (null, new OrderError("store_not_found", "No se encontró la tienda."));

        var currencies = products
            .Select(p => OrderPricing.NormalizeCurrency(p.MonedaPrecio))
            .Where(c => c is not null)
            .Select(c => c!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (currencies.Length == 0)
            return (null, new OrderError("missing_currency", "Los productos no tienen moneda definida."));
        if (currencies.Length > 1)
            return (null, new OrderError("multiple_currencies", "El pedido debe usar una sola moneda."));
        var currency = currencies[0];

        var lines = new List<CheckoutLine>();
        decimal subtotal = 0;
        foreach (var product in products)
        {
            var qty = normalized[product.Id];
            if (product.StockQuantity is int stock && qty > stock)
                return (null, new OrderError("insufficient_stock", $"Stock insuficiente para {product.Name}."));
            if (!OrderPricing.TryParseDecimal(product.Price, out var unit) || unit < 0)
                return (null, new OrderError("invalid_price", $"Precio no válido para {product.Name}."));
            var unitRounded = OrderPricing.Round(unit);
            subtotal += unitRounded * qty;
            lines.Add(new CheckoutLine(product, qty, unitRounded));
        }
        subtotal = OrderPricing.Round(subtotal);

        decimal deliveryFee = 0;
        double? distanceKm = null;
        if (deliveryMode == OrderDeliveryModes.Shipping)
        {
            if (request.DeliveryLatitude is not double lat || request.DeliveryLongitude is not double lng)
                return (null, new OrderError("missing_delivery_location", "Falta la ubicación de entrega."));
            if (string.IsNullOrWhiteSpace(request.DeliveryAddress))
                return (null, new OrderError("missing_delivery_address", "Falta la dirección de entrega."));

            if (store.LocationLatitude is double slat && store.LocationLongitude is double slng)
            {
                distanceKm = Math.Round(OrderPricing.HaversineKm(slat, slng, lat, lng), 3);
                deliveryFee = OrderPricing.Round((decimal)distanceKm.Value * store.PricePerKm);
            }
        }

        var total = OrderPricing.Round(subtotal + deliveryFee);
        return (new CheckoutContext(store, currency, deliveryMode, subtotal, deliveryFee, total, distanceKm, lines), null);
    }

    private Task<OrderRow?> LoadWithLinesAsync(string orderId, CancellationToken cancellationToken)
    {
        var id = (orderId ?? "").Trim();
        if (id.Length == 0)
            return Task.FromResult<OrderRow?>(null);
        return db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    private static bool IsSeller(OrderRow order, string? userId)
    {
        var uid = (userId ?? "").Trim();
        return uid.Length >= 2 && string.Equals(order.SellerUserId, uid, StringComparison.Ordinal);
    }

    private async Task<string> NextPublicNumberAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var candidate = $"VT-{Random.Shared.Next(0, 100_000_000):D8}";
            var exists = await db.Orders.AsNoTracking()
                .AnyAsync(o => o.PublicNumber == candidate, cancellationToken)
                .ConfigureAwait(false);
            if (!exists)
                return candidate;
        }

        throw new InvalidOperationException("No se pudo generar un número de pedido único.");
    }

    private async Task SafeNotifyAsync(
        string? userId,
        string title,
        string body,
        string? deepLink,
        CancellationToken cancellationToken)
    {
        var uid = (userId ?? "").Trim();
        if (uid.Length < 2)
            return;
        try
        {
            await notifications.RequestUserNotificationAsync(uid, title, body, null, deepLink, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // Notificar no debe revertir la operación de negocio.
        }
    }

    private static OrderLineDto MapLine(OrderLineRow l) =>
        new(
            l.Id,
            l.ProductId,
            l.ProductName,
            l.TechnicalSpecs,
            l.Quantity,
            l.UnitPrice,
            OrderPricing.Round(l.UnitPrice * l.Quantity),
            l.CurrencyCode);

    private static OrderDetailDto MapDetail(OrderRow o) =>
        new(
            o.Id,
            o.PublicNumber,
            o.Status,
            o.BuyerUserId,
            o.StoreId,
            o.SellerUserId,
            o.CustomerFirstName,
            o.CustomerLastName,
            o.PhonePrimary,
            o.PhoneSecondary,
            o.DeliveryMode,
            o.DeliveryAddress,
            o.DeliveryLatitude,
            o.DeliveryLongitude,
            o.CurrencyCode,
            o.Subtotal,
            o.DeliveryFee,
            o.Total,
            o.PaymentStatus,
            o.PaymentMethod,
            o.ClientEvidenceDecision,
            o.ClientEvidenceUrls,
            o.ClientEvidenceNote,
            o.ClientEvidenceSubmittedAtUtc,
            o.ClientEvidenceDecidedAtUtc,
            o.ClientEvidenceRejectReason,
            o.RouteSheetId,
            o.IsInvalidated,
            o.CreatedAtUtc,
            o.Lines.OrderBy(l => l.ProductName, StringComparer.Ordinal).Select(MapLine).ToList());

    /// <summary>Nombre de la tienda para el comprobante; incluye tiendas dadas de baja (histórico) y cae a "" si falta.</summary>
    private async Task<string> StoreNameForAsync(string storeId, CancellationToken cancellationToken) =>
        await db.Stores.AsNoTracking().IgnoreQueryFilters()
            .Where(s => s.Id == storeId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? "";

    private static OrderTrackingDto MapTracking(OrderRow o, string storeName) =>
        new(
            o.Id,
            o.PublicNumber,
            o.Status,
            o.StoreId,
            storeName,
            o.DeliveryMode,
            o.DeliveryAddress,
            o.CurrencyCode,
            o.Subtotal,
            o.DeliveryFee,
            o.Total,
            o.PaymentStatus,
            o.ClientEvidenceDecision,
            o.ClientEvidenceUrls,
            o.ClientEvidenceNote,
            o.ClientEvidenceSubmittedAtUtc,
            o.RouteSheetId,
            o.RouteDistanceKm,
            o.CreatedAtUtc,
            o.Lines.OrderBy(l => l.ProductName, StringComparer.Ordinal).Select(MapLine).ToList());

    private static OrderSummaryDto MapSummary(OrderRow o) =>
        new(
            o.Id,
            o.PublicNumber,
            o.Status,
            o.StoreId,
            o.CurrencyCode,
            o.Total,
            o.PaymentStatus,
            o.CreatedAtUtc);
}
