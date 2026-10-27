# Cloudflare.Kit

用于 .NET 8+ 的 Cloudflare SDK 组合包。复用社区维护的 Cloudflare.NET，补充 Tunnel 管理和 Worker 模块脚本上传，提供统一调用入口。本项目不是 Cloudflare 官方 SDK。

## 覆盖范围

| 入口 | 功能 | 实现 |
|---|---|---|
| `Dns` | DNS 记录、批量操作、导入导出 | Cloudflare.NET.Api 3.7.0 |
| `Kv` | 命名空间、文本/二进制、metadata、过期时间、批量操作 | 同上 |
| `D1` | 数据库管理、参数化 SQL、导出导入 | 同上 |
| `Accounts` | R2 桶、CORS、生命周期、自定义域等账户接口 | 同上 |
| `R2` | 对象上传下载、分片上传、预签名 URL | Cloudflare.NET.R2 2.5.0 |
| `Tunnels` | 列表/自动分页、详情、创建远程隧道、重命名、删除、token、ingress、连接查询/移除 | Kit |
| `WorkerScripts` | ES Module Worker 的 multipart 上传部署、多模块/资源、绑定元数据、删除 | Kit |
| `Api` | 完整上游客户端，访问 Zones、WAF、Turnstile 等接口 | Cloudflare.NET.Api |

没有引入 Cloudflare.ZeroTrust / Cloudflare.Net Kiota 包，避免与 Alos 的程序集名称冲突。Workers 并非全套接口，Pages、Queues 和 Tunnel 私网路由/subnet 尚未封装。

## 引用

