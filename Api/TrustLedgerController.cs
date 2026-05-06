using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Trust;
using VibeTrade.Backend.Features.Trust.Dtos;
using VibeTrade.Backend.Features.Trust.Interfaces;
using VibeTrade.Backend.Infrastructure;

namespace VibeTrade.Backend.Api;

/// <summary>Historial y ajustes demo de la barra de confianza (usuario y tienda).</summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[Tags("Trust")]
public sealed class TrustLedgerController(
    ICurrentUserAccessor currentUser,
    AppDbContext db,
    ITrustScoreLedgerService ledger) : ControllerBase
{
    private const int MinTrust = -10_000;
    /// <summary>Límite demo por solicitud (penalizaciones pueden sumar varios integrantes × base).</summary>
    private const int MaxAbsDeltaPerRequest = 10_000;

    private static int ApplyDelta(int current, int delta) => Math.Max(MinTrust, current + delta);

    /// <summary>Movimientos de confianza del usuario autenticado.</summary>
    [HttpGet("me/trust-history")]
    [ProducesResponseType(typeof(IReadOnlyList<TrustHistoryItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyHistory(
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();
        var list = await ledger.ListForSubjectAsync(
            TrustLedgerSubjects.User,
            userId,
            limit,
            cancellationToken);
        return Ok(list);
    }

    /// <summary>Aplica un delta a la confianza del usuario autenticado y registra el movimiento.</summary>
    [HttpPost("me/trust-adjust")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(TrustAdjustResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PostMyAdjust(
        [FromBody] TrustAdjustRequest body,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();
        if (body.Delta == 0)
            return BadRequest(new { error = "invalid_delta", message = "Delta no puede ser 0." });
        if (Math.Abs(body.Delta) > MaxAbsDeltaPerRequest)
            return BadRequest(new { error = "delta_too_large", message = "Delta fuera del rango permitido (demo)." });

        var acc = await db.UserAccounts.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (acc is null)
            return NotFound(new { error = "user_not_found", message = "No se encontró la cuenta." });

        var prev = acc.TrustScore;
        var next = ApplyDelta(prev, body.Delta);
        var appliedDelta = next - prev;
        if (appliedDelta == 0)
            return BadRequest(new { error = "no_change", message = "El ajuste no modifica el puntaje (límite alcanzado)." });

        acc.TrustScore = next;
        var reason = string.IsNullOrWhiteSpace(body.Reason) ? "Ajuste de confianza (demo)" : body.Reason.Trim();
        ledger.StageEntry(TrustLedgerSubjects.User, userId, appliedDelta, next, reason);
        await db.SaveChangesAsync(cancellationToken);

        var entryRow = await db.TrustScoreLedgerRows.AsNoTracking()
            .Where(x => x.SubjectType == TrustLedgerSubjects.User && x.SubjectId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstAsync(cancellationToken);
        var entry = new TrustHistoryItemDto(
            entryRow.Id,
            entryRow.CreatedAtUtc,
            entryRow.Delta,
            entryRow.BalanceAfter,
            entryRow.Reason);
        return Ok(new TrustAdjustResponse(next, entry));
    }

    /// <summary>Movimientos de confianza de una tienda (lectura pública).</summary>
    [HttpGet("stores/{storeId}/trust-history")]
    [ProducesResponseType(typeof(IReadOnlyList<TrustHistoryItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStoreHistory(
        string storeId,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var sid = (storeId ?? "").Trim();
        if (sid.Length < 2)
            return NotFound();
        var exists = await db.Stores.AsNoTracking().AnyAsync(x => x.Id == sid, cancellationToken);
        if (!exists)
            return NotFound(new { error = "store_not_found", message = "No se encontró la tienda." });
        var list = await ledger.ListForSubjectAsync(TrustLedgerSubjects.Store, sid, limit, cancellationToken);
        return Ok(list);
    }

    /// <summary>Aplica un delta a la confianza de la tienda (solo el dueño autenticado).</summary>
    [HttpPost("stores/{storeId}/trust-adjust")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(TrustAdjustResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostStoreAdjust(
        string storeId,
        [FromBody] TrustAdjustRequest body,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.GetUserId(Request);
        if (userId is null)
            return Unauthorized();
        if (body.Delta == 0)
            return BadRequest(new { error = "invalid_delta", message = "Delta no puede ser 0." });
        if (Math.Abs(body.Delta) > MaxAbsDeltaPerRequest)
            return BadRequest(new { error = "delta_too_large", message = "Delta fuera del rango permitido (demo)." });

        var sid = (storeId ?? "").Trim();
        if (sid.Length < 2)
            return NotFound();
        var store = await db.Stores.FirstOrDefaultAsync(x => x.Id == sid, cancellationToken);
        if (store is null)
            return NotFound(new { error = "store_not_found", message = "No se encontró la tienda." });
        if (!string.Equals((store.OwnerUserId ?? "").Trim(), userId, StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "forbidden", message = "Solo el dueño puede ajustar la confianza de la tienda." });

        var prev = store.TrustScore;
        var next = ApplyDelta(prev, body.Delta);
        var appliedDelta = next - prev;
        if (appliedDelta == 0)
            return BadRequest(new { error = "no_change", message = "El ajuste no modifica el puntaje (límite alcanzado)." });

        store.TrustScore = next;
        var reason = string.IsNullOrWhiteSpace(body.Reason) ? "Ajuste a la tienda (demo)" : body.Reason.Trim();
        ledger.StageEntry(TrustLedgerSubjects.Store, sid, appliedDelta, next, reason);
        await db.SaveChangesAsync(cancellationToken);

        var entryRow = await db.TrustScoreLedgerRows.AsNoTracking()
            .Where(x => x.SubjectType == TrustLedgerSubjects.Store && x.SubjectId == sid)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstAsync(cancellationToken);
        var entry = new TrustHistoryItemDto(
            entryRow.Id,
            entryRow.CreatedAtUtc,
            entryRow.Delta,
            entryRow.BalanceAfter,
            entryRow.Reason);
        return Ok(new TrustAdjustResponse(next, entry));
    }
}
