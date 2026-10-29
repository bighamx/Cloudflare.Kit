param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot

if (!$OutputDirectory)
{
    $OutputDirectory = Join-Path $taskRoot 'packages'
}

$taskProjects = @(
    'upstream/Cloudflare.NET/src/Cloudflare.NET/Cloudflare.NET.csproj'
    'upstream/Cloudflare.NET/src/Cloudflare.NET.R2/Cloudflare.NET.R2.csproj'
    'src/Cloudflare.Kit/Cloudflare.Kit.csproj'
)

foreach ($taskProject in $taskProjects)
{
    & dotnet pack (Join-Path $taskRoot $taskProject) -c Release -o $OutputDirectory

    if ($LASTEXITCODE)
    {
        throw "Package build failed: $taskProject"
    }
}
