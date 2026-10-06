param([string]$CompilerPath, [string]$Version)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
if (!$Version) { $Version = (Get-Content -LiteralPath current-version.txt -Raw).Trim() }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw '版本号必须为 x.y.z。' }
if (!$CompilerPath) { $CompilerPath = Join-Path $PSScriptRoot '.tools\inno\ISCC.exe' }
if (!(Test-Path -LiteralPath $CompilerPath)) { throw '请安装 Inno Setup 6.7 或更新版本，通过 -CompilerPath 指定 ISCC.exe。' }
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& dotnet publish src/TrifoldDesk.App -c Release -r win-x64 --self-contained true --source https://api.nuget.org/v3/index.json "-p:Version=$Version" -o "dist/installer-app-v$Version" --disable-build-servers
if ($LASTEXITCODE -ne 0) { throw '自包含发布失败。' }
& $CompilerPath /Q "/DAppVersion=$Version" installer/TrifoldDesk.iss
if ($LASTEXITCODE -ne 0) { throw '安装包编译失败。' }
Write-Host "安装包：dist/installers/TrifoldDesk-Setup-$Version-win-x64.exe"
