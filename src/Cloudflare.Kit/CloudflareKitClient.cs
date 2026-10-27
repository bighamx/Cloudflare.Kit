using Cloudflare.Kit.Tunnels;
using Cloudflare.Kit.Workers;
using Cloudflare.NET;
using Cloudflare.NET.Accounts;
using Cloudflare.NET.Accounts.D1;
using Cloudflare.NET.Accounts.Kv;
using Cloudflare.NET.Dns;
using Cloudflare.NET.Core;
using Cloudflare.NET.R2;
using Cloudflare.NET.R2.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace Cloudflare.Kit;

/// <summary>A single entry point retaining access to the underlying SDK's complete APIs.</summary>
public sealed class CloudflareKitClient : IDisposable
{
    private readonly ICloudflareApiClient api;
    private readonly IR2Client? r2;
    private readonly TunnelClient tunnels;
    private readonly WorkerScriptsClient workerScripts;
    private readonly ServiceProvider? ownedServices;
    private readonly ConcurrentBag<HttpClient>? ownedHttpClients;
    private bool disposed;

    public ICloudflareApiClient Api
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return api;
        }
    }
    public IDnsApi Dns => Api.Dns;
    public IKvApi Kv => Api.Accounts.Kv;
    public ID1Api D1 => Api.Accounts.D1;
    public IAccountsApi Accounts => Api.Accounts;
    public IR2Client R2
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return r2 ?? throw new InvalidOperationException("Provide R2 credentials through the constructor or AddCloudflareKit to use object storage.");
        }
    }
    public TunnelClient Tunnels
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return tunnels;
        }
    }
    public WorkerScriptsClient WorkerScripts
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return workerScripts;
        }
    }

    [ActivatorUtilitiesConstructor]
    public CloudflareKitClient(ICloudflareApiClient api, TunnelClient tunnels, WorkerScriptsClient workerScripts,
        IR2Client? r2 = null)
    {
        this.api = api;
        this.tunnels = tunnels;
        this.workerScripts = workerScripts;
        this.r2 = r2;
    }

    /// <summary>Creates an independently owned client; reuse it and dispose it when finished.</summary>
    public CloudflareKitClient(CloudflareApiOptions options, R2Settings? r2Settings = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var services = new ServiceCollection();
        var clients = new ConcurrentBag<HttpClient>();
        services.ConfigureHttpClientDefaults(builder => builder.ConfigureHttpClient(clients.Add));
        Action<R2Settings>? configureR2 = null;
        if (r2Settings is not null)
        {
            configureR2 = settings =>
            {
                settings.AccessKeyId = r2Settings.AccessKeyId;
                settings.SecretAccessKey = r2Settings.SecretAccessKey;
                settings.EndpointUrl = r2Settings.EndpointUrl;
                settings.Region = r2Settings.Region;
                settings.Jurisdiction = r2Settings.Jurisdiction;
            };
        }
        services.AddCloudflareKit(settings =>
        {
            settings.ApiToken = options.ApiToken;
            settings.AccountId = options.AccountId;
            settings.ApiBaseUrl = options.ApiBaseUrl;
            settings.DefaultTimeout = options.DefaultTimeout;
            settings.GraphQlApiUrl = options.GraphQlApiUrl;
            settings.RateLimiting = options.RateLimiting;
        }, configureR2);

        var provider = services.BuildServiceProvider();
        try
        {
            api = provider.GetRequiredService<ICloudflareApiClient>();
            tunnels = provider.GetRequiredService<TunnelClient>();
            workerScripts = provider.GetRequiredService<WorkerScriptsClient>();
            r2 = provider.GetService<IR2Client>();
            ownedServices = provider;
            ownedHttpClients = clients;
        }
        catch
        {
            provider.Dispose();
            foreach (var client in clients)
            {
                client.Dispose();
            }
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        // Injected dependencies belong to the caller/container. Only the new-style constructor owns its provider.
        ownedServices?.Dispose();
        if (ownedHttpClients is not null)
        {
            foreach (var client in ownedHttpClients)
            {
                client.Dispose();
            }
        }
    }
}
