using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Auth;

namespace VibeTrade.Backend.Api;

/// <summary>Subida y descarga de archivos binarios (imágenes, PDFs, docs). GET es público; POST requiere sesión.</summary>
[ApiController]
[Route("api/v1/[controller]")]
public sealed class MediaController(AppDbContext db, IAuthService auth) : ControllerBase
{
    public sealed record MediaUploadResponse(string Id, string MimeType, string FileName, long SizeBytes);
    private const long MaxBytes = 5L * 1024 * 1024;

    [HttpPost]
    [RequestSizeLimit(524_288_000L)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(MediaUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MediaUploadResponse>> Upload([FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (!auth.TryGetUserByToken(Request.Headers.Authorization, out _))
            return Unauthorized();

        if (file.Length <= 0)
            return BadRequest("Empty file.");

        if (file.Length > MaxBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, "File too large. Max 5MB.");

        await using var ms = new MemoryStream(capacity: (int)Math.Min(file.Length, int.MaxValue));
        await file.CopyToAsync(ms, cancellationToken);
        var bytes = ms.ToArray();

        var id = "med_" + Guid.NewGuid().ToString("N")[..16];
        var row = new StoredMediaRow
        {
            Id = id,
            MimeType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            FileName = string.IsNullOrWhiteSpace(file.FileName) ? "file" : file.FileName,
            SizeBytes = bytes.LongLength,
            Bytes = bytes,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.StoredMedia.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new MediaUploadResponse(row.Id, row.MimeType, row.FileName, row.SizeBytes));
    }

    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        var row = await db.StoredMedia.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null)
            return NotFound();

        // Inline by default so images render; browser can still download via "Save as".
        Response.Headers.ContentDisposition = $"inline; filename=\"{row.FileName.Replace("\"", "")}\"";
        return File(row.Bytes, row.MimeType);
    }
}

