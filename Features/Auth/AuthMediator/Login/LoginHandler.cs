using MediatR;
using Microsoft.EntityFrameworkCore;
using VibeTrade.Backend.Data;
using VibeTrade.Backend.Features.Auth.Dtos;
using VibeTrade.Backend.Features.Auth.Shared;

namespace VibeTrade.Backend.Features.Auth.AuthMediator.Login;

public sealed class LoginHandler(AppDbContext db) : IRequestHandler<LoginCommand, LoginResult?>
{
    public async Task<LoginResult?> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = AuthUtils.NormalizeEmail(request.Email);
        if (normalizedEmail.Length < 5 || string.IsNullOrWhiteSpace(request.Password))
            return null;

        var row = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.Email != null && u.Email.ToLower() == normalizedEmail,
                cancellationToken);
        if (row is null || string.IsNullOrEmpty(row.PasswordHash))
            return null;

        if (!AuthUtils.VerifyPassword(request.Password, row.PasswordHash))
            return null;

        var snapshot = AuthPersistenceHelper.ToSnapshot(row);
        var sessionUser = AuthUtils.CreateSessionUserFromSnapshot(
            snapshot,
            AuthUtils.FormatPhoneForDisplay(row.PhoneDisplay, row.PhoneDigits));
        var token = await AuthPersistenceHelper.CreateSessionAsync(db, sessionUser, cancellationToken);
        return new LoginResult(token, sessionUser);
    }
}
