# Cloudflare.Kit

[![NuGet](https://img.shields.io/nuget/v/Cloudflare.Kit.svg)](https://www.nuget.org/packages/Cloudflare.Kit/)

用于 .NET 8+ 的 Cloudflare SDK 组合包。复用社区维护的 Cloudflare.NET，补充 Tunnel 管理和 Worker 模块脚本上传，提供统一调用入口。本项目不是 Cloudflare 官方 SDK。

## 覆盖范围

| 入口 | 功能 | 实现 |
|---|---|---|
| `Dns` | DNS 记录、批量操作、导入导出，保留 Worker 归属和只读元数据 | 内置上游 API 源码 Fork |
| `Kv` | 命名空间、文本/二进制、metadata、过期时间、批量操作 | 同上 |
| `D1` | 数据库管理、参数化 SQL、导出导入 | 同上 |
| `Accounts` | R2 桶、CORS、生命周期、自定义域等账户接口 | 同上 |
| `R2` | 对象上传下载、分片上传、预签名 URL | 内置上游 R2 源码 Fork |
| `Tunnels` | 列表/自动分页、详情、创建远程隧道、重命名、删除、token、ingress、连接查询/移除 | Kit |
| `WorkerScripts` | ES Module Worker 的 multipart 上传部署、多模块/资源、绑定元数据、删除 | Kit |
| `Api` | 完整上游客户端，访问 Zones、WAF、Turnstile 等接口 | 内置上游 API 源码 Fork |

没有引入 Cloudflare.ZeroTrust / Cloudflare.Net Kiota 包，避免与 Alos 的程序集名称冲突。Workers 并非全套接口，Pages、Queues 和 Tunnel 私网路由/subnet 尚未封装。

## 引用

通过 NuGet 安装（API 和 R2 程序集已包含在主包中，AWS、Microsoft.Extensions 等第三方依赖自动还原）：

```powershell
dotnet add package Cloudflare.Kit --version 0.1.3
```

源码仓库：[bighamx/Cloudflare.Kit](https://github.com/bighamx/Cloudflare.Kit)。也可克隆源码后使用项目引用：

```powershell
git clone --recurse-submodules https://github.com/bighamx/Cloudflare.Kit.git
dotnet add YourApp.csproj reference ./Cloudflare.Kit/src/Cloudflare.Kit/Cloudflare.Kit.csproj
```

已有克隆先执行 `git submodule update --init --recursive`。执行 `./tools/Build-Packages.ps1` 可将单个 `Cloudflare.Kit` 包生成到 `packages/`，用于本地包源或发布。包中包含 Kit、API、R2 三个程序集及 API/R2 的 XML 文档、上游许可证和 Fork 说明；仍保留三个源码项目，便于独立修改和调试。

仓库包含 SDK 源码、单元测试和可编译示例；构建要求 .NET 8 或更新的稳定版 SDK。

### 上游源码与 DNS 元数据

现有 `client.Dns` 的调用方式不变。API 和 R2 均通过项目引用从 `upstream/Cloudflare.NET` 编译，该目录是 [bighamx/Cloudflare.NET](https://github.com/bighamx/Cloudflare.NET/tree/kit) 的 Git submodule，固定具体提交。Fork 基于上游提交 `dd11a749db394f2b8d4517bd0ffb56f4ba1c0244`（API 3.7.0、R2 2.5.0），保留原许可证、版权、程序集名称和签名，并扩展 DNS 返回模型：

```csharp
var record = await client.Dns.GetDnsRecordAsync(zoneId, recordId);
var workerId = record.Meta?.OriginWorkerId; // meta.origin_worker_id
var readOnly = record.Meta?.ReadOnly;       // meta.read_only，缺失为 null
```

列表和自动分页查询同样保留这些字段。`DnsRecord`、`DnsRecordMeta` 的未知字段通过 `AdditionalProperties` 保留，再输出 JSON 时不会丢失；已有构造函数、解构方法及程序集标识保持兼容。`AAAA → 100::` 不是 Worker 归属证明，应读取 `OriginWorkerId`；`ReadOnly` 应独立用于限制编辑、删除，缺失时不推测权限。

从 0.1.3 起仅发布 `Cloudflare.Kit`，不再依赖独立的 `Cloudflare.Kit.Api` 和 `Cloudflare.Kit.R2` 包。升级时如曾显式安装这两个包，请移除其直接引用。不要同时引用官方 `Cloudflare.NET.Api` / `Cloudflare.NET.R2` 包，避免相同程序集的重复引用。内置源码为本项目维护的 Fork，非上游官方发布；包内 `licenses/Cloudflare.NET/` 保留 Apache-2.0 许可证和修改说明。旧补丁文件、二进制依赖和补丁构建脚本已移除。

修改上游功能时直接编辑 submodule 中的源码，在其 `kit` 分支提交、推送，然后在 Kit 提交新的 submodule 指针。更新上游时先在 Fork 合并选定的上游提交，检查差异、兼容性和包版本，运行 Kit 构建、测试及打包，再更新指针；不自动跟随上游 HEAD。详细修改记录见 [FORK.md](upstream/Cloudflare.NET/FORK.md)。

NuGet 发布工作流位于 `.github/workflows/publish-nuget.yml`，推送 `v*` 标签或手动触发后，递归检出源码、构建、测试、检查单包内容及独立 Console 安装，再发布 Kit。上游依赖升级时同步检查 Kit 项目的第三方 PackageReference。使用 NuGet 账户 `chuckiep` 的 Trusted Publishing，绑定仓库 `bighamx/Cloudflare.Kit`、工作流 `publish-nuget.yml` 和环境 `production`，通过 OIDC 获取短期凭据；不在仓库保存 API Key。

## 创建 Token 与其他凭据

先确定需要哪种凭据；资源 ID 是定位标识，不是密钥。

| 凭据 | SDK 中的用途 | 配置位置 |
|---|---|---|
| Cloudflare API Token（Bearer Token） | DNS、KV、D1、桶管理、Tunnel 管理、Worker 部署等管理接口 | `Cloudflare.ApiToken` |
| R2 Access Key ID + Secret Access Key | S3 对象上传、下载、列举和预签名 URL | `Cloudflare.R2.AccessKeyId` / `SecretAccessKey` |
| Tunnel Token | 让源站的 `cloudflared` 连接到指定隧道 | 交给 `cloudflared`，不填入 `Cloudflare.ApiToken` |
| Turnstile Sitekey + Secret Key | 网页挑战与后端 Siteverify 验证；不是 Widget 管理凭据 | 由业务网站配置，不用于本 SDK 的 API 鉴权 |

<details>
<summary>1. 创建管理 API Token：账户 Token 与用户 Token</summary>

账户 Token 适合服务集成，用户 Token 代表某个用户。创建入口不同：

- **账户 Token**：进入 Cloudflare 控制台，选择账户 → **管理账户 → 账户 API 令牌**。
- **用户 Token**：右上角用户菜单 → **我的配置文件 → API 令牌**。

创建过程：

1. 选择 **创建令牌**，使用自定义策略；名称注明应用和用途，例如 `my-app-readonly`。
2. 仅添加所需权限，读取选择 `Read`；需要写入时才选择对应的 `Edit` / `Write`。
3. 限定资源范围：账户功能选择目标账户，DNS 选择目标域名，避免直接使用全部权限模板。
4. 设置有效期。短期测试可以设为 7 天；有固定出口 IP 时可额外设置 IP 范围。
5. 审核摘要，确认权限和范围后创建。
6. 立即保存一次性显示的 Token，填入 `Cloudflare.ApiToken`。不要把 Token 名称或 Token ID 当成 Token 密钥。

账户 Token 需要创建者具备相应的令牌供应权限或超管角色，且只能授予自身拥有的权限子集。账户 Token 不保证覆盖全部产品或用户级接口；访问 `Api.User` 等用户接口前应确认 Token 类型适用，不能通过无关的额外权限解决所有 403。

参考：[创建 API Token](https://developers.cloudflare.com/fundamentals/api/get-started/create-token/)、[账户 Token 与产品兼容性](https://developers.cloudflare.com/fundamentals/api/get-started/account-owned-tokens/)。新旧控制台的中英文名称可能略有差异。

</details>

<details>
<summary>2. 按资源选择权限：只读测试与写入操作</summary>

按实际使用勾选下表中的权限，不需要一次添加全部权限。控制台可能把 API 中的 `Write` 显示为 `Edit`，以具体接口文档列出的权限为准。

| 需要的功能 | 只读权限示例 | 写入时需要的权限示例 / 范围 |
|---|---|---|
| 查找 Zone、读取 DNS | `Zone Read` + `DNS Read` | 修改 DNS 使用 `DNS Write`；限制到指定域名 |
| KV 命名空间、键值读取 | `Workers KV Storage Read` | 修改命名空间或键值使用 `Workers KV Storage Write` |
| D1 数据库及只读 SQL | `D1 Read` | 创建数据库或修改数据使用 `D1 Write` |
| R2 桶与配置读取 | `Workers R2 Storage Read` | 修改桶与配置使用 `Workers R2 Storage Write` |
| Tunnel 列表、配置与连接读取 | `Cloudflare Tunnel Read` 或接口接受的新版连接器读取权限 | 创建、配置、删除或取得运行 Token 使用接口接受的 `Cloudflare Tunnel Write` / 连接器写入权限 |
| Turnstile Widget 管理 | `Turnstile Sites Read` | 修改 Widget 使用 `Turnstile Sites Write`；Siteverify 使用业务 Secret Key |
| Worker 路由 | `Workers Routes Read` | 修改路由使用 `Workers Routes Write`，范围为目标 Zone |
| Worker 脚本上传 | 不通过只读权限执行上传 | 使用目标 Worker 对应的脚本编辑权限；旧权限名称为 `Workers Scripts Write` |

R2 **对象操作**的权限与桶管理不同，见下一节。WAF、审计日志、成员、订阅等额外接口同样需要各自的权限；SDK 的 `Api` 入口不会绕过 Cloudflare 的权限和套餐限制。

只需要运行本仓库的只读检查时，不要添加 DNS 写入、Worker 部署、Tunnel Token 获取等权限。该检查不会调用创建、删除或部署接口。

</details>

<details>
<summary>3. 创建 R2 S3 凭据：Access Key ID 与 Secret Access Key</summary>

1. 在控制台进入 **存储和数据库 → R2 对象存储 → 概览**，确认账户已开通 R2。
2. 在账户详情区域找到 **API Tokens / 管理 API 令牌**，选择创建账户 Token 或用户 Token。
3. 按用途选择：只读取对象用 **Object Read only**；上传、删除对象用 **Object Read & Write**。这两种对象权限可进一步限制到指定桶。
4. 如果还需要枚举桶、读取或修改桶配置，选择所需的 **Admin Read only** 或 **Admin Read & Write**，不要把对象级权限当作桶管理权限。
5. 设置范围与有效期并创建，保存显示的 **Access Key ID** 和 **Secret Access Key**。Secret 只显示一次。
6. 将这两个值填入 `Cloudflare.R2`。部分账户 Token 创建结果会同时显示 Bearer Token 与 S3 凭据，各自填入对应字段。

标准 S3 端点为 `https://<ACCOUNT_ID>.r2.cloudflarestorage.com`。本 SDK 的上游配置 `EndpointUrl` 接受**模板**：`https://{0}.r2.cloudflarestorage.com`；`{0}` 保持原样，由 SDK 填入账户 ID。通常省略该配置即可。jurisdiction 桶需要对应区域端点；不要将公共下载域名或 `r2.dev` 地址作为 S3 API 端点。

只有桶级对象权限的 S3 凭据可能可以列举指定桶中的对象，却不能列举账户中的桶，这是权限范围差异。只配置对象级凭据时，不要用完整桶管理检查来判定它是否有效。

参考：[R2 凭据创建、权限及区域端点](https://developers.cloudflare.com/r2/api/tokens/)。

</details>

<details>
<summary>4. 获取 Tunnel 运行 Token；与管理 API Token 区分</summary>

远程管理的隧道可以在控制台 **联网 / Networking → Tunnels** 中创建。选中隧道，进入 **Add a replica / 添加副本**，安装命令中包含该隧道的运行 Token。也可以用具有相应写入权限的管理 API Token 调用：

```csharp
var tunnelToken = await cloudflare.Tunnels.GetTokenAsync(accountId, tunnelId);
// 交给 cloudflared 使用，不输出到日志。
```

读取隧道列表所需的只读权限，**不足以取得运行 Token**。运行 Token 允许持有者启动该隧道的 connector，因此不要把它当作一般的状态查询结果。

在源站安装官方 `cloudflared` 后可用 `TUNNEL_TOKEN` 环境变量提供运行 Token，再执行 `cloudflared tunnel run`。SDK 本身不启动 connector；配置 Ingress 与 DNS 后仍需要源站 connector 在线。

本地管理隧道的 `cert.pem` 和 `<TUNNEL-UUID>.json` 是另一套 cloudflared 凭据，本 SDK 的远程管理流程不需要把它们加载进来。不要为了使用 SDK 而复制整个账户证书。

参考：[Tunnel Token 获取与轮换](https://developers.cloudflare.com/tunnel/reference/tunnel-tokens/)、[运行参数与环境变量](https://developers.cloudflare.com/cloudflare-one/networks/connectors/cloudflare-tunnel/configure-tunnels/run-parameters/)。

</details>

<details>
<summary>5. 查找 Account ID、Zone ID 和产品资源 ID</summary>

| 参数 | 获取位置 / SDK 查询方式 |
|---|---|
| `AccountId` | 控制台的账户详情；也可从账户页面 URL 中的账户标识确认 |
| `zoneId` | 目标域名的概览页；或 `cloudflare.Api.Zones.ListZonesAsync()` |
| `namespaceId` | Workers KV 命名空间详情；或 `cloudflare.Kv.ListAsync()` |
| `databaseId` | D1 数据库 UUID；或 `cloudflare.D1.ListAsync()` 返回的 `Uuid` |
| `bucket` | R2 桶名称；对象操作传桶名，不传 URL |
| `tunnelId` | Tunnel 详情中的 UUID；或 `cloudflare.Tunnels.ListAsync(accountId)` |
| `scriptName` | Workers 应用的脚本名称；不是 `workers.dev` 完整 URL |

Account ID 与 Zone ID 是不同标识，即使格式相似也不能互换。D1 的数据库名称、Workers 配置中的 binding 名称也不能代替数据库 UUID 或 KV namespace ID。

</details>

<details>
<summary>6. 保存到本地配置、验证与常见错误</summary>

复制 `appsettings.local.example.json` 为 `appsettings.local.json`，再在实际配置中填写凭据。以下只有占位符：

```json
{
  "Cloudflare": {
    "ApiToken": "YOUR_MANAGEMENT_API_TOKEN",
    "AccountId": "YOUR_ACCOUNT_ID",
    "R2": {
      "AccessKeyId": "YOUR_R2_ACCESS_KEY_ID",
      "SecretAccessKey": "YOUR_R2_SECRET_ACCESS_KEY"
    }
  }
}
```

不调用 R2 对象 API 时，可以省略 `R2` 配置。普通业务应用也可以从环境变量、.NET User Secrets 或部署平台的密钥服务读入值后传给客户端；SDK 不会自动替应用加载某个 JSON 文件。

本仓库提供读取上述文件的独立检查程序：

```powershell
dotnet run --project samples/Cloudflare.Kit.Sample -c Release -- --live-readonly --config appsettings.local.json
```

此文件已被 Git 忽略，仍应限制本机文件访问权限；不要把真实密钥写进 README、示例模板、截图或公共日志。遗失一次性显示的密钥时应重新签发，而不是尝试从控制台找回明文。替换应用配置并确认工作后，再撤销不再需要的旧凭据。

| 现象 | 优先检查 |
|---|---|
| 401 / 认证失败 | 是否填入实际 Token 密钥、是否已过期或撤销、有没有混用 Tunnel Token 或 R2 Secret |
| 403 / 权限不足 | Token 类型、接口权限、账户/域名/桶范围、IP 限制与产品套餐；不要直接添加所有权限 |
| R2 `SignatureDoesNotMatch` | Access Key 与 Secret 是否配对，账户 ID、区域端点与系统时钟是否正确 |
| `EndpointUrl` 配置校验失败 | 此配置必须保留 `{0}` 占位符，或直接省略使用默认值 |
| Token 验证成功但某个接口失败 | 验证只表明 Token 状态有效，不证明它具有该接口的资源权限 |

</details>

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
