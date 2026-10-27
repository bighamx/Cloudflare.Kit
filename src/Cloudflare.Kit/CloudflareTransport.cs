using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cloudflare.NET.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cloudflare.Kit;

/// <summary>HTTP boundary for the APIs added by Kit. The caller owns the HttpClient.</summary>
public sealed class CloudflareTransport
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient http;
    private readonly string apiToken;
    private readonly Uri baseUri;

    [ActivatorUtilitiesConstructor]
    public CloudflareTransport(HttpClient http, IOptions<CloudflareApiOptions> options)
        : this(http, options.Value)
    {
    }

    public CloudflareTransport(HttpClient http, CloudflareApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ApiToken);
        var address = new Uri(options.ApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        if (address.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(address.UserInfo)
            || !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
        {
            throw new ArgumentException("The API base URL must be an HTTPS URL without credentials, query or fragment.");
        }
        this.http = http;
        apiToken = options.ApiToken;
        baseUri = address;
    }

    internal static string Segment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value is "." or "..")
        {
            throw new ArgumentException("A resource identifier cannot be a relative path.", nameof(value));
        }
        return Uri.EscapeDataString(value);
    }

    internal async Task<CloudflareResponse<T>> SendAsync<T>(HttpMethod method, string path,
        Func<HttpContent>? contentFactory, CancellationToken cancellationToken, bool requireResult = true)
    {
        var address = new Uri(baseUri, path);
        // Only safe reads are retried. Never replay creates, uploads, deletes or updates automatically.
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, address);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("Cloudflare.Kit/0.1.0");
            request.Content = contentFactory?.Invoke();
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (method == HttpMethod.Get && attempt < 2
                && (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500))
            {
                var retryAfter = response.Headers.RetryAfter;
                var delay = retryAfter?.Delta;
                if (retryAfter?.Date is { } date)
                {
                    delay = date - DateTimeOffset.UtcNow;
                }
                delay ??= TimeSpan.FromMilliseconds(250 * (1 << attempt));
                // A long server backoff is surfaced to the caller rather than retried early.
                if (delay.Value <= TimeSpan.FromSeconds(30))
                {
                    response.Dispose();
                    if (delay.Value > TimeSpan.Zero)
                    {
                        await Task.Delay(delay.Value, cancellationToken);
                    }
                    continue;
                }
            }

            CloudflareResponse<T>? envelope = null;
            try
            {
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                envelope = await JsonSerializer.DeserializeAsync<CloudflareResponse<T>>(stream, Json, cancellationToken);
            }
            catch (JsonException)
            {
                // Do not include raw response bodies: token and Worker endpoints can contain secrets.
            }
            var rayId = response.Headers.TryGetValues("CF-Ray", out var values) ? values.FirstOrDefault() : null;
            if (!response.IsSuccessStatusCode || envelope?.Success != true)
            {
                throw new CloudflareKitException(response.StatusCode, rayId, envelope?.Errors ?? [],
                    $"Cloudflare returned HTTP {(int)response.StatusCode}; the request did not complete successfully.");
            }
            if (requireResult && envelope.Result is null)
            {
                throw new CloudflareKitException(response.StatusCode, rayId, [], "Cloudflare returned no result.");
            }
            return envelope;
        }
    }
}
