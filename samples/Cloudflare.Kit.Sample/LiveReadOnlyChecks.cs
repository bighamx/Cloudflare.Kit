using Cloudflare.Kit;
using Cloudflare.NET.Core.Exceptions;

public static class LiveReadOnlyChecks
{
    public static async Task<int> RunAsync(CloudflareKitClient client, string accountId)
    {
        var failures = 0;
        string? sampleBucket = null;
        async Task Check(string name, Func<Task<string>> operation)
        {
            try
            {
                var summary = await operation();
                Console.WriteLine($"PASS {name}: {summary}");
            }
            catch (CloudflareKitException error)
            {
                Console.WriteLine($"FAIL {name}: HTTP {(int)error.StatusCode}");
                failures++;
            }
            catch (CloudflareApiException error)
            {
                Console.WriteLine($"FAIL {name}: {error.GetType().Name}");
                failures++;
            }
            catch (Exception error)
            {
                // Never dump raw response bodies, access keys or request headers in test output.
                Console.WriteLine($"FAIL {name}: {error.GetType().Name}");
                failures++;
            }
        }

        await Check("Tunnels", async () =>
        {
            var page = await client.Tunnels.ListAsync(accountId);
            if (page.Result.FirstOrDefault()?.Id is { } tunnelId)
            {
                await client.Tunnels.GetAsync(accountId, tunnelId);
                await client.Tunnels.GetConnectionsAsync(accountId, tunnelId);
                await client.Tunnels.GetConfigurationAsync(accountId, tunnelId);
            }
            return $"{page.Result.Count} listed; available tunnel detail/configuration/connections read";
        });
        await Check("DNS", async () =>
        {
            var zones = await client.Api.Zones.ListZonesAsync();
            if (zones.Items.FirstOrDefault()?.Id is { } zoneId)
            {
                var records = await client.Dns.ListDnsRecordsAsync(zoneId);
                return $"{zones.Items.Count} zones; {records.Items.Count} records on first page";
            }
            return "zone listing succeeded; no accessible zones to read records";
        });
        await Check("KV", async () =>
        {
            var page = await client.Kv.ListAsync();
            if (page.Items.FirstOrDefault()?.Id is { } namespaceId)
            {
                await client.Kv.ListKeysAsync(namespaceId);
            }
            return $"{page.Items.Count} namespaces listed; available namespace keys read";
        });
        await Check("D1", async () =>
        {
            var page = await client.D1.ListAsync();
            if (page.Items.FirstOrDefault()?.Uuid is { } databaseId)
            {
                var results = await client.D1.QueryAsync(databaseId, "SELECT 1 AS sdk_test");
                if (results.Any(result => !result.Success))
                {
                    throw new InvalidOperationException("D1 query failed");
                }
            }
            return $"{page.Items.Count} databases listed; SELECT 1 checked when a database exists";
        });
        await Check("R2 buckets", async () =>
        {
            var page = await client.Accounts.Buckets.ListAsync();
            sampleBucket = page.Items.FirstOrDefault()?.Name;
            return $"{page.Items.Count} buckets listed";
        });
        await Check("R2 objects (S3)", async () =>
        {
            if (sampleBucket is null)
            {
                return "no accessible bucket; object listing not exercised";
            }
            var result = await client.R2.ListObjectsPageAsync(sampleBucket, prefix: null, maxKeys: 1, continuationToken: null);
            return "S3 object listing completed (at most one object; no object content downloaded)";
        });
        await Check("Turnstile", async () =>
        {
            var page = await client.Api.Turnstile.ListWidgetsAsync(accountId);
            return $"{page.Items.Count} widgets listed";
        });
        Console.WriteLine($"Read-only checks complete. Failed: {failures}. No resources created or changed.");
        return failures == 0 ? 0 : 1;
    }
}
