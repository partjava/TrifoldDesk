param([switch]$Test, [string]$Version = '0.10.17')
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:MSBuildEnableWorkloadResolver = 'false'
if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '请先安装 .NET 8 或更新的 SDK。' }
if ($Test) {
    & dotnet run --project tests/TrifoldDesk.Tests --disable-build-servers
    if ($LASTEXITCODE -ne 0) { throw 'Core 测试失败，停止发布。' }
}
$taskOutput = Join-Path $PSScriptRoot ('dist\v' + $Version)
& dotnet publish src/TrifoldDesk.App -c Release --self-contained false -o $taskOutput --disable-build-servers
if ($LASTEXITCODE -ne 0) { throw '构建或发布失败，请查看上方输出。' }
Write-Host "发布完成：$(Join-Path $taskOutput 'TrifoldDesk.exe')" -ForegroundColor Cyan
if ($Test) {
    $taskVerification = Join-Path $PSScriptRoot ('verification\run-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
    $taskProcess = Start-Process -FilePath (Join-Path $taskOutput 'TrifoldDesk.exe') -ArgumentList @('--self-test', '--data-dir', ('"' + $taskVerification + '"')) -WindowStyle Hidden -PassThru -Wait
    if (Test-Path -LiteralPath (Join-Path $taskVerification 'self-test.txt')) { Get-Content -LiteralPath (Join-Path $taskVerification 'self-test.txt') }
    if ($taskProcess.ExitCode -ne 0) { throw "窗口集成检查失败。日志：$taskVerification" }
    Write-Host "测试结果：$taskVerification" -ForegroundColor Green
}
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'current-version.txt'), $Version)
