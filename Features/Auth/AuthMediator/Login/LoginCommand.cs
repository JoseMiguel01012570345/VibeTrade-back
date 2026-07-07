using MediatR;
using VibeTrade.Backend.Features.Auth.Dtos;

namespace VibeTrade.Backend.Features.Auth.AuthMediator.Login;

public sealed record LoginCommand(string Email, string Password) : IRequest<LoginResult?>;
