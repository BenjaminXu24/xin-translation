param([switch]$Portable)
$ErrorActionPreference = 'Stop'
 $originalDirectory = Get-Location
 $environmentNames = @('DOTNET_CLI_HOME','DOTNET_CLI_TELEMETRY_OPTOUT','DOTNET_GENERATE_ASPNET_CERTIFICATE','NUGET_PACKAGES','APPDATA')
 $originalEnvironment = @{}
 foreach ($name in $environmentNames) { $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
 try {
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot 'tools\cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot 'tools\packages'
# Build-only redirection prevents reading unrelated user NuGet configuration.
$env:APPDATA = Join-Path $PSScriptRoot 'tools\build-appdata'
New-Item -ItemType Directory -Path $env:APPDATA -Force | Out-Null
$localSdk = Join-Path $PSScriptRoot 'tools\dotnet\dotnet.exe'
$sdk = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
& $sdk restore Suiyi.sln --configfile NuGet.Config --disable-parallel -m:1
if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
& $sdk build Suiyi.sln -c Release --no-restore -m:1
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
& $sdk run --project tests/Suiyi.Tests -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
if ($Portable) {
    [xml]$project = Get-Content -LiteralPath src/Suiyi.App/Suiyi.App.csproj
    $version = [string]$project.Project.PropertyGroup.Version
    $publishDirectory = "artifacts/Suiyi-v$version-win-x64"
    & $sdk publish src/Suiyi.App -c Release -r win-x64 --self-contained true -m:1 "-p:RestoreConfigFile=$PSScriptRoot\tools\NuGet.Publish.Config" -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed' }
    Copy-Item -LiteralPath README.md -Destination (Join-Path $publishDirectory 'README.md')
    Copy-Item -LiteralPath docs -Destination $publishDirectory -Recurse -Force
    & (Join-Path $PSScriptRoot 'tools\Copy-PortableNotices.ps1') -PublishDirectory (Join-Path $PSScriptRoot $publishDirectory)
    Compress-Archive -LiteralPath $publishDirectory -DestinationPath "artifacts/Suiyi-v$version-win-x64.zip" -Force
}
 } finally {
     foreach ($name in $environmentNames) {
         if ($null -eq $originalEnvironment[$name]) { Remove-Item -LiteralPath ('Env:' + $name) -ErrorAction SilentlyContinue }
         else { [Environment]::SetEnvironmentVariable($name, $originalEnvironment[$name], 'Process') }
     }
     Set-Location -LiteralPath $originalDirectory.Path
 }
