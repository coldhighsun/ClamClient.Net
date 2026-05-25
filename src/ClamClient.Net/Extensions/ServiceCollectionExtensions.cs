using ClamClient.Net.Abstractions;
using ClamClient.Net.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClamClient.Net.Extensions;

/// <summary>
/// Extension methods for registering ClamClient.Net with Microsoft DI.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IClamClient"/> as a singleton backed by a connection pool.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional delegate to configure <see cref="ClamClientOptions"/>.</param>
    public static IServiceCollection AddClamClient(
        this IServiceCollection services,
        Action<ClamClientOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<ClamClientOptions>();
        }

        services.AddSingleton<IClamClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ClamClientOptions>>().Value;
            return new ClamAVClient(options);
        });
        return services;
    }
}