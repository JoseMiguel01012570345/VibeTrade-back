using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Media.Dtos;

namespace VibeTrade.Backend.Features.Media;

public static class MediaModule
{
    private const long MaxBytes = 5L * 1024 * 1024;

    public static WebApplication MapMediaEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/media").WithTags("Media");

        group.MapPost("/", UploadAsync).DisableAntiforgery();
        group.MapGet("/{id}", GetAsync);

        return app;
    }

    private static async Task<IResult> UploadAsync(
        IFormFile file,
        HttpRequest request,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        CancellationToken cancellationToken)
    {
        if (!currentUser.TryGetUser(request, out _))
            return Results.Unauthorized();

        if (file.Length <= 0)
            return Results.BadRequest("Empty file.");

        if (file.Length > MaxBytes)
            return Results.Text("File too large. Max 5MB.", statusCode: StatusCodes.Status413PayloadTooLarge);

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

        return Results.Ok(new MediaUploadResponse(row.Id, row.MimeType, row.FileName, row.SizeBytes));
    }

    private static async Task<IResult> GetAsync(
        string id,
        HttpContext httpContext,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var row = await db.StoredMedia.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null)
            return Results.NotFound();

        var contentDisposition = new ContentDispositionHeaderValue("inline");
        contentDisposition.SetHttpFileName(
            string.IsNullOrWhiteSpace(row.FileName) ? "file" : row.FileName);
        httpContext.Response.GetTypedHeaders().ContentDisposition = contentDisposition;
        return Results.File(row.Bytes, row.MimeType);
    }
}
