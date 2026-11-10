param([Parameter(Mandatory=$true)][string]$Release)
$ErrorActionPreference='Stop'
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if(-not (Test-Path -LiteralPath $vswhere)){throw 'Install Visual Studio Build Tools with Desktop development with C++ and a Windows SDK to build native capture.'}
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(-not $vs){throw 'No Visual Studio C++ x64 toolchain found.'}
$obj=Join-Path $PSScriptRoot 'build/native'
$null=New-Item -ItemType Directory -Path $obj -Force
$dev=Join-Path $vs 'Common7/Tools/VsDevCmd.bat'
$batch=Join-Path $obj 'compile.cmd'
@"
@echo off
chcp 65001 >nul
call "$dev" -arch=x64 -host_arch=x64 >nul
if errorlevel 1 exit /b %errorlevel%
cl /nologo /LD /EHsc /std:c++17 /O2 /MT "$PSScriptRoot\Platform\ProcessCapture.cpp" /Fo"$obj\ProcessCapture.obj" /link /IMPLIB:"$obj\ProcessCapture.lib" /OUT:"$Release\AirBridge.Capture.dll" Mmdevapi.lib Ole32.lib
exit /b %errorlevel%
"@ | ForEach-Object {[IO.File]::WriteAllText($batch,($_ -replace "\r?\n","`r`n"),[Text.UTF8Encoding]::new($false))}
& cmd.exe /d /c "`"$batch`""
if($LASTEXITCODE -ne 0){throw 'Native capture build failed.'}
