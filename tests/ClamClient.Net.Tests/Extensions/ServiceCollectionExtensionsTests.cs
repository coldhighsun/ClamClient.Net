using ClamClient.Net.Abstractions;
using ClamClient.Net.Configuration;
using ClamClient.Net.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClamClient.Net.Tests.Extensions;

/// <summary>
/// Unit tests for <see cref="ServiceCollectionExtensions.AddClamClient"/>.
/// Verifies DI registration, singleton lifetime, options binding, and default values.
/// </summary>
public sealed class ServiceCollectionExtensionsTests
{
    /// <summary>
    /// Verifies that calling <see cref="ServiceCollectionExtensions.AddClamClient"/> twice
    /// registers two <see cref="IClamClient"/> descriptors (last-wins is the DI default).
    /// </summary>
    [Fact]
    public void AddClamClient_CalledTwice_RegistersTwoDescriptors()
    {
        var services = new ServiceCollection();
        services.AddClamClient();
        services.AddClamClient();

        var descriptors = services
            .Where(d => d.ServiceType == typeof(IClamClient))
            .ToList();

        Assert.Equal(2, descriptors.Count);
    }

    /// <summary>
    /// Verifies that <see cref="IClamClient"/> is registered as a singleton — two resolutions return the same instance.
    /// </summary>
    [Fact]
    public async Task AddClamClient_IClamClient_IsSingleton()
    {
        var services = new ServiceCollection();
        services.AddClamClient();

        await using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IClamClient>();
        var second = provider.GetRequiredService<IClamClient>();

        Assert.Same(first, second);
    }

    /// <summary>
    /// Verifies that <see cref="ServiceCollectionExtensions.AddClamClient"/> returns the same
    /// <see cref="IServiceCollection"/> instance for method chaining.
    /// </summary>
    [Fact]
    public void AddClamClient_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();
        var returned = services.AddClamClient();

        Assert.Same(services, returned);
    }

    /// <summary>
    /// Verifies that the configure delegate is applied to <see cref="ClamClientOptions"/>.
    /// </summary>
    [Fact]
    public void AddClamClient_WithConfigure_AppliesOptions()
    {
        var services = new ServiceCollection();
        services.AddClamClient(o =>
        {
            o.MaxConnections = 5;
            o.Timeout = TimeSpan.FromSeconds(42);
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ClamClientOptions>>().Value;

        Assert.Equal(5, options.MaxConnections);
        Assert.Equal(TimeSpan.FromSeconds(42), options.Timeout);
    }

    /// <summary>
    /// Verifies that <see cref="IClamClient"/> is registered and can be resolved.
    /// </summary>
    [Fact]
    public async Task AddClamClient_WithoutConfigure_RegistersIClamClient()
    {
        var services = new ServiceCollection();
        services.AddClamClient();

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetService<IClamClient>();

        Assert.NotNull(client);
    }

    /// <summary>
    /// Verifies that default <see cref="ClamClientOptions"/> values are used when no configure delegate is supplied.
    /// </summary>
    [Fact]
    public void AddClamClient_WithoutConfigure_UsesDefaultOptions()
    {
        var services = new ServiceCollection();
        services.AddClamClient();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ClamClientOptions>>().Value;

        Assert.Equal(10, options.MaxConnections);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(30), options.IdleConnectionTimeout);
        Assert.Equal(128 * 1024, options.ChunkSize);
        Assert.Equal(25L * 1024 * 1024, options.MaxStreamSize);
    }
}