using MediatR;
using VibeTrade.Backend.Features.Auth.Dtos;

namespace VibeTrade.Backend.Features.Auth.VerifyOtp;

public sealed record VerifyOtpCommand(string Phone, string Code) : IRequest<VerifyResult?>;
