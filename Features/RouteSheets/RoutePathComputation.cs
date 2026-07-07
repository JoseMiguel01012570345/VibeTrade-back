using System.Globalization;
using VibeTrade.Backend.Features.RouteSheets.Dtos;

namespace VibeTrade.Backend.Features.RouteSheets;

/// <summary>
/// Rutas enlazadas en una hoja: cadenas de paradas donde destino[i-1] coincide con origen[i] (por coordenadas).
/// <see cref="RoutePathId"/> = id de la primera parada (cabeza) en la cadena.
/// </summary>
public static class RoutePathComputation
{
  public static IReadOnlyList<RoutePathDto> BuildRoutePaths(
    RouteSheetPayload sheet,
    IReadOnlySet<string>? paidStopIds = null,
    IReadOnlySet<string>? paidLikeDeliveryStopIds = null,
    IReadOnlySet<string>? confirmedStopIds = null)
  {
    var paradas = sheet.Paradas ?? [];
    if (paradas.Count == 0)
      return [];

    paidStopIds ??= new HashSet<string>(StringComparer.Ordinal);
    paidLikeDeliveryStopIds ??= new HashSet<string>(StringComparer.Ordinal);

    var chains = BuildTramoChainIndices(paradas);
    if (chains.Count == 0 || chains[0].Count == 0)
      return [];

    var chain = chains[0];
    var paths = new List<RoutePathDto>(1);

      var stopsInChain = chain
        .Select(i => paradas[i])
        .Where(p => !string.IsNullOrWhiteSpace(p.Id))
        .ToList();
      if (stopsInChain.Count == 0)
        return [];

      var head = stopsInChain[0];
      var routePathId = (head.Id ?? "").Trim();
      if (routePathId.Length == 0)
        return [];

      var stopIds = stopsInChain.Select(p => (p.Id ?? "").Trim()).Where(x => x.Length > 0).ToList();

      var paidCount = stopIds.Count(id =>
        paidStopIds.Contains(id) || paidLikeDeliveryStopIds.Contains(id));
      var partiallyPaid = paidCount > 0 && paidCount < stopIds.Count;
      var paid = paidCount == stopIds.Count && stopIds.Count > 0;

      var totals = ComputeTotalsByCurrency(stopsInChain, sheet);
      var hasPrices = totals.Count > 0 && AllStopsHavePrice(stopsInChain, sheet);
      var carriersOk = confirmedStopIds is null
                       || RouteSheetsChatServiceCore.AllStopsHaveConfirmedCarrier(stopsInChain, confirmedStopIds);
      var payable = !partiallyPaid && !paid && hasPrices && carriersOk;

      paths.Add(new RoutePathDto
      {
        RoutePathId = routePathId,
        Orden = 1,
        Label = BuildPathLabel(stopsInChain),
        StopIds = stopIds,
        Stops = stopsInChain.Select(p => new RoutePathStopSummaryDto
        {
          RouteStopId = (p.Id ?? "").Trim(),
          Orden = p.Orden,
          Origen = (p.Origen ?? "").Trim(),
          Destino = (p.Destino ?? "").Trim(),
          PrecioTransportista = p.PrecioTransportista,
          MonedaPago = p.MonedaPago,
        }).ToList(),
        TotalsByCurrency = totals,
        Payable = payable,
        Paid = paid,
        PartiallyPaid = partiallyPaid,
      });

    return paths;
  }

  public static HashSet<string> ExpandPathIdsToStopIds(
    RouteSheetPayload sheet,
    IReadOnlyList<string>? selectedRoutePathIds)
  {
    var result = new HashSet<string>(StringComparer.Ordinal);
    if (selectedRoutePathIds is null || selectedRoutePathIds.Count == 0)
      return result;

    var pathByHead = BuildRoutePaths(sheet).ToDictionary(p => p.RoutePathId, StringComparer.Ordinal);
    foreach (var pathId in selectedRoutePathIds)
    {
      var pid = (pathId ?? "").Trim();
      if (pid.Length == 0)
        continue;

      if (pathByHead.TryGetValue(pid, out var path)
          || (TryGetRoutePathIdForStop(sheet, pid) is { } pathHeadFromStop
              && pathByHead.TryGetValue(pathHeadFromStop, out path)
              && path.StopIds.Any(x => string.Equals(x, pid, StringComparison.Ordinal))))
      {
        foreach (var sid in path.StopIds)
        {
          var s = (sid ?? "").Trim();
          if (s.Length > 0)
            result.Add(s);
        }
      }
    }

    return result;
  }

