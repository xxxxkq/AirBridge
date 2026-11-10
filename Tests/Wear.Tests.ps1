param([string]$Release=(Join-Path (Split-Path $PSScriptRoot -Parent) '..\AirBridge-3.0.23'))
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs=@('Windows.Foundation','Windows.Media')|ForEach-Object {'/reference:'+(Join-Path $env:WINDIR "System32\WinMetadata\$_.winmd")}
$refs+=@('System.Runtime','System.Runtime.InteropServices.WindowsRuntime','System.Runtime.WindowsRuntime')|ForEach-Object {Get-ChildItem (Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_MSIL\$_") -Recurse -Filter *.dll|Select-Object -First 1|ForEach-Object {'/reference:'+$_.FullName}}
& $compiler /nologo /target:exe /platform:x64 /reference:System.Core.dll /reference:"$Release\AirBridge.Core.dll" @refs /out:"$Release\AirBridge.WearTests.exe" "$PSScriptRoot\Wear.Tests.cs" "$PSScriptRoot\..\Desktop\WearPlayback.cs"
if($LASTEXITCODE -ne 0){throw 'Wear tests compilation failed'}
& "$Release\AirBridge.WearTests.exe"
if($LASTEXITCODE -ne 0){throw 'Wear tests failed'}