源码仓库：[bighamx/Cloudflare.Kit](https://github.com/bighamx/Cloudflare.Kit)。当前尚未发布到 NuGet.org，可克隆源码后使用项目引用：

```powershell
git clone https://github.com/bighamx/Cloudflare.Kit.git
dotnet add YourApp.csproj reference ./Cloudflare.Kit/src/Cloudflare.Kit/Cloudflare.Kit.csproj
```

或者执行 `dotnet pack src/Cloudflare.Kit -c Release -o artifacts`，将生成的包作为本地 NuGet 源使用。包依赖会传递到调用方。

仓库包含 SDK 源码、单元测试和可编译示例；构建要求 .NET 8 或更新的稳定版 SDK。

## 创建客户端

控制台、WinForms、WPF、后台服务和 Web 项目都可以使用。直接 `new` 和 DI 两种方式提供相同的资源入口；以下示例默认折叠。

<details>
<summary>直接 new：不需要调用方使用 DI</summary>

```csharp
using Cloudflare.Kit;
using Cloudflare.NET.Core;

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
```

复用客户端，在应用或服务结束时释放。`using` 会释放该实例内部拥有的资源；不要每个请求反复创建。

</details>

<details>
<summary>直接 new：同时配置 R2 对象存储</summary>

```csharp
using Cloudflare.Kit;
using Cloudflare.NET.Core;
using Cloudflare.NET.R2.Configuration;

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
```

</details>

<details>
<summary>DI：注册、注入和配置</summary>

```csharp
using Cloudflare.Kit;
using Microsoft.Extensions.DependencyInjection;

services.AddCloudflareKit(options =>
{
    options.ApiToken = configuration["Cloudflare:ApiToken"]!;
    options.AccountId = configuration["Cloudflare:AccountId"]!;
}, r2 =>
{
    r2.AccessKeyId = configuration["Cloudflare:R2:AccessKeyId"]!;
    r2.SecretAccessKey = configuration["Cloudflare:R2:SecretAccessKey"]!;
});

var cloudflare = serviceProvider.GetRequiredService<CloudflareKitClient>();

await foreach (var tunnel in cloudflare.Tunnels.ListAllAsync(accountId))
{
    Console.WriteLine($"{tunnel.Name}: {tunnel.Status}");
}

var record = await cloudflare.Dns.FindDnsRecordByNameAsync(zoneId, "app.example.com");
var theme = await cloudflare.Kv.GetValueAsync(namespaceId, "theme");
var rows = await cloudflare.D1.QueryAsync(databaseId,
    "SELECT id, email FROM users WHERE email = ?", new object[] { email });
```

不使用对象上传下载时可以省略第二个 R2 配置参数；R2 桶管理仍通过 `Accounts` 使用。访问未配置的 `R2` 属性会提示缺少注册。

`Dns` 需要 Zone ID；KV、D1 和桶管理使用注册时的 Account ID；`Tunnels`、`WorkerScripts` 显式接收 Account ID。一个 Token 可以访问哪些账户和资源由授权范围决定。上游多凭据场景可直接使用其 named-client factories；Kit 的便捷 DI 注册当前对应一组默认凭据。

R2 对象 API 使用 S3 Access Key ID / Secret Access Key；这与 Cloudflare 管理 API 的 Bearer Token 是两种凭据，不可互换。不要将这些凭据放进浏览器端应用或提交源码。

通过 DI 注入时生命周期由容器管理，不要在业务方法中用 `using` 提前释放注入的客户端。

</details>

## 各类资源使用示例

以下代码沿用上面的 `cloudflare`，适用于直接 `new` 和 DI。资源标识与变量替换为自己的值，凭据需要相应权限。写操作会修改指定资源；示例程序不会自动执行这些方法。

<details>
<summary>DNS：枚举、查询与创建 CNAME</summary>

```csharp
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
```

</details>

<details>
<summary>Workers KV：命名空间、读写、metadata、TTL 与删除</summary>

```csharp
using System.Text.Json;
using Cloudflare.NET.Accounts.Kv.Models;

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
```

</details>

<details>
<summary>D1 SQLite：数据库、建表、参数化插入与查询</summary>

```csharp
using System.Text.Json;

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
```

</details>

<details>
<summary>R2 桶管理：列桶、CORS 与生命周期</summary>

```csharp
using System.Text.Json;

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
```

</details>

<details>
<summary>R2 对象：上传、列举、下载与预签名 URL</summary>

```csharp
using System.Text;
using System.Text.Json;
using Cloudflare.NET.R2.Models;

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
```

</details>

<details>
<summary>Zones：站点查询与指定文件缓存清理</summary>

```csharp
using System.Text.Json;
using Cloudflare.NET.Zones.Models;

var zones = await cloudflare.Api.Zones.ListZonesAsync();
var zone = await cloudflare.Api.Zones.GetZoneDetailsAsync(zoneId);
Console.WriteLine(JsonSerializer.Serialize(zones));
Console.WriteLine(JsonSerializer.Serialize(zone));

var cacheRequest = new PurgeCacheRequest(Files: new[] { "https://app.example.com/style.css" });
await cloudflare.Api.Zones.PurgeCacheAsync(zoneId, cacheRequest);
```

</details>

<details>
<summary>Accounts：账户列表与详情</summary>

```csharp
using System.Text.Json;

await foreach (var account in cloudflare.Accounts.ListAllAccountsAsync())
{
    Console.WriteLine(JsonSerializer.Serialize(account));
}

var details = await cloudflare.Accounts.GetAccountAsync(accountId);
Console.WriteLine(JsonSerializer.Serialize(details));
```

</details>

<details>
<summary>Worker 路由：查询 Zone 路由</summary>

```csharp
using System.Text.Json;

var routes = await cloudflare.Api.Workers.ListRoutesAsync(zoneId);
Console.WriteLine(JsonSerializer.Serialize(routes));
```

</details>

<details>
<summary>Turnstile：枚举 Widget</summary>

```csharp
await foreach (var widget in cloudflare.Api.Turnstile.ListAllWidgetsAsync(accountId))
{
    // 只展示公开的 sitekey，不输出可能包含 secret 的完整 Widget 对象。
    Console.WriteLine(widget.Sitekey);
}
```

</details>

<details>
<summary>API Token：账户 Token 校验与权限组</summary>

```csharp
using System.Text.Json;

var verification = await cloudflare.Api.ApiTokens.VerifyAccountTokenAsync(accountId);
Console.WriteLine(JsonSerializer.Serialize(verification));

var permissions = await cloudflare.Api.ApiTokens.GetAccountPermissionGroupsAsync(accountId);
Console.WriteLine(JsonSerializer.Serialize(permissions));
```

</details>

<details>
<summary>Audit Logs：账户审计日志</summary>

```csharp
using System.Text.Json;

var logs = await cloudflare.Api.AuditLogs.GetAccountAuditLogsAsync(accountId);
Console.WriteLine(JsonSerializer.Serialize(logs));
```

</details>

<details>
<summary>WAF Rulesets：Zone 与账户规则集</summary>

```csharp
using System.Text.Json;

await foreach (var ruleset in cloudflare.Api.Zones.Rulesets.ListAllAsync(zoneId))
{
    Console.WriteLine(JsonSerializer.Serialize(ruleset));
}

await foreach (var ruleset in cloudflare.Accounts.Rulesets.ListAllAsync())
{
    Console.WriteLine(JsonSerializer.Serialize(ruleset));
}
```

</details>

<details>
<summary>成员与角色：账户成员及角色</summary>

```csharp
using System.Text.Json;

await foreach (var member in cloudflare.Api.Members.ListAllAccountMembersAsync(accountId))
{
    Console.WriteLine(JsonSerializer.Serialize(member));
}

await foreach (var role in cloudflare.Api.Roles.ListAllAccountRolesAsync(accountId))
{
    Console.WriteLine(JsonSerializer.Serialize(role));
}
```

</details>

<details>
<summary>User：当前用户及成员关系</summary>

```csharp
using System.Text.Json;

var user = await cloudflare.Api.User.GetUserAsync();
var memberships = await cloudflare.Api.User.ListMembershipsAsync();
Console.WriteLine(JsonSerializer.Serialize(user));
Console.WriteLine(JsonSerializer.Serialize(memberships));
```

</details>

<details>
<summary>Subscriptions：账户订阅</summary>

```csharp
using System.Text.Json;

var subscriptions = await cloudflare.Api.Subscriptions.ListAccountSubscriptionsAsync(accountId);
Console.WriteLine(JsonSerializer.Serialize(subscriptions));
```

</details>

<details>
<summary>Tunnel：创建、Ingress、DNS 与 Token</summary>

```csharp
using Cloudflare.Kit.Tunnels;

var tunnel = await cloudflare.Tunnels.CreateAsync(accountId, "my-app");

await cloudflare.Tunnels.SetConfigurationAsync(accountId, tunnel.Id!, new TunnelConfig
{
    Ingress =
    [
        new IngressRule { Hostname = "app.example.com", Service = "http://localhost:8077" },
        new IngressRule { Service = "http_status:404" }
    ]
});

// DNS hostname 必须另行绑定到 tunnel 的 <ID>.cfargotunnel.com。
await cloudflare.Dns.CreateCnameRecordAsync(zoneId, "app.example.com",
    tunnel.Id + ".cfargotunnel.com", proxied: true);

var token = await cloudflare.Tunnels.GetTokenAsync(accountId, tunnel.Id!);
// 将 token 交给实际运行的 cloudflared connector，不要记录到日志。
```

SDK 管理隧道资源，不会启动 cloudflared，也不实现隧道传输协议。创建后需要在源站运行 cloudflared 才能真正连通。

`SetConfigurationAsync` 替换完整配置；编辑已有配置时先调用 `GetConfigurationAsync`。`originRequest` 字段保持 API 要求的 camelCase，未知配置属性也会保留。末尾 ingress 必须是无 hostname/path 的兜底规则。

`RemoveConnectionAsync` 移除指定 connector；`RemoveAllConnectionsAsync` 移除所有 connector，包括活跃连接，并非仅清理僵尸连接。

</details>

<details>
<summary>Worker 脚本：上传并部署 ES Module</summary>

```csharp
using System.Text;
using Cloudflare.Kit.Workers;

var source = Encoding.UTF8.GetBytes(
    "export default { fetch() { return new Response('Hello'); } };");

await cloudflare.WorkerScripts.UploadAsync(accountId, "hello-worker", new WorkerMetadata
{
    MainModule = "main.js",
    CompatibilityDate = "2026-10-27"
}, [new WorkerModule("main.js", source)]);
```

上传接口会部署脚本，不是只保存一个草稿。多模块的 part name 应与模块导入名称对应。`Bindings` 接受 `JsonArray`，可描述 KV、D1、R2 等绑定；`AdditionalProperties` 可传递 API 的其他元数据。替换已有 Worker 时应显式传入需要的绑定/设置，或按官方语义配置 `KeepBindings`；Kit 不会自动读取并合并旧设置。

</details>

可编译示例：[ResourceExamples.cs](samples/Cloudflare.Kit.Sample/ResourceExamples.cs)、[StandaloneExamples.cs](samples/Cloudflare.Kit.Sample/StandaloneExamples.cs) 和 [Examples.cs](samples/Cloudflare.Kit.Sample/Examples.cs)。示例程序默认只读取 Tunnel 列表，不会执行上述写操作。

## HTTP、错误与测试

- 新增 Tunnel/Worker 接口使用 `HttpClientFactory`，复用连接，支持 `CancellationToken`。
- 只有 GET 遇到 HTTP 429/5xx 时最多额外重试 2 次，遵守最多 30 秒的 `Retry-After`；更长的等待返回错误给调用方。网络异常不自动重试。
- 新增接口的写操作不自动重试，防止重复创建、重复部署。复用的上游 DNS/KV/D1/R2 接口仍采用上游 SDK 自己的重试策略。
- `CloudflareKitException` 提供 HTTP 状态、Cloudflare 错误列表和 CF-Ray，不在异常文本中附上原始响应体。上游接口使用上游异常类型。
- 默认 DI 传输禁用自动重定向；手工传入 HttpClient 时请自行配置 handler，调用方负责其生命周期。

```powershell
dotnet build Cloudflare.Kit.sln -c Release
dotnet test Cloudflare.Kit.sln -c Release
dotnet pack src/Cloudflare.Kit -c Release -o artifacts

# 设置环境变量后执行只读列表验证，不在命令行参数中传 token。
dotnet run --project samples/Cloudflare.Kit.Sample
```

单元测试使用模拟 HttpMessageHandler，覆盖分页、鉴权头、配置字段往返、multipart、业务失败、重试、取消、DI/R2 注册及直接 `new` 的创建与释放；单元测试不访问真实账户。真实只读验证单独执行，方法如下。

<details>
<summary>使用本地配置进行真实账户只读验证</summary>

将仓库中的 `appsettings.local.example.json` 复制为 `appsettings.local.json`，填写账户信息与只读凭据。实际配置已列入 `.gitignore`；不要将凭据填入示例模板或提交到仓库。

```powershell
dotnet run --project samples/Cloudflare.Kit.Sample -c Release -- --live-readonly --config appsettings.local.json
```

此命令使用直接 `new` 的调用方式，检查 Tunnel、DNS、KV、D1、R2 桶、S3 对象列表及 Turnstile。有数据库时只执行 `SELECT 1`，不读取业务表；对象列表最多获取一个条目，不下载对象正文。使用单独的测试 Token 并按所需接口授予只读权限。

如果配置 `R2.EndpointUrl`，上游 SDK 要求使用账户占位符，例如 `https://{0}.r2.cloudflarestorage.com`；省略时根据账户及 jurisdiction 自动构造。

2026-10-27 已使用真实账户完成上述只读检查，全部通过。写入、删除和 Worker 部署目前由模拟测试验证，未对真实业务资源执行。

</details>

## 参考与维护

- [Alos-no/Cloudflare.NET](https://github.com/Alos-no/Cloudflare.NET)：Apache-2.0，所复用的社区 SDK。
- [Cloudflare Tunnel API](https://developers.cloudflare.com/api/resources/zero_trust/subresources/tunnels/subresources/cloudflared/)。
- [Workers multipart 元数据](https://developers.cloudflare.com/workers/configuration/multipart-upload-metadata/)。
- [Cloudflare 的 .NET R2 示例](https://developers.cloudflare.com/r2/examples/aws/aws-sdk-net/)。

本地实现与文档核对日期：2026-10-27。新增封装按实际使用范围维护，没有宣称覆盖 Cloudflare 全部接口。
