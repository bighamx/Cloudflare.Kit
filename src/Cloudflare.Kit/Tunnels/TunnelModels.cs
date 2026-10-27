using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cloudflare.Kit.Tunnels;

public sealed class Tunnel
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? Status { get; init; }
    public string? AccountTag { get; init; }
    public string? ConfigSrc { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
    public IReadOnlyList<TunnelConnection>? Connections { get; init; }
}

public sealed class TunnelConnection
{
    public string? Id { get; init; }
    public string? ClientId { get; init; }
    public string? ClientVersion { get; init; }
    public string? ColoName { get; init; }
    public string? OriginIp { get; init; }
    public bool? IsPendingReconnect { get; init; }
    public DateTimeOffset? OpenedAt { get; init; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed class TunnelConfiguration
{
    public string? TunnelId { get; init; }
    public long? Version { get; init; }
    public string? Source { get; init; }
    public TunnelConfig? Config { get; init; }
}

public sealed class TunnelConfig
{
    public IReadOnlyList<IngressRule> Ingress { get; init; } = [];
    [JsonPropertyName("originRequest")]
    public Dictionary<string, JsonElement>? OriginRequest { get; init; }
    [JsonPropertyName("warp-routing")]
    public WarpRouting? WarpRouting { get; init; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record WarpRouting(bool Enabled);

public sealed class IngressRule
{
    public string? Hostname { get; init; }
    public string? Path { get; init; }
    public required string Service { get; init; }
    [JsonPropertyName("originRequest")]
    public Dictionary<string, JsonElement>? OriginRequest { get; init; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record TunnelListOptions(int Page = 1, int PerPage = 100, string? Name = null, bool? IsDeleted = false);
