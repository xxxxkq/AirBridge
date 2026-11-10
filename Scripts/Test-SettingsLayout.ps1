param([string]$DevelopmentDirectory='.\build\dev')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$dev=(Resolve-Path -LiteralPath $DevelopmentDirectory).Path
$dir=Join-Path $repo 'build/settings-layout'
$null=New-Item -ItemType Directory -Path $dir -Force
foreach($file in @('AirBridge.exe','AirBridge.exe.config','AirBridge.Core.dll','AirBridge.Routing.dll','AirBridge.UI.dll','AirBridge.Capture.dll','NAudio.Core.dll','NAudio.Wasapi.dll','build-info.json')){Copy-Item -LiteralPath (Join-Path $dev $file) -Destination $dir -Force}
foreach($folder in @('UI','Assets')){$null=New-Item -ItemType Directory -Path (Join-Path $dir $folder) -Force}
Copy-Item -LiteralPath (Join-Path $dev 'UI/Shell.xaml') -Destination (Join-Path $dir 'UI/Shell.xaml') -Force
Copy-Item -LiteralPath (Join-Path $dev 'Assets/AirBridge.ico') -Destination (Join-Path $dir 'Assets/AirBridge.ico') -Force
$wpf=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/WPF'
$refs=@('System.Core.dll','System.Xaml.dll')|ForEach-Object {'/reference:'+$_}
foreach($file in @('AirBridge.exe','AirBridge.Core.dll','AirBridge.Routing.dll','AirBridge.UI.dll')){$refs+='/reference:'+(Join-Path $dir $file)}
foreach($file in @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll')){$refs+='/reference:'+(Join-Path $wpf $file)}
$exe=Join-Path $dir 'AirBridge.MigrationTests.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe') /nologo /platform:x64 @refs ('/out:'+$exe) (Join-Path $repo 'Tests/SettingsCardTests.cs')
if($LASTEXITCODE -ne 0){throw 'Settings layout compilation failed'}
Copy-Item -LiteralPath (Join-Path $repo 'AirBridge.exe.config') -Destination ($exe+'.config') -Force
& $exe
if($LASTEXITCODE -ne 0){throw 'Settings layout tests failed'}
