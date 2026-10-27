using Cloudflare.Kit;
using Microsoft.Extensions.DependencyInjection;

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
