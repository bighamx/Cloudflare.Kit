using System.Net;
using System.Text;
using System.Text.Json;
using Cloudflare.Kit.Tunnels;
using Cloudflare.Kit.Workers;
using Cloudflare.NET;
using Cloudflare.NET.Core;
using Cloudflare.NET.Dns.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cloudflare.Kit.Tests;

public sealed class DnsMetadataTests
{
    private const string Worker = """{"id":"record","name":"worker.example.com","type":"AAAA","content":"100::","ttl":1,"meta":{"origin_worker_id":"worker-id","read_only":true,"future_flag":{"value":7}},"future_record_field":[1,2]}""";
    private sealed class Handler(Func<HttpRequestMessage, string> result) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(result(request), Encoding.UTF8, "application/json")
        });
    }
    private static CloudflareKitClient Client(HttpClient http)
    {
        var options = new CloudflareApiOptions { ApiToken = "test-token" };
        http.BaseAddress = new Uri(options.ApiBaseUrl.TrimEnd('/') + "/");
        var transport = new CloudflareTransport(http, options);
        return new(new CloudflareApiClient(http, Options.Create(options), NullLoggerFactory.Instance), new TunnelClient(transport), new WorkerScriptsClient(transport));
    }
    private static string Envelope(string row) => "{\"success\":true,\"result\":" + row + "}";

    [Fact]
    public async Task ExistingDetailCallPreservesWorkerAndUnknownFields()
    {
        using var http = new HttpClient(new Handler(_ => Envelope(Worker)));
        using var client = Client(http);
        var row = await client.Dns.GetDnsRecordAsync("zone", "record");
        Assert.Equal("worker-id", row.Meta!.OriginWorkerId);
        Assert.True(row.Meta.ReadOnly);
        Assert.Null(row.Meta.Source);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal("worker-id", json.RootElement.GetProperty("meta").GetProperty("origin_worker_id").GetString());
        Assert.True(json.RootElement.GetProperty("meta").GetProperty("read_only").GetBoolean());
        Assert.Equal(7, json.RootElement.GetProperty("meta").GetProperty("future_flag").GetProperty("value").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("future_record_field").GetArrayLength());
    }
    [Fact]
    public async Task ExistingListAndPaginationCallsPreserveMetadata()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request => {
            calls++;
            return "{\"success\":true,\"result\":[" + Worker + "],\"result_info\":{\"page\":1,\"per_page\":100,\"total_pages\":1,\"total_count\":1}}";
        }));
        using var client = Client(http);
        var page = await client.Dns.ListDnsRecordsAsync("zone");
        Assert.Equal("worker-id", page.Items[0].Meta!.OriginWorkerId);
        var count = 0;
        await foreach (var row in client.Dns.ListAllDnsRecordsAsync("zone")) { count++; Assert.True(row.Meta!.ReadOnly); }
        Assert.Equal(1, count);
        Assert.Equal(2, calls);
    }
    [Theory]
    [InlineData("{\"type\":\"AAAA\",\"content\":\"100::\"}")]
    [InlineData("{\"meta\":null}")]
    [InlineData("{\"meta\":{\"auto_added\":false,\"source\":\"primary\",\"read_only\":false}}")]
    public async Task MissingOwnershipAndReadOnlyRemainUnknown(string record)
    {
        using var http = new HttpClient(new Handler(_ => Envelope(record)));
        using var client = Client(http);
        var row = await client.Dns.GetDnsRecordAsync("zone", "record");
        Assert.Null(row.Meta?.OriginWorkerId);
        Assert.NotEqual(true, row.Meta?.ReadOnly);
    }
    [Fact]
    public void OriginalConstructorAndAssemblyIdentityStayCompatible()
    {
        var meta = new DnsRecordMeta(true, "primary");
        var (autoAdded, source) = meta;
        Assert.True(autoAdded);
        Assert.Equal("primary", source);
        Assert.Equal(new Version(3, 7, 0, 0), typeof(DnsRecordMeta).Assembly.GetName().Version);
        Assert.DoesNotContain(typeof(CloudflareKitClient).GetProperties(), p => p.Name == "DnsRecords");
    }
}
