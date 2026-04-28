namespace VibeTrade.Backend.Features.Bootstrap;

public interface IBootstrapService
{
    Task<BootstrapResponseDto> GetBootstrapAsync(string viewerPhoneDigits, CancellationToken cancellationToken = default);
}
