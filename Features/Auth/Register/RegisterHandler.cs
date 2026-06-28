using MediatR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Data.Entities;
using VibeTrade.Backend.Features.Auth.Dtos;
using VibeTrade.Backend.Features.Auth.Shared;

namespace VibeTrade.Backend.Features.Auth.Register;

public sealed class RegisterHandler(
    AppDbContext db,
    IHostEnvironment hostEnvironment,
    IConfiguration configuration) : IRequestHandler<RegisterCommand, RegisterStartResult?>
{
    public async Task<RegisterStartResult?> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            return null;

        var normalizedEmail = AuthUtils.NormalizeEmail(request.Email);
        if (normalizedEmail.Length < 5 || !normalizedEmail.Contains('@'))
            return null;

        var normalizedUsername = AuthUtils.NormalizeUsername(request.Username);
        if (normalizedUsername is null || !AuthUtils.IsValidUsername(normalizedUsername))
            return null;

        var digits = AuthUtils.DigitsOnly(request.Phone);
        if (digits.Length < 7)
            return null;

        if (await db.UserAccounts.AnyAsync(u => u.Email != null && u.Email.ToLower() == normalizedEmail, cancellationToken))
            return null;

        if (await db.UserAccounts.AsNoTracking()
                .AnyAsync(
                    u => u.Username != null && u.Username.ToLower() == normalizedUsername.ToLower(),
                    cancellationToken))
            return null;

        if (await db.UserAccounts.AnyAsync(u => u.PhoneDigits == digits, cancellationToken))
            return null;

        var registrationId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var code = AuthPersistenceHelper.GenerateCode();

        db.AuthPendingRegistrations.Add(new AuthPendingRegistrationRow
        {
            RegistrationId = registrationId,
            PasswordHash = AuthUtils.HashPassword(request.Password),
            Email = normalizedEmail,
            Username = normalizedUsername,
            PhoneDigits = digits,
            PhoneDisplay = request.Phone.Trim(),
            PhoneVerified = false,
            EmailVerified = false,
            ExpiresAt = now.Add(AuthPersistenceHelper.CredentialsPendingTtl),
            CreatedAt = now,
        });

        await AuthPersistenceHelper.UpsertPhoneOtpAsync(db, digits, code, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        AuthPersistenceHelper.PruneExpiredCredentialsPending(db);

        Console.WriteLine("\u001b[31mRegister phone OTP: " + digits + " " + code + "\u001b[0m");
        var devCode = AuthPersistenceHelper.DevCodeMaybe(configuration, hostEnvironment, code);
        return new RegisterStartResult(
            registrationId,
            AuthPersistenceHelper.RegistrationCodeLength,
            (int)AuthPersistenceHelper.CredentialsPendingTtl.TotalSeconds,
            devCode);
    }
}
