using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Market;
using VibeTrade.Backend.Features.Market.Dtos;
using VibeTrade.Backend.Features.Offers.Interfaces;
using VibeTrade.Backend.Features.Recommendations.Popularity;

namespace VibeTrade.Backend.Features.Offers;

public sealed class OfferService(
    AppDbContext db,
    IOfferPopularityWeightService popularityWeight) : IOfferService
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
            var n = obj.Qa?.Count ?? 0;
            obj.PublicCommentCount = n;
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
            p.PublicCommentCount = p.Qa?.Count ?? 0;
            p.OfferLikeCount = likeCounts.GetValueOrDefault(oid, 0);
            p.ViewerLikedOffer = viewerOfferIds is not null && viewerOfferIds.Contains(oid);
        }

        foreach (var s in services)
        {
            var oid = s.Id?.Trim() ?? "";
            if (oid.Length < 2)
                continue;
            s.PublicCommentCount = s.Qa?.Count ?? 0;
            s.OfferLikeCount = likeCounts.GetValueOrDefault(oid, 0);
            s.ViewerLikedOffer = viewerOfferIds is not null && viewerOfferIds.Contains(oid);
        }
    }

    public async Task<IReadOnlyList<OfferQaItemResponseDto>> EnrichOfferQaListAsync(
        string offerId,
        IReadOnlyList<OfferQaComment> qa,
        string? likerKey,
        CancellationToken cancellationToken = default)
    {
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2)
            return Array.Empty<OfferQaItemResponseDto>();

        var list = new List<OfferQaItemResponseDto>(qa.Count);
        foreach (var c in qa)
        {
            list.Add(new OfferQaItemResponseDto
            {
                Id = c.Id,
                Text = c.Text,
                Question = c.Question,
                ParentId = c.ParentId,
                AskedBy = c.AskedBy,
                Author = c.Author,
                CreatedAt = c.CreatedAt,
                Answer = c.Answer,
            });
        }

        if (list.Count == 0)
            return list;

        var commentIds = list.Select(x => x.Id).Where(id => id.Length > 0).ToList();
        if (commentIds.Count == 0)
            return list;

        var likeCounts = await db.OfferQaCommentLikes.AsNoTracking()
            .Where(x => x.OfferId == oid && commentIds.Contains(x.QaCommentId))
            .GroupBy(x => x.QaCommentId)
            .Select(g => new { QaCommentId = g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.QaCommentId, x => x.C, StringComparer.Ordinal, cancellationToken);

        HashSet<string>? viewerLikedIds = null;
        if (!string.IsNullOrEmpty(likerKey))
        {
            viewerLikedIds = (await db.OfferQaCommentLikes.AsNoTracking()
                .Where(x => x.OfferId == oid && commentIds.Contains(x.QaCommentId) && x.LikerKey == likerKey)
                .Select(x => x.QaCommentId)
                .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        }

        foreach (var o in list)
        {
            var cid = o.Id;
            o.LikeCount = likeCounts.GetValueOrDefault(cid, 0);
            o.ViewerLiked = viewerLikedIds is not null && viewerLikedIds.Contains(cid);
        }

        return list;
    }

    public async Task<(bool Liked, int LikeCount)> ToggleOfferLikeAsync(
        string offerId,
        string likerKey,
        CancellationToken cancellationToken = default)
    {
        var oid = (offerId ?? "").Trim();
        if (oid.Length < 2 || string.IsNullOrEmpty(likerKey))
            return (false, 0);

        if (!await OfferExistsAsync(oid, cancellationToken))
            return (false, 0);

        var existing = await db.OfferLikes
            .FirstOrDefaultAsync(x => x.OfferId == oid && x.LikerKey == likerKey, cancellationToken);

        if (existing is not null)
        {
            db.OfferLikes.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            await popularityWeight.RecomputeAsync(oid, cancellationToken);
            var c = await db.OfferLikes.CountAsync(x => x.OfferId == oid, cancellationToken);
            return (false, c);
        }

        db.OfferLikes.Add(new OfferLikeRow
        {
            Id = OfferUtils.NewId("olk_"),
            OfferId = oid,
            LikerKey = likerKey,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
        await popularityWeight.RecomputeAsync(oid, cancellationToken);
        var c2 = await db.OfferLikes.CountAsync(x => x.OfferId == oid, cancellationToken);
        return (true, c2);
    }

    public async Task<(bool Liked, int LikeCount)> ToggleQaCommentLikeAsync(
        string offerId,
        string qaCommentId,
        string likerKey,
        CancellationToken cancellationToken = default)
    {
        var oid = (offerId ?? "").Trim();
        var cid = (qaCommentId ?? "").Trim();
        if (oid.Length < 2 || cid.Length < 2 || string.IsNullOrEmpty(likerKey))
            return (false, 0);

        if (!await OfferExistsAsync(oid, cancellationToken))
            return (false, 0);

        var existing = await db.OfferQaCommentLikes
            .FirstOrDefaultAsync(
                x => x.OfferId == oid && x.QaCommentId == cid && x.LikerKey == likerKey,
                cancellationToken);

        if (existing is not null)
        {
            db.OfferQaCommentLikes.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            await popularityWeight.RecomputeAsync(oid, cancellationToken);
            var c = await db.OfferQaCommentLikes.CountAsync(
                x => x.OfferId == oid && x.QaCommentId == cid,
                cancellationToken);
            return (false, c);
        }

        db.OfferQaCommentLikes.Add(new OfferQaCommentLikeRow
        {
            Id = OfferUtils.NewId("oqk_"),
            OfferId = oid,
            QaCommentId = cid,
            LikerKey = likerKey,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
        await popularityWeight.RecomputeAsync(oid, cancellationToken);
        var c2 = await db.OfferQaCommentLikes.CountAsync(
            x => x.OfferId == oid && x.QaCommentId == cid,
            cancellationToken);
        return (true, c2);
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
            Qa = p.OfferQa ?? new List<OfferQaComment>(),
        };
    }

    public HomeOfferViewDto FromServiceRow(StoreServiceRow s)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(s.Category))
            tags.Add(s.Category.Trim());
        if (!string.IsNullOrWhiteSpace(s.TipoServicio))
            tags.Add(s.TipoServicio.Trim());
        tags.Add("Servicio");

        var title = !string.IsNullOrWhiteSpace(s.TipoServicio)
            ? s.TipoServicio.Trim()
            : (!string.IsNullOrWhiteSpace(s.Category) ? s.Category.Trim() : "Servicio");
        var photoUrls = MarketCatalogPhotoRules.CollectServiceOfferGalleryUrls(s);
        var primary = photoUrls.Count > 0 ? photoUrls[0] : MarketCatalogConstants.DefaultServiceOfferImageUrl;
        var imageUrls = photoUrls.Count > 0
            ? (IReadOnlyList<string>)photoUrls
            : new[] { MarketCatalogConstants.DefaultServiceOfferImageUrl };

        var accepted = CatalogJsonColumnParsing.StringListOrEmpty(s.Monedas);
        var o = new HomeOfferViewDto
        {
            Id = s.Id,
            StoreId = s.StoreId,
            Title = title,
            Price = OfferUtils.FormatServicePriceLine(s),
            AcceptedCurrencies = accepted,
            Description = (s.Descripcion ?? "").Trim(),
            Tags = tags,
            ImageUrl = primary,
            ImageUrls = imageUrls,
            Qa = s.OfferQa ?? new List<OfferQaComment>(),
        };
        if (!string.IsNullOrWhiteSpace(s.Category))
            o.Category = s.Category.Trim();
        if (!string.IsNullOrWhiteSpace(s.TipoServicio))
            o.TipoServicio = s.TipoServicio.Trim();
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
            TaxesShippingInstall = string.IsNullOrEmpty(p.TaxesShippingInstall) ? null : p.TaxesShippingInstall,
            TransportIncluded = p.TransportIncluded,
            PhotoUrls = CatalogJsonColumnParsing.StringListOrEmpty(p.PhotoUrls),
            CustomFields = CatalogJsonColumnParsing.CustomFieldsListOrEmpty(p.CustomFields),
            Qa = p.OfferQa ?? new List<OfferQaComment>(),
        };

    public StoreServiceCatalogRowView ServiceCatalogRowFromEntity(StoreServiceRow s)
    {
        var urls = MarketCatalogPhotoRules.CollectDisplayablePhotoUrls(s.PhotoUrls);
        return new StoreServiceCatalogRowView
        {
            Id = s.Id,
            StoreId = s.StoreId,
            Category = s.Category,
            TipoServicio = s.TipoServicio,
            Descripcion = s.Descripcion,
            Incluye = s.Incluye,
            NoIncluye = s.NoIncluye,
            Entregables = s.Entregables,
            PropIntelectual = s.PropIntelectual,
            Published = s.Published,
            Monedas = CatalogJsonColumnParsing.StringListOrEmpty(s.Monedas),
            CustomFields = CatalogJsonColumnParsing.CustomFieldsListOrEmpty(s.CustomFields),
            PhotoUrls = urls,
            Riesgos = s.Riesgos,
            Dependencias = s.Dependencias,
            Garantias = s.Garantias,
            Qa = s.OfferQa ?? new List<OfferQaComment>(),
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

        baseH.Qa = e.OfferQa ?? new List<OfferQaComment>();
        return baseH;
    }
}
