namespace VibeTrade.Backend.Features.Bootstrap.Interfaces;

public interface IBootstrapService
{
    Task<BootstrapResponseDto> GetBootstrapAsync(string viewerPhoneDigits, CancellationToken cancellationToken = default);
}
