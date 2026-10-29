param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot

if (!$OutputDirectory)
{
    $OutputDirectory = Join-Path $taskRoot 'packages'
}

$taskProject = Join-Path $taskRoot 'src/Cloudflare.Kit/Cloudflare.Kit.csproj'
& dotnet pack $taskProject -c Release -o $OutputDirectory

if ($LASTEXITCODE)
{
    throw "Package build failed: $taskProject"
}
