using MediatR;
using VibeTrade.Backend.Features.Auth.Dtos;

namespace VibeTrade.Backend.Features.Auth.AuthMediator.SendOtp;

public sealed record SendOtpCommand(string Phone) : IRequest<RequestCodeResult>;
