using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cloudflare.Kit.Workers;

public sealed class WorkerScriptsClient
{
    private readonly CloudflareTransport transport;

    public WorkerScriptsClient(CloudflareTransport transport)
    {
        this.transport = transport;
    }

    private static string Path(string accountId, string scriptName)
    {
        return "accounts/" + CloudflareTransport.Segment(accountId)
            + "/workers/scripts/" + CloudflareTransport.Segment(scriptName);
    }

    /// <summary>Uploads and deploys a module Worker. Existing settings/bindings follow Cloudflare's replacement semantics.</summary>
    public async Task<WorkerScript> UploadAsync(string accountId, string scriptName, WorkerMetadata metadata,
        IReadOnlyList<WorkerModule> modules, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.MainModule);
        if (!DateOnly.TryParseExact(metadata.CompatibilityDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _))
        {
            throw new ArgumentException("CompatibilityDate must use yyyy-MM-dd.", nameof(metadata));
        }
        var names = new HashSet<string>(StringComparer.Ordinal) { "metadata" };
        foreach (var module in modules)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(module.Name);
            if (module.Name.Any(char.IsControl) || module.Name.Contains('"') || !names.Add(module.Name))
            {
                throw new ArgumentException("Module names must be unique and contain no control characters or quotes.", nameof(modules));
            }
            MediaTypeHeaderValue.Parse(module.ContentType);
        }
        if (!modules.Any(module => module.Name == metadata.MainModule))
        {
            throw new ArgumentException("The main module must be included in modules.", nameof(modules));
        }

        HttpContent CreateContent()
        {
            var form = new MultipartFormDataContent();
            form.Add(JsonContent.Create(metadata, options: CloudflareTransport.Json), "metadata");
            foreach (var module in modules)
            {
                var part = new ReadOnlyMemoryContent(module.Content);
                part.Headers.ContentType = new MediaTypeHeaderValue(module.ContentType);
                form.Add(part, module.Name, module.Name);
            }
            return form;
        }

        var response = await transport.SendAsync<WorkerScript>(HttpMethod.Put, Path(accountId, scriptName),
            CreateContent, cancellationToken);
        return response.Result;
    }

    public async Task DeleteAsync(string accountId, string scriptName, CancellationToken cancellationToken = default)
    {
        await transport.SendAsync<JsonElement?>(HttpMethod.Delete, Path(accountId, scriptName), null, cancellationToken, false);
    }
}
