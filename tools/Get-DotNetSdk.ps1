$ErrorActionPreference = 'Stop'
$toolsRoot = $PSScriptRoot
$sdkDirectory = Join-Path $toolsRoot 'dotnet'
if (Test-Path -LiteralPath $sdkDirectory) {
    throw 'tools/dotnet already exists. Inspect it first; this script will not overwrite it.'
}
$metadataPath = Join-Path $toolsRoot 'releases.json'
Invoke-WebRequest -Uri 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json' -OutFile $metadataPath
$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
$sdk = $metadata.releases | ForEach-Object { $_.sdks } | Where-Object { $_.version -eq '10.0.401' } | Select-Object -First 1
if ($null -eq $sdk) { throw 'The pinned SDK was not found in official release metadata.' }
$asset = $sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.url.EndsWith('.zip') } | Select-Object -First 1
if ($null -eq $asset) { throw 'The official Windows x64 SDK archive was not found.' }
$archivePath = Join-Path $toolsRoot 'dotnet-sdk.zip'
Invoke-WebRequest -Uri $asset.url -OutFile $archivePath
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA512).Hash -ne $asset.hash) { throw 'SDK SHA512 mismatch; extraction stopped.' }
Expand-Archive -LiteralPath $archivePath -DestinationPath $sdkDirectory
Write-Output 'Verified Microsoft .NET SDK 10.0.401 is available in tools/dotnet.'
