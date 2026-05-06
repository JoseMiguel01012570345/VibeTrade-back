using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Chat;
using VibeTrade.Backend.Features.Chat.Interfaces;
using VibeTrade.Backend.Features.Logistics.Dtos;

namespace VibeTrade.Backend.Features.Logistics;

/// <summary>
/// Marca tramos elegibles para reembolso cuando vence el plazo de evidencia.
/// </summary>
public sealed class CarrierEvidenceDeadlineWatcher(
    IServiceScopeFactory scopeFactory,
    ILogger<CarrierEvidenceDeadlineWatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var chat = scope.ServiceProvider.GetRequiredService<IChatService>();

                var now = DateTimeOffset.UtcNow;
                var due = await db.RouteStopDeliveries
                        .Where(x =>
                            x.EvidenceDeadlineAtUtc != null
                            && x.EvidenceDeadlineAtUtc < now
                            && x.RefundedAtUtc == null
                            && x.RefundEligibleReason == null
                            && x.State == RouteStopDeliveryStates.DeliveredPendingEvidence
                            && !db.CarrierDeliveryEvidences.Any(e =>
                                e.ThreadId == x.ThreadId
                                && e.TradeAgreementId == x.TradeAgreementId
                                && e.RouteSheetId == x.RouteSheetId
                                && e.RouteStopId == x.RouteStopId
                                && (e.Status == ServiceEvidenceStatuses.Submitted
                                    || e.Status == ServiceEvidenceStatuses.Accepted)))
                        .ToListAsync(stoppingToken)
                        .ConfigureAwait(false)
                    ;

                foreach (var d in due)
                {
                    d.RefundEligibleReason = RouteStopRefundEligibleReasons.EvidenceExpired;
                    d.RefundEligibleSinceUtc = now;
                    d.UpdatedAtUtc = now;

                    var threadRow = await db.ChatThreads.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == d.ThreadId, stoppingToken)
                        .ConfigureAwait(false);
                    var buyer = (threadRow?.BuyerUserId ?? "").Trim();
                    var seller = (threadRow?.SellerUserId ?? "").Trim();
                    var preview =
                        "Venció el plazo de evidencia de entrega: el comprador/tienda puede solicitar reembolso del tramo.";
                    foreach (var rid in new[] { buyer, seller }.Where(x => x.Length >= 2).Distinct(StringComparer.Ordinal))
                    {
                        await chat.NotifyRouteLegProximityAsync(
                                new RouteLegProximityNotificationArgs(
                                    rid,
                                    d.ThreadId,
                                    d.RouteSheetId,
                                    d.TradeAgreementId,
                                    d.RouteStopId,
                                    preview),
                                stoppingToken)
                            .ConfigureAwait(false);
                    }
                }

                if (due.Count > 0)
                    await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "CarrierEvidenceDeadlineWatcher tick failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
        }
    }
}
