using System.Text;
using System.Text.Json;
using Cloudflare.Kit;
using Cloudflare.NET.Accounts.Kv.Models;
using Cloudflare.NET.R2.Models;
using Cloudflare.NET.Zones.Models;

// Compiled documentation examples. Program.cs never calls these methods automatically.
public static class ResourceExamples
{
    public static async Task DnsAsync(CloudflareKitClient cloudflare, string zoneId)
    {
        // <example:dns>
        await foreach (var record in cloudflare.Dns.ListAllDnsRecordsAsync(zoneId))
        {
            Console.WriteLine($"{record.Name}: {record.Content}");
        }

        var existing = await cloudflare.Dns.FindDnsRecordByNameAsync(zoneId, "app.example.com");
        if (existing is null)
        {
            await cloudflare.Dns.CreateCnameRecordAsync(
                zoneId, "app.example.com", "origin.example.com", proxied: true);
        }
        // </example:dns>
    }

    public static async Task KvAsync(CloudflareKitClient cloudflare, string namespaceId)
    {
        // <example:kv>
        await foreach (var item in cloudflare.Kv.ListAllAsync())
        {
            Console.WriteLine(JsonSerializer.Serialize(item));
        }

        var metadata = JsonSerializer.SerializeToElement(new { purpose = "example" });
        var options = new KvWriteOptions(ExpirationTtl: 3600, Metadata: metadata);
        await cloudflare.Kv.WriteValueAsync(namespaceId, "examples/theme", "dark", options);
        var value = await cloudflare.Kv.GetValueAsync(namespaceId, "examples/theme");
        Console.WriteLine(value);

        await foreach (var key in cloudflare.Kv.ListAllKeysAsync(namespaceId, prefix: "examples/"))
        {
            Console.WriteLine(JsonSerializer.Serialize(key));
        }

        await cloudflare.Kv.DeleteValueAsync(namespaceId, "examples/theme");
        // </example:kv>
    }

    public static async Task D1Async(CloudflareKitClient cloudflare, string databaseId, string email)
    {
        // <example:d1>
        await foreach (var database in cloudflare.D1.ListAllAsync())
        {
            Console.WriteLine(JsonSerializer.Serialize(database));
        }

        await cloudflare.D1.QueryAsync(databaseId,
            "CREATE TABLE IF NOT EXISTS sdk_example_users (id INTEGER PRIMARY KEY, email TEXT NOT NULL)");
        await cloudflare.D1.QueryAsync(databaseId,
            "INSERT INTO sdk_example_users (email) VALUES (?)", new object[] { email });
        var result = await cloudflare.D1.QueryAsync(databaseId,
            "SELECT id, email FROM sdk_example_users WHERE email = ?", new object[] { email });
        Console.WriteLine(JsonSerializer.Serialize(result));
        // </example:d1>
    }

    public static async Task R2BucketsAsync(CloudflareKitClient cloudflare, string bucket)
    {
        // <example:r2-buckets>
        await foreach (var item in cloudflare.Accounts.Buckets.ListAllAsync())
        {
            Console.WriteLine(JsonSerializer.Serialize(item));
        }

        var cors = await cloudflare.Accounts.Buckets.GetCorsAsync(bucket);
        var lifecycle = await cloudflare.Accounts.Buckets.GetLifecycleAsync(bucket);
        Console.WriteLine(JsonSerializer.Serialize(cors));
        Console.WriteLine(JsonSerializer.Serialize(lifecycle));

        // 创建新桶时按需调用，不要重复创建已有桶。
        // await cloudflare.Accounts.Buckets.CreateAsync("my-new-bucket");
        // </example:r2-buckets>
    }

    public static async Task R2ObjectsAsync(CloudflareKitClient cloudflare, string bucket)
    {
        // <example:r2-objects>
        using var upload = new MemoryStream(Encoding.UTF8.GetBytes("Hello R2"));
        await cloudflare.R2.UploadAsync(bucket, "examples/hello.txt", upload);

        var objects = await cloudflare.R2.ListObjectsAsync(bucket, "examples/");
        Console.WriteLine(JsonSerializer.Serialize(objects));

        using var download = new MemoryStream();
        await cloudflare.R2.DownloadFileAsync(bucket, "examples/hello.txt", download);
        var text = Encoding.UTF8.GetString(download.ToArray());
        Console.WriteLine(text);

        var request = new PresignedGetRequest("examples/hello.txt", TimeSpan.FromMinutes(10));
        var signedUrl = cloudflare.R2.CreatePresignedGetUrl(bucket, request);
        // 将 signedUrl 提供给需要临时下载权限的调用方，避免写入公共日志。
        // </example:r2-objects>
        _ = signedUrl;
    }