  public static HashSet<string> ExpandPathSelectionToStopIds(
    RouteSheetPayload sheet,
    IReadOnlyList<string>? selectedRoutePathIds)
  {
    if (selectedRoutePathIds is null)
    {
      var all = new HashSet<string>(StringComparer.Ordinal);
      foreach (var path in BuildRoutePaths(sheet))
      {
        if (!path.Payable)
          continue;
        foreach (var sid in path.StopIds)
        {
          var s = (sid ?? "").Trim();
          if (s.Length > 0)
            all.Add(s);
        }
      }

      return all;
    }

    if (selectedRoutePathIds.Count == 0)
      return new HashSet<string>(StringComparer.Ordinal);

    return ExpandPathIdsToStopIds(sheet, selectedRoutePathIds);
  }

  public static string? TryGetRoutePathIdForStop(RouteSheetPayload sheet, string routeStopId)
  {
    var sid = (routeStopId ?? "").Trim();
    if (sid.Length == 0)
      return null;

    foreach (var path in BuildRoutePaths(sheet))
    {
      if (path.StopIds.Any(x => string.Equals(x, sid, StringComparison.Ordinal)))
        return path.RoutePathId;
    }

    return null;
  }

  public static HashSet<string> RoutePathHeadStopIds(
    RouteSheetPayload sheet,
    IReadOnlySet<string> paidStopIdsInCharge)
  {
    var heads = new HashSet<string>(StringComparer.Ordinal);
    foreach (var path in BuildRoutePaths(sheet))
    {
      if (path.StopIds.Any(sid => paidStopIdsInCharge.Contains(sid)))
        heads.Add(path.RoutePathId);
    }

    return heads;
  }

  /// <summary>
  /// Expande selección de rutas a paradas para checkout/cobro.
  /// <c>null</c> = todas las rutas payables; <c>[]</c> = sin transporte.
  /// </summary>
  public static RoutePathCheckoutResolveResult ResolveForCheckout(
    RouteSheetPayload sheet,
    IReadOnlyList<string>? selectedRoutePathIds,
    IReadOnlySet<string>? paidStopIds = null,
    IReadOnlySet<string>? paidLikeDeliveryStopIds = null,
    IReadOnlySet<string>? confirmedStopIds = null)
  {
    paidStopIds ??= new HashSet<string>(StringComparer.Ordinal);
    paidLikeDeliveryStopIds ??= new HashSet<string>(StringComparer.Ordinal);
    confirmedStopIds ??= new HashSet<string>(StringComparer.Ordinal);

    var paths = BuildRoutePaths(sheet, paidStopIds, paidLikeDeliveryStopIds, confirmedStopIds);
    var pathById = paths.ToDictionary(p => p.RoutePathId, StringComparer.Ordinal);
    var errors = new List<string>();

    if (selectedRoutePathIds is null)
    {
      var allPayable = new HashSet<string>(StringComparer.Ordinal);
      foreach (var path in paths.Where(p => p.Payable))
      {
        foreach (var sid in path.StopIds)
        {
          var s = (sid ?? "").Trim();
          if (s.Length > 0)
            allPayable.Add(s);
        }
      }

      return new RoutePathCheckoutResolveResult(allPayable, errors);
    }

    if (selectedRoutePathIds.Count == 0)
      return new RoutePathCheckoutResolveResult(new HashSet<string>(StringComparer.Ordinal), errors);

    var expanded = new HashSet<string>(StringComparer.Ordinal);
    foreach (var raw in selectedRoutePathIds)
    {
      var pid = (raw ?? "").Trim();
      if (pid.Length == 0)
        continue;

      RoutePathDto? path = null;
      if (!pathById.TryGetValue(pid, out path))
      {
        var pathHeadFromStop = TryGetRoutePathIdForStop(sheet, pid);
        if (pathHeadFromStop is null
            || !pathById.TryGetValue(pathHeadFromStop, out path)
            || !path.StopIds.Any(x => string.Equals(x, pid, StringComparison.Ordinal)))
        {
          errors.Add("Selección de ruta inválida.");
          continue;
        }
      }

      if (path.PartiallyPaid)
      {
        errors.Add($"La ruta «{path.Label}» está parcialmente pagada; solo se puede cobrar la ruta completa.");
        continue;
      }

      if (path.Paid)
      {
        errors.Add($"La ruta «{path.Label}» ya está pagada.");
        continue;
      }

      if (!path.Payable)
      {
        if (RouteSheetsChatServiceCore.PathMissingConfirmedCarriers(path, confirmedStopIds))
          errors.Add($"La ruta «{path.Label}» no es cobrable: faltan transportistas confirmados en uno o más tramos.");
        else
          errors.Add($"La ruta «{path.Label}» no es cobrable (revisa precios y moneda).");
        continue;
      }

      foreach (var sid in path.StopIds)
      {
        var s = (sid ?? "").Trim();
        if (s.Length > 0)
          expanded.Add(s);
      }
    }

    return new RoutePathCheckoutResolveResult(expanded, errors);
  }

