param([string]$RuntimeDirectory='.\build\dev')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$runtime=(Resolve-Path -LiteralPath $RuntimeDirectory).Path
$dir=Join-Path $repo 'build/output-formats-live'
$null=New-Item -ItemType Directory -Path $dir -Force
foreach($file in @('AirBridge.Core.dll','AirBridge.Routing.dll','NAudio.Core.dll','NAudio.Wasapi.dll')){Copy-Item -LiteralPath (Join-Path $runtime $file) -Destination $dir -Force}
$net=Get-ChildItem (Join-Path $env:WINDIR 'Microsoft.NET/assembly/GAC_MSIL/netstandard') -Recurse -Filter netstandard.dll|Select-Object -First 1 -ExpandProperty FullName
$refs=@('/reference:System.Core.dll',('/reference:'+$net))
foreach($file in @('AirBridge.Core.dll','AirBridge.Routing.dll','NAudio.Core.dll','NAudio.Wasapi.dll')){$refs+='/reference:'+(Join-Path $dir $file)}
$exe=Join-Path $dir 'OutputFormatsLive.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe') /nologo /platform:x64 @refs ('/out:'+$exe) (Join-Path $repo 'Tests/OutputFormatsLive.cs')
if($LASTEXITCODE -ne 0){throw 'Output format probe compilation failed'}
Copy-Item -LiteralPath (Join-Path $repo 'AirBridge.exe.config') -Destination ($exe+'.config') -Force
& $exe
if($LASTEXITCODE -ne 0){throw 'Some output formats failed; inspect per-device results'}
