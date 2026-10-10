param([Parameter(Mandatory=$true)][string]$PublishDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$licenses = Join-Path $PublishDirectory 'licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
$runtime = (Get-Content -LiteralPath (Join-Path $PublishDirectory 'Suiyi.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks
$coreVersion = ($runtime | Where-Object {$_.name -eq 'Microsoft.NETCore.App'}).version
$desktopVersion = ($runtime | Where-Object {$_.name -eq 'Microsoft.WindowsDesktop.App'}).version
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $projectRoot 'tools\packages' }
Copy-Item -LiteralPath (Join-Path $packages "microsoft.netcore.app.runtime.win-x64\$coreVersion\LICENSE.TXT") -Destination (Join-Path $licenses 'NETCore-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $packages "microsoft.netcore.app.runtime.win-x64\$coreVersion\THIRD-PARTY-NOTICES.TXT") -Destination (Join-Path $licenses 'NETCore-ThirdPartyNotices.txt')
Copy-Item -LiteralPath (Join-Path $packages "microsoft.windowsdesktop.app.runtime.win-x64\$desktopVersion\LICENSE") -Destination (Join-Path $licenses 'WindowsDesktop-LICENSE.txt')
$sdkVersion = (Get-Content -LiteralPath (Join-Path $projectRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$desktopNotices = Join-Path $projectRoot "tools\dotnet\sdk\$sdkVersion\Sdks\Microsoft.NET.Sdk.WindowsDesktop\THIRD-PARTY-NOTICES.TXT"
if (-not (Test-Path -LiteralPath $desktopNotices)) {
    $sdkRoot = (& dotnet --list-sdks | Where-Object {$_ -match "^$([regex]::Escape($sdkVersion)) "} | Select-Object -First 1) -replace '^.*\[([^\]]+)\]$', '$1'
    $desktopNotices = Join-Path $sdkRoot "$sdkVersion\Sdks\Microsoft.NET.Sdk.WindowsDesktop\THIRD-PARTY-NOTICES.TXT"
}
Copy-Item -LiteralPath $desktopNotices -Destination (Join-Path $licenses 'WindowsDesktop-ThirdPartyNotices.txt')
