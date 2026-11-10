param([Parameter(Mandatory=$true)][string]$Release)
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 /reference:"$Release\AirBridge.Routing.dll" /out:"$Release\AirBridge.PhoneWorkerTests.exe" "$PSScriptRoot\PhoneWorkers.cs"
if($LASTEXITCODE -ne 0){throw 'Worker tests compilation failed'}
Copy-Item "$Release\AirBridge.exe.config" "$Release\AirBridge.PhoneWorkerTests.exe.config" -Force
& "$Release\AirBridge.PhoneWorkerTests.exe"
if($LASTEXITCODE -ne 0){throw 'Worker tests failed'}
