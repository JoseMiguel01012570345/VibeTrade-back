using MediatR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Dtos;
using VibeTrade.Backend.Features.Offers.Interfaces;
using VibeTrade.Backend.Features.Offers.OffersMediator.ToggleOfferLike;
using VibeTrade.Backend.Features.Recommendations.Interfaces;

namespace VibeTrade.Backend.Features.Offers;

public sealed class OfferService(
    AppDbContext db,
    IMediator mediator) : IOfferService
{
    public async Task<bool> OfferExistsAsync(string offerId, CancellationToken cancellationToken = default)
    {
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2)
            return false;
        if (OfferUtils.IsEmergentPublicationId(oid))
        {
            return await db.EmergentOffers.AsNoTracking()
                .AnyAsync(e =>
                    e.Id == oid
                    && e.RetractedAtUtc == null
                    && db.ChatRouteSheets.Any(r =>
                        r.ThreadId == e.ThreadId
                        && r.RouteSheetId == e.RouteSheetId
                        && r.DeletedAtUtc == null
                        && r.PublishedToPlatform),
                    cancellationToken);
        }
        if (await db.StoreProducts.AsNoTracking().AnyAsync(p => p.Id == oid, cancellationToken))
            return true;
        return await db.StoreServices.AsNoTracking().AnyAsync(s => s.Id == oid, cancellationToken);
    }

    public async Task EnrichHomeOffersAsync(
        Dictionary<string, HomeOfferViewDto> offers,
        string? likerKey,
        CancellationToken cancellationToken = default)
    {
        if (offers.Count == 0)
            return;

        var ids = offers.Keys.ToList();
        var likeCounts = await db.OfferLikes.AsNoTracking()
            .Where(x => ids.Contains(x.OfferId))
            .GroupBy(x => x.OfferId)
            .Select(g => new { OfferId = g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.OfferId, x => x.C, StringComparer.Ordinal, cancellationToken);

        HashSet<string>? viewerOfferIds = null;
        if (!string.IsNullOrEmpty(likerKey))
        {
            var liked = await db.OfferLikes.AsNoTracking()
                .Where(x => ids.Contains(x.OfferId) && x.LikerKey == likerKey)
                .Select(x => x.OfferId)
                .ToListAsync(cancellationToken);
            viewerOfferIds = liked.ToHashSet(StringComparer.Ordinal);
        }

        foreach (var kv in offers)
        {
            var oid = kv.Key;
            var obj = kv.Value;
            obj.PublicCommentCount = 0;
            obj.OfferLikeCount = likeCounts.GetValueOrDefault(oid, 0);
            obj.ViewerLikedOffer = viewerOfferIds is not null && viewerOfferIds.Contains(oid);
        }
    }

    public async Task EnrichStoreCatalogBlockEngagementAsync(
        IReadOnlyList<StoreProductCatalogRowView> products,
        IReadOnlyList<StoreServiceCatalogRowView> services,
        string? likerKey,
        CancellationToken cancellationToken = default)
    {
        var ids = new List<string>(products.Count + services.Count);
        foreach (var p in products)
        {
            var oid = p.Id?.Trim() ?? "";
            if (oid.Length >= 2)
                ids.Add(oid);
        }

        foreach (var s in services)
        {
            var oid = s.Id?.Trim() ?? "";
            if (oid.Length >= 2)
                ids.Add(oid);
        }

        if (ids.Count == 0)
            return;

        var likeCounts = await db.OfferLikes.AsNoTracking()
            .Where(x => ids.Contains(x.OfferId))
            .GroupBy(x => x.OfferId)
            .Select(g => new { OfferId = g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.OfferId, x => x.C, StringComparer.Ordinal, cancellationToken);

        HashSet<string>? viewerOfferIds = null;
        if (!string.IsNullOrEmpty(likerKey))
        {
            var liked = await db.OfferLikes.AsNoTracking()
                .Where(x => ids.Contains(x.OfferId) && x.LikerKey == likerKey)
                .Select(x => x.OfferId)
                .ToListAsync(cancellationToken);
            viewerOfferIds = liked.ToHashSet(StringComparer.Ordinal);
        }

        foreach (var p in products)
        {
            var oid = p.Id?.Trim() ?? "";
            if (oid.Length < 2)
                continue;
            p.PublicCommentCount = 0;
            p.OfferLikeCount = likeCounts.GetValueOrDefault(oid, 0);
            p.ViewerLikedOffer = viewerOfferIds is not null && viewerOfferIds.Contains(oid);
        }

        foreach (var s in services)
        {
            var oid = s.Id?.Trim() ?? "";
            if (oid.Length < 2)
                continue;
            s.PublicCommentCount = 0;
            s.OfferLikeCount = likeCounts.GetValueOrDefault(oid, 0);
            s.ViewerLikedOffer = viewerOfferIds is not null && viewerOfferIds.Contains(oid);
        }
    }

    public async Task<(bool Liked, int LikeCount)> ToggleOfferLikeAsync(
        string offerId,
        string likerKey,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(new ToggleOfferLikeCommand(offerId, likerKey), cancellationToken);
        return (result.Liked, result.LikeCount);
    }

    public HomeOfferViewDto FromProductRow(StoreProductRow p)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.Category))
            tags.Add(p.Category.Trim());
        if (!string.IsNullOrWhiteSpace(p.Condition))
            tags.Add(p.Condition.Trim());
        tags.Add("Producto");

        var price = OfferUtils.FormatProductPrice(p);
        var title = string.IsNullOrWhiteSpace(p.Name) ? "Producto" : p.Name.Trim();
        var photoUrls = MarketCatalogPhotoRules.CollectDisplayablePhotoUrls(p.PhotoUrls);
        var primary = photoUrls.Count > 0 ? photoUrls[0] : null;
        var accepted = CatalogJsonColumnParsing.StringListOrEmpty(p.Monedas);
        var currency = (p.MonedaPrecio ?? "").Trim();

        return new HomeOfferViewDto
        {
            Id = p.Id,
            StoreId = p.StoreId,
            Title = title,
            Price = price,
            Currency = currency.Length == 0 ? null : currency,
            AcceptedCurrencies = accepted,
            Description = OfferUtils.OfferDescriptionForProduct(p),
            Tags = tags,
            ImageUrl = primary,
            ImageUrls = photoUrls,
        };
    }

    public HomeOfferViewDto FromServiceRow(StoreServiceRow s)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(s.Category))
            tags.Add(s.Category.Trim());
        if (!string.IsNullOrWhiteSpace(s.NombreServicio))
            tags.Add(s.NombreServicio.Trim());
        tags.Add("Servicio");

        var title = !string.IsNullOrWhiteSpace(s.NombreServicio)
            ? s.NombreServicio.Trim()
            : (!string.IsNullOrWhiteSpace(s.Category) ? s.Category.Trim() : "Servicio");
        var photoUrls = MarketCatalogPhotoRules.CollectServiceOfferGalleryUrls(s);
        var primary = photoUrls.Count > 0 ? photoUrls[0] : MarketCatalogConstants.DefaultServiceOfferImageUrl;
        var imageUrls = photoUrls.Count > 0
            ? (IReadOnlyList<string>)photoUrls
            : new[] { MarketCatalogConstants.DefaultServiceOfferImageUrl };

        var o = new HomeOfferViewDto
        {
            Id = s.Id,
            StoreId = s.StoreId,
            Title = title,
            Price = OfferUtils.FormatServicePriceLine(s),
            AcceptedCurrencies = string.IsNullOrWhiteSpace(s.CurrencyCode) ? new[] { "USD" } : new[] { s.CurrencyCode.Trim().ToUpperInvariant() },
            Description = (s.Descripcion ?? "").Trim(),
            Tags = tags,
            ImageUrl = primary,
            ImageUrls = imageUrls,
        };
        if (!string.IsNullOrWhiteSpace(s.Category))
            o.Category = s.Category.Trim();
        if (!string.IsNullOrWhiteSpace(s.NombreServicio))
            o.NombreServicio = s.NombreServicio.Trim();
        if (!string.IsNullOrWhiteSpace(s.Incluye))
            o.Incluye = s.Incluye.Trim();
        if (!string.IsNullOrWhiteSpace(s.NoIncluye))
            o.NoIncluye = s.NoIncluye.Trim();
        if (!string.IsNullOrWhiteSpace(s.Entregables))
            o.Entregables = s.Entregables.Trim();
        if (!string.IsNullOrWhiteSpace(s.PropIntelectual))
            o.PropIntelectual = s.PropIntelectual.Trim();
        return o;
    }

    public StoreProductCatalogRowView ProductCatalogRowFromEntity(StoreProductRow p) =>
        new()
        {
            Id = p.Id,
            StoreId = p.StoreId,
            Category = p.Category,
            Name = p.Name,
            ShortDescription = p.ShortDescription,
            MainBenefit = p.MainBenefit,
            TechnicalSpecs = p.TechnicalSpecs,
            Model = string.IsNullOrEmpty(p.Model) ? null : p.Model,
            Condition = p.Condition,
            Price = p.Price,
            MonedaPrecio = string.IsNullOrEmpty(p.MonedaPrecio) ? null : p.MonedaPrecio,
            Monedas = CatalogJsonColumnParsing.StringListOrEmpty(p.Monedas),
            Availability = p.Availability,
            WarrantyReturn = p.WarrantyReturn,
            ContentIncluded = p.ContentIncluded,
            UsageConditions = p.UsageConditions,
            Published = p.Published,
            StockQuantity = p.StockQuantity,
            UpdatedAt = p.UpdatedAt,
            PendingApproval = p.PendingApproval,
            SupplierId = p.SupplierId,
            CategoryIds = CatalogJsonColumnParsing.StringListOrEmpty(p.CategoryIds),
            CategoryId = ResolvePrimaryCategoryId(p.CategoryIds),
            SubcategoryId = ResolveSubcategoryId(p.CategoryIds),
            TaxesShippingInstall = string.IsNullOrEmpty(p.TaxesShippingInstall) ? null : p.TaxesShippingInstall,
            TransportIncluded = p.TransportIncluded,
            PhotoUrls = CatalogJsonColumnParsing.StringListOrEmpty(p.PhotoUrls),
            CustomFields = CatalogJsonColumnParsing.CustomFieldsListOrEmpty(p.CustomFields),
        };

    public StoreServiceCatalogRowView ServiceCatalogRowFromEntity(StoreServiceRow s)
    {
        var urls = MarketCatalogPhotoRules.CollectDisplayablePhotoUrls(s.PhotoUrls);
        return new StoreServiceCatalogRowView
        {
            Id = s.Id,
            StoreId = s.StoreId,
            Category = s.Category,
            NombreServicio = s.NombreServicio,
            Descripcion = s.Descripcion,
            Incluye = s.Incluye,
            NoIncluye = s.NoIncluye,
            Entregables = s.Entregables,
            PropIntelectual = s.PropIntelectual,
            Published = s.Published,
            FixedPrice = s.FixedPrice,
            CurrencyCode = string.IsNullOrWhiteSpace(s.CurrencyCode) ? "USD" : s.CurrencyCode,
            RecurrenceMonth = s.RecurrenceMonth,
            RecurrenceDay = s.RecurrenceDay,
            CustomFields = CatalogJsonColumnParsing.CustomFieldsListOrEmpty(s.CustomFields),
            PhotoUrls = urls,
            Riesgos = s.Riesgos,
            Dependencias = s.Dependencias,
            Garantias = s.Garantias,
        };
    }

    public HomeOfferViewDto CreateEmergentRoutePublication(
        EmergentOfferRow e,
        StoreProductRow? p,
        StoreServiceRow? s,
        string? fallbackStoreId,
        RouteSheetPayload? liveRoutePayload = null)
    {
        var node = CreateEmergentRoutePublicationCore(e, p, s, fallbackStoreId);
        if (liveRoutePayload is not null)
            OfferUtils.ApplyLiveParadaStopIds(node, liveRoutePayload);
        return node;
    }

    private HomeOfferViewDto CreateEmergentRoutePublicationCore(
        EmergentOfferRow e,
        StoreProductRow? p,
        StoreServiceRow? s,
        string? fallbackStoreId)
    {
        var snap = e.RouteSheetSnapshot ?? new EmergentRouteSheetSnapshot();
        var storeIdForOrphan = (fallbackStoreId ?? "").Trim();
        var baseH = p is not null
            ? FromProductRow(p)
            : s is not null
                ? FromServiceRow(s)
                : new HomeOfferViewDto
                {
                    Id = e.Id,
                    Title = snap.Titulo,
                    StoreId = string.IsNullOrEmpty(storeIdForOrphan) ? "" : storeIdForOrphan,
                    Price = "—",
                };

        baseH.Id = e.Id;
        baseH.EmergentBaseOfferId = e.OfferId;
        baseH.EmergentThreadId = e.ThreadId;
        baseH.EmergentRouteSheetId = e.RouteSheetId;
        baseH.IsEmergentRoutePublication = true;
        if (!string.IsNullOrWhiteSpace(snap.MonedaPago))
            baseH.EmergentMonedaPago = snap.MonedaPago.Trim();
        if (!string.IsNullOrWhiteSpace(snap.Titulo))
            baseH.Title = snap.Titulo.Trim();
        var routeLine = OfferUtils.RouteSummaryLine(snap);
        var prevDesc = baseH.Description ?? "";
        if (!string.IsNullOrWhiteSpace(routeLine))
        {
            baseH.Description = string.IsNullOrWhiteSpace(prevDesc)
                ? routeLine
                : routeLine + "\n\n" + prevDesc;
        }

        baseH.Tags.Add("Hoja de ruta (publicada)");

        var paradasSnap = snap.Paradas ?? [];
        if (paradasSnap.Count > 0)
        {
            var paradas = new List<EmergentRouteParadaViewDto>();
            var idx = 0;
            foreach (var leg in paradasSnap)
            {
                idx++;
                var orden = leg.Orden > 0 ? leg.Orden : idx;
                var v = new EmergentRouteParadaViewDto
                {
                    Origen = leg.Origen?.Trim() ?? "",
                    Destino = leg.Destino?.Trim() ?? "",
                    Orden = orden,
                };
                var sid = (leg.StopId ?? "").Trim();
                if (sid.Length > 0)
                    v.StopId = sid;
                if (!string.IsNullOrWhiteSpace(leg.OrigenLat)) v.OrigenLat = leg.OrigenLat!.Trim();
                if (!string.IsNullOrWhiteSpace(leg.OrigenLng)) v.OrigenLng = leg.OrigenLng!.Trim();
                if (!string.IsNullOrWhiteSpace(leg.DestinoLat)) v.DestinoLat = leg.DestinoLat!.Trim();
                if (!string.IsNullOrWhiteSpace(leg.DestinoLng)) v.DestinoLng = leg.DestinoLng!.Trim();
                if (!string.IsNullOrWhiteSpace(leg.MonedaPago)) v.MonedaPago = leg.MonedaPago.Trim();
                if (!string.IsNullOrWhiteSpace(leg.PrecioTransportista))
                    v.PrecioTransportista = leg.PrecioTransportista.Trim();
                if (leg.OsrmRoadKm is double km && km >= 0 && !double.IsNaN(km) && !double.IsInfinity(km))
                    v.OsrmRoadKm = km;
                if (leg.OsrmRouteLatLngs is { Count: >= 2 })
                    v.OsrmRouteLatLngs = leg.OsrmRouteLatLngs;
                paradas.Add(v);
            }

            baseH.EmergentRouteParadas = paradas;
        }

        return baseH;
    }

    private static string? ResolvePrimaryCategoryId(IReadOnlyList<string> ids) =>
        ids.Count > 0 ? ids[0] : null;

    private static string? ResolveSubcategoryId(IReadOnlyList<string> ids) =>
        ids.Count > 1 ? ids[^1] : null;
}
