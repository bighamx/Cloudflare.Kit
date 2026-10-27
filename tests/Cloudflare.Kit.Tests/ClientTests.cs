using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cloudflare.Kit.Tunnels;
using Cloudflare.Kit.Workers;
using Cloudflare.NET.Core;
using Cloudflare.NET.R2.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cloudflare.Kit.Tests;

public sealed class ClientTests
{
    private const string Account = "0123456789abcdef0123456789abcdef";
    private const string Id = "f70ff985-a4ef-4643-bbbc-4a0ed4fc8415";

    private static HttpResponseMessage Response(string body, HttpStatusCode code = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(code)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private static CloudflareTransport Transport(HttpClient http)
    {
        return new CloudflareTransport(http, new CloudflareApiOptions
        {
            ApiToken = "test-token",
            AccountId = Account
        });
    }

    [Fact]
    public void StandaloneConstructorExposesResourcesWithoutCallerDi()
    {
        using var client = new CloudflareKitClient(new CloudflareApiOptions
        {
            ApiToken = "test-token",
            AccountId = Account
        });
        Assert.NotNull(client.Api);
        Assert.NotNull(client.Dns);
        Assert.NotNull(client.Kv);
        Assert.NotNull(client.D1);
        Assert.NotNull(client.Tunnels);
        Assert.NotNull(client.WorkerScripts);
        Assert.Throws<InvalidOperationException>(() => client.R2);
    }

    [Fact]
    public void StandaloneConstructorSupportsR2AndIdempotentDisposal()
    {
        var client = new CloudflareKitClient(new CloudflareApiOptions
        {
            ApiToken = "test-token",
            AccountId = Account
        }, new R2Settings
        {
            AccessKeyId = "test-access-key",
            SecretAccessKey = "test-secret"
        });
        Assert.NotNull(client.R2);
        client.Dispose();
        client.Dispose();
        Assert.Throws<ObjectDisposedException>(() => client.Api);
        Assert.Throws<ObjectDisposedException>(() => client.R2);
        Assert.Throws<ObjectDisposedException>(() => client.Tunnels);
        Assert.Throws<ObjectDisposedException>(() => client.WorkerScripts);
    }

    [Fact]
    public void StandaloneConstructorRejectsMissingOptions()
    {
        Assert.Throws<ArgumentNullException>(() => new CloudflareKitClient((CloudflareApiOptions)null!));
    }

    [Fact]
    public async Task RegistrationExposesAllFiveResourcesWithoutR2Credentials()
    {
        var services = new ServiceCollection();
        services.AddCloudflareKit(options =>
        {
            options.ApiToken = "test-token";
            options.AccountId = Account;
        }).ConfigurePrimaryHttpMessageHandler(() => new Handler((_, _) => Task.FromResult(Response("{\"success\":true,\"result\":[]}"))));
        using var provider = services.BuildServiceProvider();
        var kit = provider.GetRequiredService<CloudflareKitClient>();
        Assert.NotNull(kit.Dns);
        Assert.NotNull(kit.Kv);
        Assert.NotNull(kit.D1);
        Assert.NotNull(kit.Accounts);
        Assert.NotNull(kit.WorkerScripts);
        Assert.Throws<InvalidOperationException>(() => kit.R2);
        Assert.Empty((await kit.Tunnels.ListAsync(Account)).Result);
    }

    [Fact]
    public void RegistrationSupportsR2WithoutAssemblyNameConflicts()
    {
        var services = new ServiceCollection();
        services.AddCloudflareKit(options =>
        {
            options.ApiToken = "test-token";
            options.AccountId = Account;
        }, options =>
        {
            options.AccessKeyId = "test-access-key";
            options.SecretAccessKey = "test-secret";
        });
        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<CloudflareKitClient>().R2);
    }

