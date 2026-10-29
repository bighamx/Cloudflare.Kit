param([string]$WorkDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskCommit = 'dd11a749db394f2b8d4517bd0ffb56f4ba1c0244'
if (!$WorkDirectory) { $WorkDirectory = Join-Path ([IO.Path]::GetTempPath()) ('cloudflare-dns-patch-' + [guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $WorkDirectory) { throw 'Choose a new work directory; existing files will not be overwritten.' }
& git clone --depth 1 --no-checkout https://github.com/Alos-no/Cloudflare.NET.git $WorkDirectory
if ($LASTEXITCODE) { throw 'Clone failed' }
& git -C $WorkDirectory fetch --depth 1 origin $taskCommit
if ($LASTEXITCODE) { throw 'Pinned source fetch failed' }
& git -C $WorkDirectory checkout --detach FETCH_HEAD
if ($LASTEXITCODE) { throw 'Checkout failed' }
if ((& git -C $WorkDirectory rev-parse HEAD) -ne $taskCommit) { throw 'Unexpected upstream revision' }
$taskPatch = Join-Path $taskRoot 'patches/cloudflare-net-api-dns-metadata.patch'
& git -C $WorkDirectory apply --check $taskPatch
if ($LASTEXITCODE) { throw 'Patch does not match pinned source' }
& git -C $WorkDirectory apply $taskPatch
if ($LASTEXITCODE) { throw 'Patch application failed' }
$taskProject = Join-Path $WorkDirectory 'src/Cloudflare.NET/Cloudflare.NET.csproj'
# Preserve the original strong-name assembly identity for R2 and existing callers.
$taskProperties = @('-p:TargetFrameworks=net8.0', '-p:Version=3.7.0.1', '-p:AssemblyVersion=3.7.0.0', '-p:FileVersion=3.7.0.1', '-p:EnablePackageValidation=false')
& dotnet build $taskProject -c Release @taskProperties '-p:GeneratePackageOnBuild=false'
if ($LASTEXITCODE) { throw 'Patched dependency build failed' }
& dotnet pack $taskProject -c Release --no-build @taskProperties -o (Join-Path $taskRoot 'packages')
if ($LASTEXITCODE) { throw 'Patched dependency pack failed' }
Write-Output "Patched source retained at: $WorkDirectory"