    public static async Task ZonesAsync(CloudflareKitClient cloudflare, string zoneId)
    {
        // <example:zones>
        var zones = await cloudflare.Api.Zones.ListZonesAsync();
        var zone = await cloudflare.Api.Zones.GetZoneDetailsAsync(zoneId);
        Console.WriteLine(JsonSerializer.Serialize(zones));
        Console.WriteLine(JsonSerializer.Serialize(zone));

        var cacheRequest = new PurgeCacheRequest(Files: new[] { "https://app.example.com/style.css" });
        await cloudflare.Api.Zones.PurgeCacheAsync(zoneId, cacheRequest);
        // </example:zones>
    }

    public static async Task AccountsAsync(CloudflareKitClient cloudflare, string accountId)
    {
        // <example:accounts>
        await foreach (var account in cloudflare.Accounts.ListAllAccountsAsync())
        {
            Console.WriteLine(JsonSerializer.Serialize(account));
        }

        var details = await cloudflare.Accounts.GetAccountAsync(accountId);
        Console.WriteLine(JsonSerializer.Serialize(details));
        // </example:accounts>
    }

    public static async Task WorkerRoutesAsync(CloudflareKitClient cloudflare, string zoneId)
    {
        // <example:worker-routes>
        var routes = await cloudflare.Api.Workers.ListRoutesAsync(zoneId);
        Console.WriteLine(JsonSerializer.Serialize(routes));
        // </example:worker-routes>
    }

    public static async Task TurnstileAsync(CloudflareKitClient cloudflare, string accountId)
    {
        // <example:turnstile>
        await foreach (var widget in cloudflare.Api.Turnstile.ListAllWidgetsAsync(accountId))
        {
            // 只展示公开的 sitekey，不输出可能包含 secret 的完整 Widget 对象。
            Console.WriteLine(widget.Sitekey);
        }
        // </example:turnstile>
    }

    public static async Task ApiTokensAsync(CloudflareKitClient cloudflare, string accountId)
    {
        // <example:api-tokens>
        var verification = await cloudflare.Api.ApiTokens.VerifyAccountTokenAsync(accountId);
        Console.WriteLine(JsonSerializer.Serialize(verification));

        var permissions = await cloudflare.Api.ApiTokens.GetAccountPermissionGroupsAsync(accountId);
        Console.WriteLine(JsonSerializer.Serialize(permissions));
        // </example:api-tokens>
    }

    public static async Task AuditLogsAsync(CloudflareKitClient cloudflare, string accountId)
    {
        // <example:audit-logs>
        var logs = await cloudflare.Api.AuditLogs.GetAccountAuditLogsAsync(accountId);
        Console.WriteLine(JsonSerializer.Serialize(logs));
        // </example:audit-logs>
    }

    public static async Task WafAsync(CloudflareKitClient cloudflare, string zoneId)
    {
        // <example:waf>
        await foreach (var ruleset in cloudflare.Api.Zones.Rulesets.ListAllAsync(zoneId))
        {
            Console.WriteLine(JsonSerializer.Serialize(ruleset));
        }

        await foreach (var ruleset in cloudflare.Accounts.Rulesets.ListAllAsync())
        {
            Console.WriteLine(JsonSerializer.Serialize(ruleset));
        }
        // </example:waf>
    }

    public static async Task MembersAndRolesAsync(CloudflareKitClient cloudflare, string accountId)
    {
        // <example:members-roles>
        await foreach (var member in cloudflare.Api.Members.ListAllAccountMembersAsync(accountId))
        {
            Console.WriteLine(JsonSerializer.Serialize(member));
        }

        await foreach (var role in cloudflare.Api.Roles.ListAllAccountRolesAsync(accountId))
        {
            Console.WriteLine(JsonSerializer.Serialize(role));
        }
        // </example:members-roles>
    }

    public static async Task UserAsync(CloudflareKitClient cloudflare)
    {
        // <example:user>
        var user = await cloudflare.Api.User.GetUserAsync();
        var memberships = await cloudflare.Api.User.ListMembershipsAsync();
        Console.WriteLine(JsonSerializer.Serialize(user));
        Console.WriteLine(JsonSerializer.Serialize(memberships));
        // </example:user>
    }

    public static async Task SubscriptionsAsync(CloudflareKitClient cloudflare, string accountId)
    {
        // <example:subscriptions>
        var subscriptions = await cloudflare.Api.Subscriptions.ListAccountSubscriptionsAsync(accountId);
        Console.WriteLine(JsonSerializer.Serialize(subscriptions));
        // </example:subscriptions>
    }
}
