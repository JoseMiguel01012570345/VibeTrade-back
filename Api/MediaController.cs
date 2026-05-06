using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Infrastructure;

namespace VibeTrade.Backend.Api;

/// <summary>Almacenamiento de medios binarios (bytea en PostgreSQL); GET público por id.</summary>
[ApiController]
[Route("api/v1/[controller]")]
[Tags("Media")]
public sealed class MediaController(AppDbContext db, ICurrentUserAccessor currentUser) : ControllerBase
{
    public sealed record MediaUploadResponse(string Id, string MimeType, string FileName, long SizeBytes);
    private const long MaxBytes = 5L * 1024 * 1024;

    /// <summary>
    /// Sube un archivo multipart (máx. 5 MB); devuelve id para referenciar en perfil u ofertas.
    /// Sin <c>[FromForm]</c>: Swashbuckle falla al generar OpenAPI con <c>[FromForm] IFormFile</c>; el binding sigue usando el campo de formulario <c>file</c>.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(524_288_000L)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(MediaUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<MediaUploadResponse>> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        if (!currentUser.TryGetUser(Request, out _))
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

    /// <summary>Descarga el binario con <c>Content-Disposition: inline</c> (adecuado para imágenes en &lt;img&gt;).</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        var row = await db.StoredMedia.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null)
            return NotFound();

        // Inline by default so images render; kestrel only allows ASCII in the raw header string unless
        // the filename is encoded (RFC 5987 filename*). SetHttpFileName does filename + filename* safely.
        var contentDisposition = new ContentDispositionHeaderValue("inline");
        contentDisposition.SetHttpFileName(
            string.IsNullOrWhiteSpace(row.FileName) ? "file" : row.FileName);
        Response.GetTypedHeaders().ContentDisposition = contentDisposition;
        return File(row.Bytes, row.MimeType);
    }
}

