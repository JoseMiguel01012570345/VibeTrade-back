using MediatR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Auth.Dtos;
using VibeTrade.Backend.Features.Auth.Shared;

namespace VibeTrade.Backend.Features.Auth.AuthMediator.VerifyOtp;

public sealed class VerifyOtpHandler(AppDbContext db) : IRequestHandler<VerifyOtpCommand, VerifyResult?>
{
    public async Task<VerifyResult?> Handle(VerifyOtpCommand request, CancellationToken cancellationToken)
    {
        var digits = AuthUtils.DigitsOnly(request.Phone);
        var pending = await db.AuthPendingOtps
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PhoneDigits == digits, cancellationToken);

        if (pending is null)
            return null;

        var now = DateTimeOffset.UtcNow;
        if (now > pending.ExpiresAt)
        {
            await db.AuthPendingOtps.Where(p => p.PhoneDigits == digits)
                .ExecuteDeleteAsync(cancellationToken);
            return null;
        }

        var normalizedCode = AuthUtils.DigitsOnly(request.Code);
        if (normalizedCode != pending.Code)
            return null;

        await db.AuthPendingOtps.Where(p => p.PhoneDigits == digits)
            .ExecuteDeleteAsync(cancellationToken);

        var profile = await AuthPersistenceHelper.GetProfileSnapshotAsync(db, digits, cancellationToken);
        var sessionUser = AuthUtils.CreateSessionUserForVerifiedPhone(digits, profile);
        var token = await AuthPersistenceHelper.CreateSessionAsync(db, sessionUser, cancellationToken);
        AuthPersistenceHelper.PruneExpiredPendingOtps(db);

        return new VerifyResult(token, sessionUser);
    }
}
