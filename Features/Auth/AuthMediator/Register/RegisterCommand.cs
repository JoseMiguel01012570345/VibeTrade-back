using MediatR;
using VibeTrade.Backend.Features.Auth.Dtos;

namespace VibeTrade.Backend.Features.Auth.AuthMediator.Register;

public sealed record RegisterCommand(
    string Password,
    string Email,
    string Username,
    string Phone) : IRequest<RegisterStartResult?>;
