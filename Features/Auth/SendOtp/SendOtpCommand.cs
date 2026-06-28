using MediatR;
using VibeTrade.Backend.Features.Auth.Dtos;

namespace VibeTrade.Backend.Features.Auth.SendOtp;

public sealed record SendOtpCommand(string Phone) : IRequest<RequestCodeResult>;
