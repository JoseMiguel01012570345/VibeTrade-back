namespace VibeTrade.Backend.Features.Bootstrap.Interfaces;

public interface IGuestBootstrapService
{
    Task<BootstrapResponseDto> GetGuestBootstrapAsync(string guestId, CancellationToken cancellationToken = default);
}
