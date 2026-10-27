using Cloudflare.Kit.Tunnels;
using Cloudflare.Kit.Workers;
using Cloudflare.NET;
using Cloudflare.NET.Accounts;
using Cloudflare.NET.Accounts.D1;
using Cloudflare.NET.Accounts.Kv;
using Cloudflare.NET.Dns;
using Cloudflare.NET.R2;

namespace Cloudflare.Kit;

/// <summary>A single entry point retaining access to the underlying SDK's complete APIs.</summary>
public sealed class CloudflareKitClient
{
    private readonly IR2Client? r2;

    public ICloudflareApiClient Api { get; }
    public IDnsApi Dns => Api.Dns;
    public IKvApi Kv => Api.Accounts.Kv;
    public ID1Api D1 => Api.Accounts.D1;
    public IAccountsApi Accounts => Api.Accounts;
    public IR2Client R2 => r2 ?? throw new InvalidOperationException("Register R2 credentials with AddCloudflareKit to use object storage.");
    public TunnelClient Tunnels { get; }
    public WorkerScriptsClient WorkerScripts { get; }

    public CloudflareKitClient(ICloudflareApiClient api, TunnelClient tunnels, WorkerScriptsClient workerScripts,
        IR2Client? r2 = null)
    {
        Api = api;
        Tunnels = tunnels;
        WorkerScripts = workerScripts;
        this.r2 = r2;
    }
}
