using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Cloudflare.Kit.Tunnels;

/// <summary>Manages cloudflared Tunnel resources; does not run a local cloudflared connector.</summary>
public sealed class TunnelClient
{
    private readonly CloudflareTransport transport;

    public TunnelClient(CloudflareTransport transport)
    {
        this.transport = transport;
    }

    private static string Root(string accountId)
    {
        return "accounts/" + CloudflareTransport.Segment(accountId) + "/cfd_tunnel";
    }

    private static string Path(string accountId, string tunnelId)
    {
        return Root(accountId) + "/" + CloudflareTransport.Segment(tunnelId);
    }

    public Task<CloudflareResponse<IReadOnlyList<Tunnel>>> ListAsync(string accountId,
        TunnelListOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new TunnelListOptions();
        if (options.Page < 1 || options.PerPage is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
        var query = $"?page={options.Page.ToString(CultureInfo.InvariantCulture)}&per_page={options.PerPage.ToString(CultureInfo.InvariantCulture)}";
        if (options.IsDeleted.HasValue)
        {
            query += "&is_deleted=" + options.IsDeleted.Value.ToString().ToLowerInvariant();
        }
        if (options.Name is not null)
        {
            query += "&name=" + Uri.EscapeDataString(options.Name);
        }
        return transport.SendAsync<IReadOnlyList<Tunnel>>(HttpMethod.Get, Root(accountId) + query, null, cancellationToken);
    }

    public async IAsyncEnumerable<Tunnel> ListAllAsync(string accountId, string? name = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var page = 1; ; page++)
        {
            var response = await ListAsync(accountId, new TunnelListOptions(Page: page, Name: name), cancellationToken);
            foreach (var tunnel in response.Result)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return tunnel;
            }
            var info = response.ResultInfo;
            if (response.Result.Count == 0 || info?.TotalPages <= page
                || info?.TotalCount <= (long)page * (info?.PerPage ?? 100)
                || (info?.TotalPages is null && info?.TotalCount is null && response.Result.Count < 100))
            {
                yield break;
            }
        }
    }

    public async Task<Tunnel> GetAsync(string accountId, string tunnelId, CancellationToken cancellationToken = default)
    {
        var response = await transport.SendAsync<Tunnel>(HttpMethod.Get, Path(accountId, tunnelId), null, cancellationToken);
        return response.Result;
    }

    public async Task<Tunnel> CreateAsync(string accountId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var payload = new { name, config_src = "cloudflare" };
        var response = await transport.SendAsync<Tunnel>(HttpMethod.Post, Root(accountId),
            () => JsonContent.Create(payload, options: CloudflareTransport.Json), cancellationToken);
        return response.Result;
    }

    public async Task<Tunnel> RenameAsync(string accountId, string tunnelId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var response = await transport.SendAsync<Tunnel>(HttpMethod.Patch, Path(accountId, tunnelId),
            () => JsonContent.Create(new { name }), cancellationToken);
        return response.Result;
    }

    public async Task DeleteAsync(string accountId, string tunnelId, CancellationToken cancellationToken = default)
    {
        await transport.SendAsync<JsonElement?>(HttpMethod.Delete, Path(accountId, tunnelId), null, cancellationToken, false);
    }

    public async Task<string> GetTokenAsync(string accountId, string tunnelId, CancellationToken cancellationToken = default)
    {
        var response = await transport.SendAsync<string>(HttpMethod.Get, Path(accountId, tunnelId) + "/token", null, cancellationToken);
        return response.Result;
    }

    public async Task<TunnelConfiguration> GetConfigurationAsync(string accountId, string tunnelId,
        CancellationToken cancellationToken = default)
    {
        var response = await transport.SendAsync<TunnelConfiguration>(HttpMethod.Get, Path(accountId, tunnelId) + "/configurations", null, cancellationToken);
        return response.Result;
    }

    /// <summary>Replaces the complete remote configuration. Preserve fields when editing existing configurations.</summary>
    public async Task<TunnelConfiguration> SetConfigurationAsync(string accountId, string tunnelId, TunnelConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Ingress.Count == 0 || config.Ingress.Any(rule => string.IsNullOrWhiteSpace(rule.Service))
            || !string.IsNullOrEmpty(config.Ingress[^1].Hostname) || !string.IsNullOrEmpty(config.Ingress[^1].Path))
        {
            throw new ArgumentException("Ingress requires services and a final catch-all rule without hostname or path.", nameof(config));
        }
        var response = await transport.SendAsync<TunnelConfiguration>(HttpMethod.Put, Path(accountId, tunnelId) + "/configurations",
            () => JsonContent.Create(new { config }, options: CloudflareTransport.Json), cancellationToken);
        return response.Result;
    }

    public async Task<IReadOnlyList<TunnelConnection>> GetConnectionsAsync(string accountId, string tunnelId,
        CancellationToken cancellationToken = default)
    {
        var response = await transport.SendAsync<IReadOnlyList<TunnelConnection>>(HttpMethod.Get,
            Path(accountId, tunnelId) + "/connections", null, cancellationToken);
        return response.Result;
    }

    /// <summary>Removes the named connector even if currently active.</summary>
    public async Task RemoveConnectionAsync(string accountId, string tunnelId, string clientId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        var path = Path(accountId, tunnelId) + "/connections?client_id=" + Uri.EscapeDataString(clientId);
        await transport.SendAsync<JsonElement?>(HttpMethod.Delete, path, null, cancellationToken, false);
    }

    /// <summary>Removes every connector, including active ones. This can interrupt the tunnel.</summary>
    public async Task RemoveAllConnectionsAsync(string accountId, string tunnelId, CancellationToken cancellationToken = default)
    {
        await transport.SendAsync<JsonElement?>(HttpMethod.Delete, Path(accountId, tunnelId) + "/connections", null, cancellationToken, false);
    }
}
