$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$directory=Join-Path $repo 'build/storage-tests'
$null=New-Item -ItemType Directory -Path $directory -Force
$exe=Join-Path $directory 'DataLocationTests.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe') /nologo /platform:x64 /reference:System.Core.dll /reference:System.Xml.dll ('/out:'+$exe) (Join-Path $repo 'Shared/DataLocation.cs') (Join-Path $repo 'Tests/DataLocationTests.cs')
if($LASTEXITCODE -ne 0){throw 'Storage tests compilation failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Storage tests failed'}
