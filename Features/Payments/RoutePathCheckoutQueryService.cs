using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Agreements;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Logistics;
using VibeTrade.Backend.Features.Payments.Interfaces;
using VibeTrade.Backend.Features.RouteSheets;
using VibeTrade.Backend.Features.RouteSheets.Dtos;

namespace VibeTrade.Backend.Features.Payments;

public sealed class RoutePathCheckoutQueryService(AppDbContext db, IChatService chat) : IRoutePathCheckoutQueryService
{
  public async Task<AgreementRoutePathsDto?> GetAgreementRoutePathsAsync(
    string userId,
    string threadId,
    string agreementId,
    string routeSheetId,
    CancellationToken cancellationToken = default)
  {
    var uid = userId.Trim();
    var tid = threadId.Trim();
    var aid = agreementId.Trim();
    var rsid = routeSheetId.Trim();
    if (uid.Length < 2 || tid.Length < 2 || aid.Length < 2 || rsid.Length < 2)
      return null;

    var t = await db.ChatThreads.AsNoTracking()
      .FirstOrDefaultAsync(x => x.Id == tid, cancellationToken)
      .ConfigureAwait(false);
    if (t is null)
      return null;
    if (!await chat.UserCanAccessThreadRowAsync(uid, t, cancellationToken).ConfigureAwait(false))
      return null;

    var ag = await AgreementCheckoutExecutor.LoadAgreementAsync(db, tid, aid, cancellationToken)
      .ConfigureAwait(false);
    if (ag is null)
      return null;
    if (!string.Equals(ag.Status, "accepted", StringComparison.OrdinalIgnoreCase))
      return null;
    if (!ag.IncludeMerchandise)
      return null;

    var linkedRs = (ag.RouteSheetId ?? "").Trim();
    if (linkedRs.Length == 0 || !string.Equals(linkedRs, rsid, StringComparison.Ordinal))
      return null;

    var rp = await AgreementCheckoutExecutor.LoadRoutePayloadAsync(db, tid, rsid, cancellationToken)
      .ConfigureAwait(false);
    if (rp?.Paradas is not { Count: > 0 })
      return new AgreementRoutePathsDto { RouteSheetId = rsid, Paths = [] };

    var paidStopIds = await LoadPaidRouteLegStopIdsAsync(aid, cancellationToken).ConfigureAwait(false);
    var paidLikeStopIds = await LoadPaidLikeDeliveryStopIdsAsync(tid, aid, rsid, cancellationToken)
      .ConfigureAwait(false);

    var paths = RoutePathComputation.BuildRoutePaths(rp, paidStopIds, paidLikeStopIds);
    return new AgreementRoutePathsDto { RouteSheetId = rsid, Paths = paths };
  }

  private async Task<HashSet<string>> LoadPaidRouteLegStopIdsAsync(
    string agreementId,
    CancellationToken cancellationToken)
  {
    var rows = await (
        from rl in db.AgreementRouteLegPaids.AsNoTracking()
        join cp in db.AgreementCurrencyPayments.AsNoTracking()
          on rl.AgreementCurrencyPaymentId equals cp.Id
        where cp.TradeAgreementId == agreementId.Trim()
              && cp.Status == AgreementPaymentStatuses.Succeeded
        select rl.RouteStopId)
      .ToListAsync(cancellationToken)
      .ConfigureAwait(false);

    var set = new HashSet<string>(StringComparer.Ordinal);
    foreach (var id in rows)
    {
      var s = (id ?? "").Trim();
      if (s.Length > 0)
        set.Add(s);
    }

    return set;
  }

  private async Task<HashSet<string>> LoadPaidLikeDeliveryStopIdsAsync(
    string threadId,
    string agreementId,
    string routeSheetId,
    CancellationToken cancellationToken)
  {
    var rows = await db.RouteStopDeliveries.AsNoTracking()
      .Where(x =>
        x.ThreadId == threadId.Trim()
        && x.TradeAgreementId == agreementId.Trim()
        && x.RouteSheetId == routeSheetId.Trim())
      .Select(x => new { x.RouteStopId, x.State })
      .ToListAsync(cancellationToken)
      .ConfigureAwait(false);

    var set = new HashSet<string>(StringComparer.Ordinal);
    foreach (var row in rows)
    {
      if (!LogisticsUtils.IsPaidLikeState(row.State))
        continue;
      var s = (row.RouteStopId ?? "").Trim();
      if (s.Length > 0)
        set.Add(s);
    }

    return set;
  }
}
