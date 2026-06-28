using FluentValidation;

namespace VibeTrade.Backend.Features.Auth.SendOtp;

public sealed class SendOtpCommandValidator : AbstractValidator<SendOtpCommand>
{
    public SendOtpCommandValidator()
    {
        RuleFor(x => x.Phone).NotEmpty();
    }
}
