using System.Text;
using System.Text.Json;
using Cloudflare.Kit;
using Cloudflare.Kit.Tunnels;
using Cloudflare.Kit.Workers;

// These examples are compiled, but are not invoked by Program.cs.
public static class Examples
{
    public static async Task ReadDnsAsync(CloudflareKitClient cloudflare, string zoneId, CancellationToken cancellationToken)
    {
        await foreach (var record in cloudflare.Dns.ListAllDnsRecordsAsync(zoneId, cancellationToken: cancellationToken))
        {
            Console.WriteLine($"{record.Name}: {record.Content}");
        }
    }

    public static async Task ReadKvAsync(CloudflareKitClient cloudflare, string namespaceId, CancellationToken cancellationToken)
    {
        var value = await cloudflare.Kv.GetValueAsync(namespaceId, "settings/theme", cancellationToken);
        Console.WriteLine(value);
    }

    public static async Task QueryD1Async(CloudflareKitClient cloudflare, string databaseId, string email,
        CancellationToken cancellationToken)
    {
        var result = await cloudflare.D1.QueryAsync(databaseId,
            "SELECT id, email FROM users WHERE email = ?", new object[] { email }, cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(result));
    }

    public static async Task UploadR2Async(CloudflareKitClient cloudflare, string bucket, CancellationToken cancellationToken)
    {
        using var data = new MemoryStream(Encoding.UTF8.GetBytes("Hello R2"));
        await cloudflare.R2.UploadAsync(bucket, "examples/hello.txt", data, cancellationToken: cancellationToken);
    }

    public static async Task ConfigureTunnelAsync(CloudflareKitClient cloudflare, string accountId, string tunnelId,
        CancellationToken cancellationToken)
    {
        await cloudflare.Tunnels.SetConfigurationAsync(accountId, tunnelId, new TunnelConfig
        {
            Ingress =
            [
                new IngressRule { Hostname = "app.example.com", Service = "http://localhost:8077" },
                new IngressRule { Service = "http_status:404" }
            ]
        }, cancellationToken);
    }

    public static async Task UploadWorkerAsync(CloudflareKitClient cloudflare, string accountId,
        CancellationToken cancellationToken)
    {
        var source = Encoding.UTF8.GetBytes("export default { fetch() { return new Response('Hello'); } };");
        await cloudflare.WorkerScripts.UploadAsync(accountId, "hello-worker", new WorkerMetadata
        {
            MainModule = "main.js",
            CompatibilityDate = "2026-10-27"
        }, [new WorkerModule("main.js", source)], cancellationToken);
    }
}
