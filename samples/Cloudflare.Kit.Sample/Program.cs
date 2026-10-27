using Cloudflare.Kit;
using Microsoft.Extensions.DependencyInjection;
using Cloudflare.NET.Core;
using Cloudflare.NET.R2.Configuration;
using System.Text.Json;

if (args.Length == 3 && args[0] == "--live-readonly" && args[1] == "--config")
{
    using var json = JsonDocument.Parse(await File.ReadAllTextAsync(args[2]));
    var settings = json.RootElement.GetProperty("Cloudflare");
    var options = settings.Deserialize<CloudflareApiOptions>()!;
    R2Settings? r2Settings = null;
    if (settings.TryGetProperty("R2", out var r2) && r2.TryGetProperty("AccessKeyId", out var accessKey)
        && !string.IsNullOrWhiteSpace(accessKey.GetString()))
    {
        r2Settings = r2.Deserialize<R2Settings>();
    }
    using var client = new CloudflareKitClient(options, r2Settings);
    Environment.ExitCode = await LiveReadOnlyChecks.RunAsync(client, options.AccountId);
    return;
}

var token = Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN");
var accountId = Environment.GetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID");
if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(accountId))
{
    Console.WriteLine("Set CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID to list tunnels (read-only).");
    return;
}

var services = new ServiceCollection();
services.AddCloudflareKit(options =>
{
    options.ApiToken = token;
    options.AccountId = accountId;
});
using var provider = services.BuildServiceProvider();
var cloudflare = provider.GetRequiredService<CloudflareKitClient>();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
try
{
    await foreach (var tunnel in cloudflare.Tunnels.ListAllAsync(accountId, cancellationToken: cancellation.Token))
    {
        Console.WriteLine($"{tunnel.Id}\t{tunnel.Name}\t{tunnel.Status}");
    }
}
catch (CloudflareKitException error)
{
    Console.Error.WriteLine($"Cloudflare request failed: {(int)error.StatusCode}; Ray ID: {error.RayId}");
    Environment.ExitCode = 1;
}
catch (OperationCanceledException)
{
    Console.WriteLine("Cancelled.");
}
