namespace VibeTrade.Backend.Features.Auth;

/// <summary>País habilitado para registro / inicio de sesión por SMS (selector de prefijo).</summary>
public sealed record SignInCountryDto(string Name, string Code, string Dial, string Flag);

/// <summary>Lista autoritativa de países expuesta al cliente web.</summary>
public static class SignInCountryCatalog
{
    public static IReadOnlyList<SignInCountryDto> All { get; } =
    [
        new("Cuba", "CU", "+53", "🇨🇺"),
        new("Argentina", "AR", "+54", "🇦🇷"),
        new("Colombia", "CO", "+57", "🇨🇴"),
        new("España", "ES", "+34", "🇪🇸"),
        new("México", "MX", "+52", "🇲🇽"),
        new("Chile", "CL", "+56", "🇨🇱"),
        new("Perú", "PE", "+51", "🇵🇪"),
        new("Estados Unidos", "US", "+1", "🇺🇸"),
    ];
}
