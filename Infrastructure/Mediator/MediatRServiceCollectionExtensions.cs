using System.Reflection;
using FluentValidation;
using MediatR;

namespace VibeTrade.Backend.Infrastructure.Mediator;

public static class MediatRServiceCollectionExtensions
{
    public static IServiceCollection AddVibeTradeMediatR(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        return services;
    }
}
