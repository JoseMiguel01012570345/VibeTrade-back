using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Market.Interfaces;

namespace VibeTrade.Backend.Features.Market;

/// <summary>
/// Tablero de comentarios públicos de una tienda. Refleja el patrón del Q&amp;A por-oferta
/// (<c>CatalogService</c> + <c>OfferService</c>) pero a nivel de tienda: el array vive en la
/// columna jsonb <c>CommentsJson</c> de <see cref="StoreRow"/> y los likes en
/// <c>store_qa_comment_likes</c>.
/// </summary>
public sealed class StoreCommentsService(AppDbContext db) : IStoreCommentsService
{
    /// <summary>Cota superior de tiempo válido (año ~2100) para descartar epochs corruptos del cliente.</summary>
    private const long MaxPlausibleUnixMs = 4_102_441_920_000L;

    public async Task<IReadOnlyList<OfferQaItemResponseDto>?> GetStoreCommentsAsync(
        string storeId,
        string? likerKey,
        CancellationToken cancellationToken = default)
    {
        var sid = (storeId ?? "").Trim();
        if (sid.Length < 2)
            return null;

        var store = await db.Stores.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sid, cancellationToken);
        if (store is null)
            return null;

        var comments = store.Comments ?? new List<OfferQaComment>();
        var list = new List<OfferQaItemResponseDto>(comments.Count);
        foreach (var c in comments)
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

        var likeCounts = await db.StoreCommentLikes.AsNoTracking()
            .Where(x => x.StoreId == sid && commentIds.Contains(x.CommentId))
            .GroupBy(x => x.CommentId)
            .Select(g => new { CommentId = g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.CommentId, x => x.C, StringComparer.Ordinal, cancellationToken);

        HashSet<string>? viewerLikedIds = null;
        if (!string.IsNullOrEmpty(likerKey))
        {
            viewerLikedIds = (await db.StoreCommentLikes.AsNoTracking()
                .Where(x => x.StoreId == sid && commentIds.Contains(x.CommentId) && x.LikerKey == likerKey)
                .Select(x => x.CommentId)
                .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        }

        foreach (var o in list)
        {
            o.LikeCount = likeCounts.GetValueOrDefault(o.Id, 0);
            o.ViewerLiked = viewerLikedIds is not null && viewerLikedIds.Contains(o.Id);
        }

        return list;
    }

    public async Task<OfferQaComment?> AppendStoreCommentAsync(
        string storeId,
        string text,
        string? parentId,
        string authorId,
        string authorName,
        int authorTrust,
        long? createdAtMs,
        CancellationToken cancellationToken = default)
    {
        var sid = (storeId ?? "").Trim();
        if (sid.Length < 2)
            throw new ArgumentException("storeId is required.", nameof(storeId));

        var store = await db.Stores.FirstOrDefaultAsync(x => x.Id == sid, cancellationToken);
        if (store is null)
            return null;

        var pid = string.IsNullOrWhiteSpace(parentId) ? null : parentId.Trim();
        var list = store.Comments.ToList();
        if (pid is not null && !list.Any(x => string.Equals(x.Id, pid, StringComparison.Ordinal)))
            throw new ArgumentException("parentId no corresponde a un comentario de esta tienda.", nameof(parentId));

        var now = DateTimeOffset.UtcNow;
        var createdMs = createdAtMs is long ms && ms > 0 && ms < MaxPlausibleUnixMs
            ? ms
            : now.ToUnixTimeMilliseconds();

        var author = new OfferQaAuthorSnapshot
        {
            Id = authorId,
            Name = authorName,
            TrustScore = authorTrust,
        };

        var newItem = new OfferQaComment
        {
            Id = $"sqa_{Guid.NewGuid():N}",
            Text = text,
            Question = text,
            ParentId = pid,
            AskedBy = author,
            Author = author,
            CreatedAt = createdMs,
        };

        list.Insert(0, newItem);
        store.Comments = list;
        store.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return newItem;
    }

    public async Task<(bool Liked, int LikeCount)> ToggleStoreCommentLikeAsync(
        string storeId,
        string commentId,
        string likerKey,
        CancellationToken cancellationToken = default)
    {
        var sid = (storeId ?? "").Trim();
        var cid = (commentId ?? "").Trim();
        if (sid.Length < 2 || cid.Length < 2 || string.IsNullOrEmpty(likerKey))
            return (false, 0);

        var storeExists = await db.Stores.AsNoTracking().AnyAsync(x => x.Id == sid, cancellationToken);
        if (!storeExists)
            return (false, 0);

        var existing = await db.StoreCommentLikes
            .FirstOrDefaultAsync(
                x => x.StoreId == sid && x.CommentId == cid && x.LikerKey == likerKey,
                cancellationToken);

        if (existing is not null)
        {
            db.StoreCommentLikes.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            var c = await db.StoreCommentLikes.CountAsync(
                x => x.StoreId == sid && x.CommentId == cid,
                cancellationToken);
            return (false, c);
        }

        db.StoreCommentLikes.Add(new StoreCommentLikeRow
        {
            Id = $"sqk_{Guid.NewGuid():N}",
            StoreId = sid,
            CommentId = cid,
            LikerKey = likerKey,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
        var c2 = await db.StoreCommentLikes.CountAsync(
            x => x.StoreId == sid && x.CommentId == cid,
            cancellationToken);
        return (true, c2);
    }
}
