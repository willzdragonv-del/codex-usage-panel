param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
& $dotnetCommand run --project (Join-Path $projectRoot 'tests\CodexUsage.Tests') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
& $dotnetCommand run --project (Join-Path $projectRoot 'tests\CodexUsage.Smoke') -c Release -- --floating
if ($LASTEXITCODE -ne 0) { throw 'Floating UI tests failed.' }
$publishArguments = @('publish', (Join-Path $projectRoot 'src\CodexUsage.Desktop'), '-c', 'Release', '-r', 'win-x64', '--self-contained', $SelfContained.IsPresent.ToString().ToLowerInvariant(), '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-o', (Join-Path $projectRoot 'dist'))
& $dotnetCommand @publishArguments
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Write-Host ('Ready: ' + (Join-Path $projectRoot 'dist\CodexUsage.exe'))
