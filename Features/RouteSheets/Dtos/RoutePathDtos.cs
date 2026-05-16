namespace VibeTrade.Backend.Features.RouteSheets.Dtos;

/// <summary>Ruta enlazada (cadena de paradas con destino[i-1]=origen[i]) dentro de una hoja.</summary>
public sealed class RoutePathDto
{
  public string RoutePathId { get; set; } = "";

  public int Orden { get; set; }

  public string Label { get; set; } = "";

  public IReadOnlyList<string> StopIds { get; set; } = [];

  public IReadOnlyList<RoutePathStopSummaryDto> Stops { get; set; } = [];

  public IReadOnlyList<RoutePathCurrencyTotalDto> TotalsByCurrency { get; set; } = [];

  public bool Payable { get; set; }

  public bool Paid { get; set; }

  public bool PartiallyPaid { get; set; }
}

public sealed class RoutePathStopSummaryDto
{
  public string RouteStopId { get; set; } = "";

  public int Orden { get; set; }

  public string Origen { get; set; } = "";

  public string Destino { get; set; } = "";

  public string? PrecioTransportista { get; set; }

  public string? MonedaPago { get; set; }
}

public sealed class RoutePathCurrencyTotalDto
{
  public string CurrencyLower { get; set; } = "";

  public long AmountMinor { get; set; }
}

public sealed class AgreementRoutePathsDto
{
  public string RouteSheetId { get; set; } = "";

  public IReadOnlyList<RoutePathDto> Paths { get; set; } = [];
}

public sealed class RoutePathCheckoutResolveResult
{
  public RoutePathCheckoutResolveResult(HashSet<string> expandedStopIds, IReadOnlyList<string> errors)
  {
    ExpandedStopIds = expandedStopIds;
    Errors = errors;
  }

  public HashSet<string> ExpandedStopIds { get; }

  public IReadOnlyList<string> Errors { get; }
}
