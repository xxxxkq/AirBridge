param([ValidateSet('Aap','VirtualAudio')][string[]]$Components=@('Aap','VirtualAudio'))
# Build and stage only. Never install/sign/trust or select audio endpoints.
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
$managed=Join-Path $workspace '01_项目源码\AirBridge.Managed'
$virtual=Join-Path $workspace '02_AirBridge运行版本\AirBridge2_49_1\VirtualAudio'
$aap=Join-Path $managed 'Optional\Aap'
$out=Join-Path $PSScriptRoot ('artifacts\'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $out -Force|Out-Null
function Run([string]$exe,[string[]]$arguments){& $exe @arguments;if($LASTEXITCODE -ne 0){throw "Failed: $exe"}}
$packages=Join-Path $virtual 'packages'
$wdk=Join-Path $packages 'Microsoft.Windows.WDK.x64.10.0.26100.6584\c'
$records=@()
foreach($component in ($Components|Select-Object -Unique)){
 $dest=Join-Path $out $component
 New-Item -ItemType Directory -Path "$dest\driver","$dest\tools","$dest\source" -Force|Out-Null
 if($component -eq 'Aap'){
  Run powershell.exe @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$aap\Build.ps1")
  Run powershell.exe @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$aap\BuildDriver.ps1")
  Copy-Item "$aap\bin\driver-candidate\NTPodsAAP.sys","$aap\bin\driver-candidate\NTPodsAAP.inf" "$dest\driver"
  Copy-Item "$aap\bin\AirBridge.Aap.dll","$aap\bin\AirBridge.Core.dll","$aap\bin\AapProbe.exe","$aap\bin\offline-tests.txt" "$dest\tools"
  Copy-Item "$aap\vendor","$aap\Driver" "$dest\source" -Recurse
  Copy-Item "$aap\*.cs","$aap\Build*.ps1","$aap\README.txt","$aap\UPSTREAM.json" "$dest\source"
  $inf='NTPodsAAP.inf';$hardware='BTHENUM\{74ec2172-0bad-4d01-8f77-997b2be0722a}'
 }else{
  Run powershell.exe @('-NoProfile','-ExecutionPolicy','Bypass','-File',"$virtual\BuildDriver.ps1")
  Run cmd.exe @('/c',"$virtual\BuildNativeTests.cmd")
  Copy-Item "$virtual\bin\driver\AirBridgeVirtual.sys","$virtual\bin\driver\AirBridgeVirtual.inf" "$dest\driver"
  $tools="$dest\tools";$release=Join-Path $workspace 'AirBridge-3.0.22'
  foreach($name in @('AirBridge.Core.dll','AirBridge.Routing.dll','NAudio.Core.dll','NAudio.Wasapi.dll')){Copy-Item (Join-Path $release $name) $tools}
  $csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
  $net=Get-ChildItem "$env:WINDIR\Microsoft.NET\assembly\GAC_MSIL\netstandard" -Recurse -Filter netstandard.dll|Select-Object -First 1 -ExpandProperty FullName
  $refs=@("/reference:$tools\AirBridge.Routing.dll","/reference:$tools\NAudio.Core.dll","/reference:$net")
  Run $csc (@('/nologo','/target:library',"/out:$tools\AirBridge.VirtualAudio.dll")+$refs+@("$virtual\VirtualAudioTransport.cs","$virtual\OfflineGraph.cs","$virtual\KernelPcmSource.cs"))
  Run $csc (@('/nologo','/target:exe',"/out:$tools\AirBridge.VirtualAudio.Tests.exe","/reference:$tools\AirBridge.VirtualAudio.dll")+$refs+@("$virtual\VirtualAudio.Tests.cs"))
  foreach($name in @('KernelPcm.Tests','KernelPcmProbe')){Run $csc @('/nologo','/target:exe',"/out:$tools\AirBridge.$name.exe","/reference:$tools\AirBridge.VirtualAudio.dll","$virtual\$name.cs")}
  Copy-Item "$virtual\bin\AirBridge.Transport.Tests.exe","$virtual\bin\AirBridge.Bridge.Tests.exe" $tools
  New-Item -ItemType Directory -Path "$tools\evidence" -Force|Out-Null
  Run "$tools\AirBridge.Transport.Tests.exe" @("$tools\evidence\packets.bin")
  Run "$tools\AirBridge.Bridge.Tests.exe" @()
  Run "$tools\AirBridge.KernelPcm.Tests.exe" @("$tools\evidence\packets.bin")
  Run "$tools\AirBridge.VirtualAudio.Tests.exe" @("$tools\evidence\packets.bin","$tools\evidence")
  foreach($dir in @('Driver','SysVad')){Copy-Item "$virtual\$dir" "$dest\source" -Recurse}
  Copy-Item "$virtual\*.cs","$virtual\*.ps1","$virtual\*.cmd","$virtual\sysvad-upstream.json" "$dest\source"
  $inf='AirBridgeVirtual.inf';$hardware='Root\AirBridgeVirtualAudio'
 }
 Run "$wdk\tools\10.0.26100.0\x64\infverif.exe" @('/w',"$dest\driver\$inf")
 Run "$wdk\bin\10.0.26100.0\x86\Inf2Cat.exe" @("/driver:$dest\driver",'/os:10_NI_X64,10_GE_X64')
 $records+=@{Id=$component;Inf="$component/driver/$inf";HardwareId=$hardware;BuildAndOfflineTests='Passed';HardwareTests='NotRun';Installed=$false}
}
@{Format=1;Version='0.3-test-candidate';CreatedUtc=[DateTime]::UtcNow.ToString('o');Architecture='x64';InstallApproved=$false;DefaultOutputTakeover=$false;Components=$records;Files=@(Get-ChildItem $out -Recurse -File|ForEach-Object {@{Path=$_.FullName.Substring($out.Length+1);SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}})}|ConvertTo-Json -Depth 6|Set-Content "$out\bundle.json" -Encoding UTF8
Copy-Item "$PSScriptRoot\Preflight.ps1","$PSScriptRoot\INSTALLATION.txt" $out
& "$PSScriptRoot\Preflight.ps1" -Bundle $out
Set-Content "$PSScriptRoot\latest-build.txt" $out -Encoding UTF8
Write-Host "Prepared only: $out"
