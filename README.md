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

## 注册与使用

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

## Tunnel 示例

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

## Worker 上传

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

完整、可编译的五类资源示例见 `samples/Cloudflare.Kit.Sample/Examples.cs`。示例程序默认只读取 Tunnel 列表，不创建或删除资源。

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

测试使用模拟 HttpMessageHandler，覆盖分页、鉴权头、配置字段往返、multipart、业务失败、重试、取消及 DI/R2 注册，不访问真实账户。尚未验证真实 Cloudflare 账户调用。

## 参考与维护

- [Alos-no/Cloudflare.NET](https://github.com/Alos-no/Cloudflare.NET)：Apache-2.0，所复用的社区 SDK。
- [Cloudflare Tunnel API](https://developers.cloudflare.com/api/resources/zero_trust/subresources/tunnels/subresources/cloudflared/)。
- [Workers multipart 元数据](https://developers.cloudflare.com/workers/configuration/multipart-upload-metadata/)。
- [Cloudflare 的 .NET R2 示例](https://developers.cloudflare.com/r2/examples/aws/aws-sdk-net/)。

本地实现与文档核对日期：2026-10-27。新增封装按实际使用范围维护，没有宣称覆盖 Cloudflare 全部接口。
