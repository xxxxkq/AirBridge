param([Parameter(Mandatory=$true)][string]$Release,[switch]$StatsOnly)
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$netstandard=Get-ChildItem (Join-Path $env:WINDIR 'Microsoft.NET/assembly/GAC_MSIL/netstandard') -Recurse -Filter netstandard.dll|Select-Object -First 1 -ExpandProperty FullName
& $compiler /nologo /target:exe /platform:x64 /reference:$netstandard /reference:"$Release\AirBridge.Routing.dll" /reference:"$Release\NAudio.Core.dll" /out:"$Release\IPhonePipelineTests.exe" "$PSScriptRoot\IPhonePipeline.cs"
if($LASTEXITCODE -ne 0){throw 'Pipeline tests compilation failed'}
Copy-Item "$Release\AirBridge.exe.config" "$Release\IPhonePipelineTests.exe.config" -Force
if($StatsOnly){& "$Release\IPhonePipelineTests.exe" stats-only}else{& "$Release\IPhonePipelineTests.exe"}
if($LASTEXITCODE -ne 0){throw 'Pipeline tests failed'}
