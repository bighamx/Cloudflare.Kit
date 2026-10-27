using Cloudflare.Kit.Tunnels;
using Cloudflare.Kit.Workers;
using Cloudflare.NET.Core;
using Cloudflare.NET.R2;
using Cloudflare.NET.R2.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cloudflare.Kit;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the upstream SDK and the Kit extensions; R2 object credentials are optional.</summary>
    public static IHttpClientBuilder AddCloudflareKit(this IServiceCollection services,
        Action<CloudflareApiOptions> configureApi, Action<R2Settings>? configureR2 = null)
    {
        ArgumentNullException.ThrowIfNull(configureApi);
        services.AddCloudflareApiClient(configureApi);
        if (configureR2 is not null)
        {
            services.AddCloudflareR2Client(configureR2);
        }
        services.AddTransient<TunnelClient>();
        services.AddTransient<WorkerScriptsClient>();
        services.AddTransient<CloudflareKitClient>();
        return services.AddHttpClient<CloudflareTransport>((provider, http) =>
        {
            var options = provider.GetRequiredService<IOptions<CloudflareApiOptions>>().Value;
            http.Timeout = options.DefaultTimeout;
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });
    }
}
