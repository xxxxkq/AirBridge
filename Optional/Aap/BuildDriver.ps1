# Compile only. No signing, certificate changes, driver staging or installation.
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$packages=Join-Path $workspace 'AirBridge2_49_1\VirtualAudio\packages'
$msvc='C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\14.44.35207'
$wdk=Join-Path $packages 'Microsoft.Windows.WDK.x64.10.0.26100.6584\c'
$sdk=Join-Path $packages 'microsoft.windows.sdk.cpp.10.0.26100.9457\c'
$out=Join-Path $PSScriptRoot 'bin\driver-candidate'
New-Item -ItemType Directory -Path $out -Force|Out-Null
$includes=@("$wdk\Include\10.0.26100.0\km","$wdk\Include\10.0.26100.0\km\crt","$wdk\Include\wdf\kmdf\1.15","$sdk\Include\10.0.26100.0\shared","$msvc\include")
$objects=@()
foreach($name in @('Driver','Device','L2cap','Ioctl')){
 $obj=Join-Path $out ($name+'.obj');$objects+=$obj
 & "$msvc\bin\Hostx64\x64\cl.exe" /nologo /utf-8 /c /kernel /Zl /W3 /O2 /GS /D_AMD64_ /D_WIN64 /D_WIN32_WINNT=0x0A00 /DNTDDI_VERSION=0x0A00000E /DNDEBUG @($includes|ForEach-Object {'/I'+$_}) "/Fo$obj" "$PSScriptRoot\Driver\$name.c"
 if($LASTEXITCODE -ne 0){throw "Compile failed: $name"}
}
$libs=@("$wdk\Lib\10.0.26100.0\km\x64","$wdk\Lib\wdf\kmdf\x64\1.15")
& "$msvc\bin\Hostx64\x64\link.exe" /nologo /driver /subsystem:native /entry:FxDriverEntry /machine:x64 /nodefaultlib /integritycheck /release /dynamicbase /nxcompat "/out:$out\NTPodsAAP.sys" @($libs|ForEach-Object {'/libpath:'+$_}) @objects ntoskrnl.lib hal.lib wmilib.lib BufferOverflowFastFailK.lib WdfLdr.lib WdfDriverEntry.lib
if($LASTEXITCODE -ne 0){throw 'Driver link failed'}
Copy-Item "$PSScriptRoot\Driver\NTPodsAAP.inf" $out -Force
Get-AuthenticodeSignature "$out\NTPodsAAP.sys" | Select-Object @{Name="Status";Expression={$_.Status.ToString()}},Path | ConvertTo-Json | Set-Content "$out\signature.json"
Write-Host 'Built unsigned research candidate; NOT approved for installation.'
