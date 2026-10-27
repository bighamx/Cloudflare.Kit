using Cloudflare.Kit;
using Cloudflare.NET.Core;
using Cloudflare.NET.R2.Configuration;

// These methods are compiled examples and are not invoked by Program.cs.
public static class StandaloneExamples
{
    public static async Task WithoutDiAsync(string apiToken, string accountId)
    {
        // <example:standalone>
        using var cloudflare = new CloudflareKitClient(new CloudflareApiOptions
        {
            ApiToken = apiToken,
            AccountId = accountId,
            DefaultTimeout = TimeSpan.FromSeconds(30)
        });

        await foreach (var tunnel in cloudflare.Tunnels.ListAllAsync(accountId))
        {
            Console.WriteLine($"{tunnel.Name}: {tunnel.Status}");
        }

        var databases = await cloudflare.D1.ListAsync();
        Console.WriteLine(databases.Items.Count);
        // </example:standalone>
    }

    public static async Task WithR2Async(string apiToken, string accountId, string accessKeyId,
        string secretAccessKey, string bucket)
    {
        // <example:standalone-r2>
        using var cloudflare = new CloudflareKitClient(new CloudflareApiOptions
        {
            ApiToken = apiToken,
            AccountId = accountId
        }, new R2Settings
        {
            AccessKeyId = accessKeyId,
            SecretAccessKey = secretAccessKey
        });

        var objects = await cloudflare.R2.ListObjectsAsync(bucket, prefix: null);
        Console.WriteLine(objects.Data.Count);
        // </example:standalone-r2>
    }
}