  /// <summary>Índices en <paramref name="paradas"/> de la cadena única (ordenadas por <see cref="RouteStopPayload.Orden"/>).</summary>
  public static List<List<int>> BuildTramoChainIndices(IReadOnlyList<RouteStopPayload> paradas)
  {
    var ordered = RouteSheetUtils.OrderParadaIndices(paradas);
    return ordered.Count == 0 ? [] : [ordered];
  }

  private static bool AllStopsHavePrice(IReadOnlyList<RouteStopPayload> stops, RouteSheetPayload sheet) =>
    stops.All(p => TryParseStopAmount(p, sheet, out _, out _));

  private static List<RoutePathCurrencyTotalDto> ComputeTotalsByCurrency(
    IReadOnlyList<RouteStopPayload> stops,
    RouteSheetPayload sheet)
  {
    var buckets = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    foreach (var p in stops)
    {
      if (!TryParseStopAmount(p, sheet, out var amt, out var mon))
        continue;
      var minor = PaymentCheckoutComputationBridge.MajorToMinor(amt, mon);
      if (minor <= 0)
        continue;
      buckets[mon] = buckets.GetValueOrDefault(mon) + minor;
    }

    return buckets
      .OrderBy(kv => kv.Key, StringComparer.Ordinal)
      .Select(kv => new RoutePathCurrencyTotalDto { CurrencyLower = kv.Key, AmountMinor = kv.Value })
      .ToList();
  }

  private static string BuildPathLabel(IReadOnlyList<RouteStopPayload> stops)
  {
    if (stops.Count == 0)
      return "";
    if (stops.Count == 1)
    {
      var p = stops[0];
      return $"{(p.Origen ?? "").Trim()} → {(p.Destino ?? "").Trim()}".Trim();
    }

    var origen = (stops[0].Origen ?? "").Trim();
    var destino = (stops[^1].Destino ?? "").Trim();
    return $"{origen} → {destino}".Trim();
  }

  internal static bool TryParseStopAmount(
    RouteStopPayload p,
    RouteSheetPayload sheet,
    out decimal amountMajor,
    out string currencyLower)
  {
    amountMajor = 0;
    currencyLower = "";
    try
    {
      amountMajor = ParseDecimal(p.PrecioTransportista ?? "");
    }
    catch
    {
      return false;
    }

    var mon = PaymentCheckoutComputationBridge.NormalizeCurrencyFirst(p.MonedaPago)
              ?? PaymentCheckoutComputationBridge.NormalizeCurrencyFirst(sheet.MonedaPago);
    if (string.IsNullOrEmpty(mon))
      return false;
    currencyLower = mon;
    return amountMajor > 0;
  }

  private static decimal ParseDecimal(string? raw)
  {
    var t = (raw ?? "").Trim().Replace(",", ".", StringComparison.Ordinal).Replace('\u00a0', ' ');
    return decimal.TryParse(t, CultureInfo.InvariantCulture, out var d)
      ? d
      : throw new ArgumentException("invalid_decimal");
  }

  /// <summary>Acepta id de cabeza de ruta o id de parada dentro de una ruta enlazada.</summary>
  internal static bool TryResolveRoutePathSelection(
    RouteSheetPayload sheet,
    IReadOnlyDictionary<string, RoutePathDto> pathByHead,
    string? rawSelectionId,
    out RoutePathDto? path)
  {
    path = null;
    var pid = (rawSelectionId ?? "").Trim();
    if (pid.Length == 0)
      return false;

    if (pathByHead.TryGetValue(pid, out path))
      return true;

    var fromStop = TryGetRoutePathIdForStop(sheet, pid);
    if (fromStop is not null && pathByHead.TryGetValue(fromStop, out path))
      return true;

    return false;
  }
}

/// <summary>Bridge to payment normalization without circular project reference issues.</summary>
internal static class PaymentCheckoutComputationBridge
{
  internal static string? NormalizeCurrencyFirst(string? raw) =>
    VibeTrade.Backend.Features.Payments.PaymentCheckoutComputation.NormalizeCurrencyFirst(raw);

  internal static long MajorToMinor(decimal amountMajor, string currencyLower) =>
    VibeTrade.Backend.Features.Payments.PaymentCheckoutComputation.MajorToMinor(amountMajor, currencyLower);
}