    [Fact]
    public async Task ListsAllPagesAndEncodesFilters()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            calls++;
            Assert.Equal("Bearer test-token", request.Headers.Authorization?.ToString());
            Assert.Contains("/client/v4/accounts/" + Account + "/cfd_tunnel", request.RequestUri!.AbsoluteUri);
            Assert.Contains("name=a%26b", request.RequestUri.Query);
            Assert.Contains("page=" + calls, request.RequestUri.Query);
            var json = JsonSerializer.Serialize(new
            {
                success = true,
                result = new[] { new { id = calls.ToString() } },
                result_info = new { page = calls, per_page = 1, total_count = 2 }
            });
            return Task.FromResult(Response(json));
        }));
        var client = new TunnelClient(Transport(http));
        var ids = new List<string?>();
        await foreach (var tunnel in client.ListAllAsync(Account, "a&b"))
        {
            ids.Add(tunnel.Id);
        }
        Assert.Equal(["1", "2"], ids);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CreatesRemoteTunnelAndGetsToken()
    {
        using var http = new HttpClient(new Handler(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
                Assert.Equal("test", body["name"]!.GetValue<string>());
                Assert.Equal("cloudflare", body["config_src"]!.GetValue<string>());
                Assert.Null(body["tunnel_secret"]);
                return Response(JsonSerializer.Serialize(new
                {
                    success = true,
                    result = new { id = Id, name = "test", status = "inactive" }
                }));
            }
            Assert.EndsWith("/" + Id + "/token", request.RequestUri!.AbsolutePath);
            return Response("{\"success\":true,\"result\":\"tunnel-token\"}");
        }));
        var client = new TunnelClient(Transport(http));
        Assert.Equal(Id, (await client.CreateAsync(Account, "test")).Id);
        Assert.Equal("tunnel-token", await client.GetTokenAsync(Account, Id));
    }

    [Fact]
    public async Task ConfigurationRoundTripsCamelCaseAndUnknownFields()
    {
        using var http = new HttpClient(new Handler(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Response("""{"success":true,"result":{"tunnel_id":"t","config":{"ingress":[{"hostname":"a.test","service":"http://localhost:8080","originRequest":{"noTLSVerify":true},"futureRule":1},{"service":"http_status:404"}],"originRequest":{"connectTimeout":10},"warp-routing":{"enabled":true},"futureConfig":true}}}""");
            }
            var json = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
            Assert.Equal(10, json["config"]!["originRequest"]!["connectTimeout"]!.GetValue<int>());
            Assert.True(json["config"]!["warp-routing"]!["enabled"]!.GetValue<bool>());
            Assert.True(json["config"]!["ingress"]![0]!["originRequest"]!["noTLSVerify"]!.GetValue<bool>());
            Assert.True(json["config"]!["futureConfig"]!.GetValue<bool>());
            Assert.Equal(1, json["config"]!["ingress"]![0]!["futureRule"]!.GetValue<int>());
            return Response("{\"success\":true,\"result\":{\"version\":2}}");
        }));
        var client = new TunnelClient(Transport(http));
        var config = await client.GetConfigurationAsync(Account, Id);
        Assert.Equal(2, (await client.SetConfigurationAsync(Account, Id, config.Config!)).Version);
        await Assert.ThrowsAsync<ArgumentException>(() => client.SetConfigurationAsync(Account, Id,
            new TunnelConfig { Ingress = [new IngressRule { Hostname = "a.test", Service = "http://localhost" }] }));
    }

    [Fact]
    public async Task SupportsGetRenameDeleteAndConnectionOperations()
    {
        var paths = new List<string>();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            paths.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            if (request.Method == HttpMethod.Delete)
            {
                return Task.FromResult(Response("{\"success\":true,\"result\":null}"));
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/connections"))
            {
                return Task.FromResult(Response("{\"success\":true,\"result\":[{\"client_id\":\"connector\",\"colo_name\":\"HKG\"}]}"));
            }
            return Task.FromResult(Response("{\"success\":true,\"result\":{\"name\":\"renamed\"}}"));
        }));
        var client = new TunnelClient(Transport(http));
        Assert.Equal("renamed", (await client.GetAsync(Account, Id)).Name);
        await client.RenameAsync(Account, Id, "renamed");
        Assert.Equal("HKG", (await client.GetConnectionsAsync(Account, Id))[0].ColoName);
        await client.RemoveConnectionAsync(Account, Id, "connector");
        await client.RemoveAllConnectionsAsync(Account, Id);
        await client.DeleteAsync(Account, Id);
        Assert.Contains(paths, path => path.StartsWith("PATCH ", StringComparison.Ordinal));
        Assert.Contains(paths, path => path.EndsWith("/connections?client_id=connector", StringComparison.Ordinal));
        Assert.Equal(6, paths.Count);
    }

    [Fact]
    public async Task UploadsWorkerModulesAndBindingsAsMultipart()
    {
        using var http = new HttpClient(new Handler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.EndsWith("/workers/scripts/hello", request.RequestUri!.AbsolutePath);
            var form = Assert.IsType<MultipartFormDataContent>(request.Content);
            var parts = form.ToArray();
            Assert.Equal(3, parts.Length);
            var metadata = JsonNode.Parse(await parts[0].ReadAsStringAsync(cancellationToken))!;
            Assert.Equal("main.js", metadata["main_module"]!.GetValue<string>());
            Assert.Equal("2026-10-27", metadata["compatibility_date"]!.GetValue<string>());
            Assert.Equal("kv_namespace", metadata["bindings"]![0]!["type"]!.GetValue<string>());
            Assert.Equal("application/javascript+module", parts[1].Headers.ContentType!.MediaType);
            Assert.Equal("export default {};", await parts[1].ReadAsStringAsync(cancellationToken));
            Assert.Equal("text/plain", parts[2].Headers.ContentType!.MediaType);
            return Response("{\"success\":true,\"result\":{\"id\":\"hello\",\"etag\":\"v1\"}}");
        }));
        var client = new WorkerScriptsClient(Transport(http));
        var result = await client.UploadAsync(Account, "hello", new WorkerMetadata
        {
            MainModule = "main.js",
            CompatibilityDate = "2026-10-27",
            Bindings = new JsonArray(new JsonObject { ["type"] = "kv_namespace", ["name"] = "CACHE", ["namespace_id"] = "ns" })
        }, [new WorkerModule("main.js", Encoding.UTF8.GetBytes("export default {};")),
            new WorkerModule("data.txt", Encoding.UTF8.GetBytes("data"), "text/plain")]);
        Assert.Equal("v1", result.Etag);
    }

    [Fact]
    public async Task InvalidWorkerModulesFailBeforeNetwork()
    {
        using var http = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("Unexpected request")));
        var client = new WorkerScriptsClient(Transport(http));
        var metadata = new WorkerMetadata { MainModule = "main.js", CompatibilityDate = "2026-10-27" };
        await Assert.ThrowsAsync<ArgumentException>(() => client.UploadAsync(Account, "hello", metadata, []));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UploadAsync(Account, "hello", metadata,
            [new WorkerModule("metadata", ReadOnlyMemory<byte>.Empty)]));
    }

    [Fact]
    public async Task ReadRetriesRateLimitButWritesNeverReplay()
    {
        var reads = 0;
        var writes = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Get && ++reads > 1)
            {
                return Task.FromResult(Response("{\"success\":true,\"result\":[]}"));
            }
            if (request.Method == HttpMethod.Post)
            {
                writes++;
            }
            var response = Response("{\"success\":false,\"errors\":[{\"code\":10000,\"message\":\"limited\"}]}", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        }));
        var client = new TunnelClient(Transport(http));
        Assert.Empty((await client.ListAsync(Account)).Result);
        var error = await Assert.ThrowsAsync<CloudflareKitException>(() => client.CreateAsync(Account, "test"));
        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Equal(2, reads);
        Assert.Equal(1, writes);
    }

    [Theory]
    [InlineData("{\"success\":false,\"errors\":[{\"code\":10000,\"message\":\"denied\"}]}", 200)]
    [InlineData("<html>private-response</html>", 403)]
    [InlineData("{\"success\":true,\"result\":null}", 200)]
    public async Task BusinessFailuresAndInvalidResponsesAreNotSuccess(string body, int status)
    {
        using var http = new HttpClient(new Handler((_, _) =>
        {
            var response = Response(body, (HttpStatusCode)status);
            response.Headers.Add("CF-Ray", "test-ray");
            return Task.FromResult(response);
        }));
        var client = new TunnelClient(Transport(http));
        var error = await Assert.ThrowsAsync<CloudflareKitException>(() => client.GetAsync(Account, Id));
        Assert.Equal("test-ray", error.RayId);
        Assert.DoesNotContain("private-response", error.ToString());
        Assert.DoesNotContain("test-token", error.ToString());
    }

    [Fact]
    public async Task CancellationFlowsToHttpHandler()
    {
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Unexpected completion");
        }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = new TunnelClient(Transport(http));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync(Account, Id, cancellation.Token));
    }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle;

        public Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle)
        {
            this.handle = handle;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return handle(request, cancellationToken);
        }
    }
}
