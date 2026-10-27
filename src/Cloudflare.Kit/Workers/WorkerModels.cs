using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Cloudflare.Kit.Workers;

public sealed record WorkerModule(string Name, ReadOnlyMemory<byte> Content, string ContentType = "application/javascript+module");

public sealed class WorkerMetadata
{
    public required string MainModule { get; init; }
    public required string CompatibilityDate { get; init; }
    public IReadOnlyList<string>? CompatibilityFlags { get; init; }
    public JsonArray? Bindings { get; init; }
    public IReadOnlyList<string>? KeepBindings { get; init; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed class WorkerScript
{
    public string? Id { get; init; }
    public string? Etag { get; init; }
    public DateTimeOffset? CreatedOn { get; init; }
    public DateTimeOffset? ModifiedOn { get; init; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}
