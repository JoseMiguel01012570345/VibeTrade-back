using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VibeTrade.Backend.Features.Auth;
using VibeTrade.Backend.Features.Logistics;
using VibeTrade.Backend.Utils;

namespace VibeTrade.Backend.Api;

[ApiController]
[Produces("application/json")]
[Tags("Chat logistics")]
public sealed class RouteLogisticsController(
    IAuthService auth,
    ICarrierTelemetryService telemetry,
    ICarrierOwnershipService ownership,
    ICarrierDeliveryEvidenceService carrierEvidence,
    ICarrierLegRefundService carrierLegRefund) : ControllerBase
{
    public sealed record PostTelemetryBody(
        string RouteSheetId,
        string RouteStopId,
        double Lat,
        double Lng,
        double? SpeedKmh,
        DateTimeOffset ReportedAtUtc,
        string SourceClientId);

    /// <summary>Transportista con ownership: push GPS para tracking en vivo.</summary>
    [HttpPost("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/logistics/telemetry")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CarrierTelemetryIngestResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostTelemetry(
        string threadId,
        string agreementId,
        [FromBody] PostTelemetryBody body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();

        var r = await telemetry.IngestAsync(
                userId.Trim(),
                threadId.Trim(),
                agreementId.Trim(),
                body.RouteSheetId.Trim(),
                body.RouteStopId.Trim(),
                body.Lat,
                body.Lng,
                body.SpeedKmh,
                body.ReportedAtUtc,
                body.SourceClientId.Trim(),
                cancellationToken)
            .ConfigureAwait(false);
        if (r is null)
            return NotFound();

        if (!r.Accepted)
            return BadRequest(r);

        return Ok(r);
    }

    public sealed record CedeOwnershipBody(string RouteSheetId, string RouteStopId, string TargetCarrierUserId);

    [HttpPost("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/logistics/ownership/cede")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CarrierOwnershipCedeResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostCedeOwnership(
        string threadId,
        string agreementId,
        [FromBody] CedeOwnershipBody body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();

        var r = await ownership.CedeOwnershipAsync(
                userId.Trim(),
                threadId.Trim(),
                agreementId.Trim(),
                body.RouteSheetId.Trim(),
                body.RouteStopId.Trim(),
                body.TargetCarrierUserId.Trim(),
                cancellationToken)
            .ConfigureAwait(false);
        if (r is null)
            return NotFound();

        if (!r.Ok)
            return BadRequest(r);

        return Ok(r);
    }

    [HttpGet("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/logistics/deliveries")]
    [ProducesResponseType(typeof(IReadOnlyList<RouteStopDeliveryStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListDeliveries(
        string threadId,
        string agreementId,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();

        var rows = await telemetry.ListDeliveriesAsync(userId.Trim(), threadId.Trim(), agreementId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (rows is null)
            return NotFound();

        return Ok(rows);
    }

    public sealed record UpsertEvidenceBody(string Text, List<VibeTrade.Backend.Data.Entities.ServiceEvidenceAttachmentBody>? Attachments, bool Submit);

    [HttpGet("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/logistics/evidence")]
    [ProducesResponseType(typeof(CarrierDeliveryEvidenceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEvidence(
        string threadId,
        string agreementId,
        [FromQuery] string routeSheetId,
        [FromQuery] string routeStopId,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();

        var (status, err, data) = await carrierEvidence.GetAsync(
                userId.Trim(),
                threadId.Trim(),
                agreementId.Trim(),
                routeSheetId.Trim(),
                routeStopId.Trim(),
                cancellationToken)
            .ConfigureAwait(false);

        if (status == StatusCodes.Status404NotFound)
            return NotFound();
        if (status != StatusCodes.Status200OK || data is null)
            return BadRequest(new { message = err });

        return Ok(data);
    }

    [HttpPut("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/logistics/evidence")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CarrierDeliveryEvidenceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpsertEvidence(
        string threadId,
        string agreementId,
        [FromQuery] string routeSheetId,
        [FromQuery] string routeStopId,
        [FromBody] UpsertEvidenceBody body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();

        var (status, err, data) = await carrierEvidence.UpsertAsync(
                userId.Trim(),
                threadId.Trim(),
                agreementId.Trim(),
                routeSheetId.Trim(),
                routeStopId.Trim(),
                new UpsertCarrierDeliveryEvidenceRequest(body.Text, body.Attachments, body.Submit),
                cancellationToken)
            .ConfigureAwait(false);

        if (status == StatusCodes.Status404NotFound)
            return NotFound();
        if (status != StatusCodes.Status200OK)
            return BadRequest(new { message = err });

        return Ok(data);
    }

    public sealed record DecideEvidenceBody(string Decision);

    [HttpPost("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/logistics/evidence/decide")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DecideEvidence(
        string threadId,
        string agreementId,
        [FromQuery] string routeSheetId,
        [FromQuery] string routeStopId,
        [FromBody] DecideEvidenceBody body,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();

        var (status, err) = await carrierEvidence.DecideAsync(
                userId.Trim(),
                threadId.Trim(),
                agreementId.Trim(),
                routeSheetId.Trim(),
                routeStopId.Trim(),
                new DecideCarrierDeliveryEvidenceRequest(body.Decision),
                cancellationToken)
            .ConfigureAwait(false);

        if (status == StatusCodes.Status404NotFound)
            return NotFound();
        if (status == StatusCodes.Status403Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = err });
        if (status != StatusCodes.Status200OK)
            return BadRequest(new { message = err });

        return Ok(new { ok = true });
    }

    [HttpPost("/api/v1/chat/threads/{threadId}/agreements/{agreementId}/logistics/refunds/leg")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefundLeg(
        string threadId,
        string agreementId,
        [FromQuery] string routeSheetId,
        [FromQuery] string routeStopId,
        CancellationToken cancellationToken)
    {
        var userId = BearerUserId.FromRequest(auth, Request);
        if (userId is null)
            return Unauthorized();

        var (ok, code) = await carrierLegRefund.TryRefundEligibleLegAsync(
                userId.Trim(),
                threadId.Trim(),
                agreementId.Trim(),
                routeSheetId.Trim(),
                routeStopId.Trim(),
                cancellationToken)
            .ConfigureAwait(false);

        if (!ok)
            return BadRequest(new { error = code ?? "refund_failed" });

        return Ok(new { ok = true });
    }
}
