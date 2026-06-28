using MediatR;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Auth.Dtos;
using VibeTrade.Backend.Features.Auth.Shared;

namespace VibeTrade.Backend.Features.Auth.SendOtp;

public sealed class SendOtpHandler(
    AppDbContext db,
    IHostEnvironment hostEnvironment,
    IConfiguration configuration) : IRequestHandler<SendOtpCommand, RequestCodeResult>
{
    public Task<RequestCodeResult> Handle(SendOtpCommand request, CancellationToken cancellationToken)
    {
        var digits = AuthUtils.DigitsOnly(request.Phone);
        var code = Random.Shared.Next(1_000_000, 9_999_999).ToString();
        Console.WriteLine("\u001b[31mRequestCode: " + digits + " " + code + "\u001b[0m");

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(AuthPersistenceHelper.OtpPendingTtl);

        var existing = db.AuthPendingOtps.Find(digits);
        if (existing is not null)
        {
            existing.Code = code;
            existing.CodeLength = code.Length;
            existing.ExpiresAt = expiresAt;
            existing.CreatedAt = now;
        }
        else
        {
            db.AuthPendingOtps.Add(
                new AuthPendingOtpRow
                {
                    PhoneDigits = digits,
                    Code = code,
                    CodeLength = code.Length,
                    ExpiresAt = expiresAt,
                    CreatedAt = now,
                });
        }

        db.SaveChanges();
        AuthPersistenceHelper.PruneExpiredPendingOtps(db);

        var devCode = AuthPersistenceHelper.DevCodeMaybe(configuration, hostEnvironment, code);
        return Task.FromResult(
            new RequestCodeResult(code.Length, (int)AuthPersistenceHelper.OtpPendingTtl.TotalSeconds, devCode));
    }
}
