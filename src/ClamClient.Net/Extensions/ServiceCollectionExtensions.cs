using ClamClient.Net.Abstractions;
using ClamClient.Net.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClamClient.Net.Extensions;

/// <summary>
/// Extension methods for registering ClamClient.Net with Microsoft DI.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IClamClient"/> as a transient service.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional delegate to configure <see cref="ClamClientOptions"/>.</param>
    public static IServiceCollection AddClamClient(
        this IServiceCollection services,
        Action<ClamClientOptions>? configure = null)
    {
        var options = new ClamClientOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddTransient<IClamClient, ClamAVClient>();
        return services;
    }
}