# Optional development build only; production Build.ps1 does not call this.
$ErrorActionPreference='Stop'
$out=Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $out -Force|Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$core=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..\02_AirBridge运行版本\AirBridge-3.0.22\AirBridge.Core.dll'))
& $csc /nologo /target:library /platform:x64 /optimize+ /reference:System.Core.dll "/reference:$core" "/out:$out\AirBridge.Aap.dll" "$PSScriptRoot\Protocol.cs" "$PSScriptRoot\WindowsChannel.cs" "$PSScriptRoot\NativeAdapter.cs"
if($LASTEXITCODE -ne 0){throw 'AAP build failed'}
Copy-Item $core $out -Force
& $csc /nologo /target:exe /platform:x64 "/reference:$out\AirBridge.Aap.dll" "/reference:$core" "/out:$out\AapProbe.exe" "$PSScriptRoot\Tests.cs"
if($LASTEXITCODE -ne 0){throw 'AAP test build failed'}
& "$out\AapProbe.exe" | Tee-Object -FilePath "$out\offline-tests.txt"
if($LASTEXITCODE -ne 0){throw 'AAP tests failed'}
