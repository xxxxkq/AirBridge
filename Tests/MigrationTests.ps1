param([string]$Release,[string]$LegacySettings)
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs=@('Windows.Devices','Windows.Foundation','Windows.Media')|ForEach-Object {'/reference:'+(Join-Path $env:WINDIR "System32\WinMetadata\$_.winmd")}
$refs+=@('System.Runtime','System.Runtime.InteropServices.WindowsRuntime','System.Runtime.WindowsRuntime')|ForEach-Object {Get-ChildItem (Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_MSIL\$_") -Recurse -Filter *.dll|Select-Object -First 1|ForEach-Object {'/reference:'+$_.FullName}}
$wpf=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
& $compiler /nologo /target:exe /platform:x64 /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:"$wpf\PresentationFramework.dll" /reference:"$wpf\PresentationCore.dll" /reference:"$wpf\WindowsBase.dll" /reference:"$Release\AirBridge.exe" /reference:"$Release\AirBridge.Core.dll" /reference:"$Release\AirBridge.UI.dll" /reference:"$Release\AirBridge.Routing.dll" @refs /out:"$Release\AirBridge.MigrationTests.exe" "$PSScriptRoot\MigrationTests.cs"
if($LASTEXITCODE -ne 0){throw 'Migration tests compilation failed'}
Copy-Item "$Release\AirBridge.exe.config" "$Release\AirBridge.MigrationTests.exe.config" -Force
if([string]::IsNullOrWhiteSpace($LegacySettings)){& "$Release\AirBridge.MigrationTests.exe"}else{& "$Release\AirBridge.MigrationTests.exe" $LegacySettings}
if($LASTEXITCODE -ne 0){throw 'Migration tests failed'}
