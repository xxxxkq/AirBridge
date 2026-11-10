param([string]$Release='.\build\release-runtime')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$runtime=(Resolve-Path -LiteralPath $Release).Path
$dir=Join-Path $repo 'build/updater-tests'
$null=New-Item -ItemType Directory -Path $dir -Force
$exe=Join-Path $dir 'UpdaterTests.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe') /nologo /platform:x64 /reference:System.Core.dll /reference:System.Web.Extensions.dll ('/out:'+$exe) (Join-Path $repo 'Tests/UpdaterTests.cs')
if($LASTEXITCODE -ne 0){throw 'Updater tests compilation failed'}
Copy-Item -LiteralPath (Join-Path $repo 'AirBridge.exe.config') -Destination ($exe+'.config') -Force
& $exe $runtime
if($LASTEXITCODE -ne 0){throw 'Updater tests failed'}
