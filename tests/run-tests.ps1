param([switch]$Windows, [switch]$UiSmoke)
$ErrorActionPreference = 'Stop'
$suiyiRoot = Split-Path -Parent $PSScriptRoot
$startingLocation = Get-Location
$environmentNames = @('APPDATA', 'DOTNET_CLI_HOME', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_GENERATE_ASPNET_CERTIFICATE', 'NUGET_PACKAGES')
$savedEnvironment = @{}
foreach ($name in $environmentNames) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    Set-Location -LiteralPath $suiyiRoot
    $env:DOTNET_CLI_HOME = Join-Path $suiyiRoot 'tools\cli-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:NUGET_PACKAGES = Join-Path $suiyiRoot 'tools\packages'
    # Build-only configuration: do not load unrelated per-user NuGet configuration.
    $env:APPDATA = Join-Path $suiyiRoot 'tools\build-appdata'
    New-Item -ItemType Directory -Path $env:APPDATA -Force | Out-Null
    $localSdk = Join-Path $suiyiRoot 'tools\dotnet\dotnet.exe'
    $sdk = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
    # Isolated output/intermediate paths allow checks while the normal app build runs.
    $restoreConfig = Join-Path $suiyiRoot 'NuGet.Config'
    & $sdk build tests/Suiyi.Tests/Suiyi.Tests.csproj -c Release -m:1 --artifacts-path tests/artifacts "-p:RestoreConfigFile=$restoreConfig"
    if ($LASTEXITCODE -ne 0) { throw 'Test project build failed' }
    $testDll = Join-Path $suiyiRoot 'tests\artifacts\bin\Suiyi.Tests\release\Suiyi.Tests.dll'
    if ($UiSmoke) { & $sdk $testDll --ui-smoke }
    elseif ($Windows) { & $sdk $testDll --windows }
    else { & $sdk $testDll }
    if ($LASTEXITCODE -ne 0) { throw 'One or more assertions failed' }
}
finally {
    foreach ($name in $environmentNames) {
        if ($null -eq $savedEnvironment[$name]) { Remove-Item -LiteralPath ('Env:' + $name) -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
    }
    Set-Location -LiteralPath $startingLocation.Path
}
