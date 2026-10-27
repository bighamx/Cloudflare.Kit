using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cloudflare.Kit;

public sealed class CloudflareKitException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? RayId { get; }
    public IReadOnlyList<CloudflareError> Errors { get; }

    internal CloudflareKitException(HttpStatusCode statusCode, string? rayId,
        IReadOnlyList<CloudflareError> errors, string message) : base(message)
    {
        StatusCode = statusCode;
        RayId = rayId;
        Errors = errors;
    }
}

public sealed record CloudflareError(int? Code, string? Message);

public sealed class CloudflarePageInfo
{
    public int? Page { get; init; }
    public int? Count { get; init; }
    [JsonPropertyName("per_page")]
    public int? PerPage { get; init; }
    [JsonPropertyName("total_count")]
    public int? TotalCount { get; init; }
    [JsonPropertyName("total_pages")]
    public int? TotalPages { get; init; }
}

public sealed class CloudflareResponse<T>
{
    public bool Success { get; init; }
    public T Result { get; init; } = default!;
    public IReadOnlyList<CloudflareError> Errors { get; init; } = [];
    [JsonPropertyName("result_info")]
    public CloudflarePageInfo? ResultInfo { get; init; }
    public JsonElement? Messages { get; init; }
}
