param([Parameter(Mandatory)][string]$PackageDirectory)

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskPackage = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.nupkg')

if ($taskPackage.Count -ne 1)
{
    throw 'Expected exactly one NuGet package.'
}

$taskArchive = [IO.Compression.ZipFile]::OpenRead($taskPackage[0].FullName)

try
{
    $taskRequired = @(
        'lib/net8.0/Cloudflare.Kit.dll'
        'lib/net8.0/Cloudflare.NET.dll'
        'lib/net8.0/Cloudflare.NET.xml'
        'lib/net8.0/Cloudflare.NET.R2.dll'
        'lib/net8.0/Cloudflare.NET.R2.xml'
        'licenses/Cloudflare.NET/LICENSE.txt'
        'licenses/Cloudflare.NET/FORK.md'
    )

    foreach ($taskEntry in $taskRequired)
    {
        if (!$taskArchive.GetEntry($taskEntry))
        {
            throw "Package is missing: $taskEntry"
        }
    }

    $taskNuspec = $taskArchive.Entries | Where-Object { $_.Name.EndsWith('.nuspec') }
    $taskReader = [IO.StreamReader]::new($taskNuspec.Open())

    try
    {
        [xml]$taskMetadata = $taskReader.ReadToEnd()
    }
    finally
    {
        $taskReader.Dispose()
    }
}
finally
{
    $taskArchive.Dispose()
}

$taskDependencies = $taskMetadata.SelectNodes("//*[local-name()='dependency']")

if ($taskDependencies | Where-Object { $_.id -match '^Cloudflare\.' })
{
    throw 'Single package must not depend on separate Cloudflare packages.'
}

foreach ($taskProjectName in @('Cloudflare.NET', 'Cloudflare.NET.R2'))
{
    [xml]$taskProject = Get-Content (Join-Path $taskRoot "upstream/Cloudflare.NET/src/$taskProjectName/$taskProjectName.csproj") -Raw

    foreach ($taskReference in $taskProject.SelectNodes('//PackageReference'))
    {
        $taskDependency = $taskDependencies | Where-Object { $_.id -eq $taskReference.Include }

        if (!$taskDependency -or $taskDependency.version -ne $taskReference.Version)
        {
            throw "Missing or mismatched upstream dependency: $($taskReference.Include)"
        }
    }
}

$taskVersion = $taskMetadata.package.metadata.version
$taskCheckRoot = Join-Path ([IO.Path]::GetTempPath()) ('cloudflare-kit-package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskCheckRoot | Out-Null
$taskFeed = [Security.SecurityElement]::Escape($taskPackage[0].DirectoryName)

@"
<configuration><packageSources><clear /><add key="Kit" value="$taskFeed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>
"@ | Set-Content (Join-Path $taskCheckRoot 'NuGet.Config') -Encoding utf8

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup><PackageReference Include="Cloudflare.Kit" Version="[$taskVersion]" /></ItemGroup>
</Project>
"@ | Set-Content (Join-Path $taskCheckRoot 'Consumer.csproj') -Encoding utf8

@'
using Cloudflare.Kit;
using Cloudflare.NET.Core;
using Cloudflare.NET.Dns.Models;
using Cloudflare.NET.R2.Configuration;

using var client = new CloudflareKitClient(new CloudflareApiOptions
{
    ApiToken = "package-check-no-network",
    AccountId = "test-account"
}, new R2Settings
{
    AccessKeyId = "test-access",
    SecretAccessKey = "test-secret"
});

var metadata = new DnsRecordMeta(true, "test")
{
    OriginWorkerId = "worker",
    ReadOnly = true
};

if (metadata.OriginWorkerId != "worker" || metadata.ReadOnly != true || client.Dns == null || client.D1 == null || client.R2 == null)
{
    throw new InvalidOperationException("Single-package consumer failed.");
}

Console.WriteLine("Single-package Console consumer passed without Cloudflare network calls.");
'@ | Set-Content (Join-Path $taskCheckRoot 'Program.cs') -Encoding utf8

& dotnet restore (Join-Path $taskCheckRoot 'Consumer.csproj') --configfile (Join-Path $taskCheckRoot 'NuGet.Config') --packages (Join-Path $taskCheckRoot 'cache')

if ($LASTEXITCODE)
{
    throw 'Single-package consumer restore failed.'
}

& dotnet run --project (Join-Path $taskCheckRoot 'Consumer.csproj') -c Release --no-restore

if ($LASTEXITCODE)
{
    throw 'Single-package consumer failed.'
}
