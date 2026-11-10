param([string]$DevelopmentDirectory='.\build\dev',[string]$ReleaseDirectory='.\build\release-runtime',[string]$Bundle='.\artifacts\single-file\AirBridge.exe')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$dev=(Resolve-Path -LiteralPath $DevelopmentDirectory).Path
$release=(Resolve-Path -LiteralPath $ReleaseDirectory).Path
$bundlePath=(Resolve-Path -LiteralPath $Bundle).Path
$tests=Join-Path $repo 'build/variant-tests'
$null=New-Item -ItemType Directory -Path $tests -Force
$exe=Join-Path $tests 'BuildVariantTests.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe') /nologo /platform:x64 /reference:System.Core.dll ('/out:'+$exe) (Join-Path $repo 'Tests/BuildVariantTests.cs')
if($LASTEXITCODE -ne 0){throw 'Build variant test compilation failed'}
Copy-Item -LiteralPath (Join-Path $repo 'AirBridge.exe.config') -Destination ($exe+'.config') -Force
& $exe runtime $dev Development
if($LASTEXITCODE -ne 0){throw 'Development isolation test failed'}
& $exe runtime $release Release
if($LASTEXITCODE -ne 0){throw 'Release isolation test failed'}
& $exe bundle $bundlePath (Join-Path $tests 'cache')
if($LASTEXITCODE -ne 0){throw 'Single-file test failed'}
Write-Output 'PASS build isolation and single-file checks; no UI playback or system changes'
