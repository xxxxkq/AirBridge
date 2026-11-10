param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version='3.0.65',[string]$OutputDirectory='')
$ErrorActionPreference='Stop'
$release=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path (Split-Path $PSScriptRoot) ('AirBridge-'+$Version)}
$null=New-Item -ItemType Directory -Path $release -Force
& (Join-Path $PSScriptRoot 'BuildNative.ps1') -Release $release
if($LASTEXITCODE -ne 0){throw 'Native capture build failed'}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs=@('Windows.Devices','Windows.Foundation','Windows.Storage','Windows.Media')|ForEach-Object {'/reference:'+(Join-Path $env:WINDIR "System32\WinMetadata\$_.winmd")}
$refs+=@('System.Runtime','System.Runtime.InteropServices.WindowsRuntime','System.Runtime.WindowsRuntime')|ForEach-Object {Get-ChildItem (Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_MSIL\$_") -Recurse -Filter *.dll|Select-Object -First 1|ForEach-Object {'/reference:'+$_.FullName}}
& $compiler /nologo /target:library /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /out:"$release\AirBridge.Core.dll" @refs "$PSScriptRoot\Core\Devices.cs" "$PSScriptRoot\Platform\Endpoints.cs" "$PSScriptRoot\Platform\BluetoothMonitor.cs" "$PSScriptRoot\Platform\WearState.cs" "$PSScriptRoot\Platform\AudioControl.cs" "$PSScriptRoot\Platform\DesktopFeatures.cs" "$PSScriptRoot\Platform\PhoneBattery.cs" "$PSScriptRoot\Platform\HeadsetConnection.cs" "$PSScriptRoot\Platform\PhoneSessionVolume.cs" "$PSScriptRoot\Platform\PhoneRelease.cs"
if($LASTEXITCODE -ne 0){throw 'Core build failed'}
Copy-Item "$PSScriptRoot/Dependencies/NAudio.Core.dll","$PSScriptRoot/Dependencies/NAudio.Wasapi.dll" $release -Force
$netstandard=Get-ChildItem (Join-Path $env:WINDIR 'Microsoft.NET/assembly/GAC_MSIL/netstandard') -Recurse -Filter netstandard.dll|Select-Object -First 1 -ExpandProperty FullName
& $compiler /nologo /target:library /reference:System.Xml.dll /reference:"$release\AirBridge.Core.dll" /reference:$netstandard /reference:"$release\NAudio.Core.dll" /reference:"$release\NAudio.Wasapi.dll" /out:"$release\AirBridge.Routing.dll" @refs "$PSScriptRoot\Platform\RouteEngine.cs" "$PSScriptRoot\Platform\Graph.cs" "$PSScriptRoot\Platform\PcmPassThrough.cs" "$PSScriptRoot\Platform\PcAudioControl.cs" "$PSScriptRoot\Platform\PhoneCapture.cs" "$PSScriptRoot\Platform\PhoneJitterBuffer.cs" "$PSScriptRoot\Platform\PlaybackTuning.cs" "$PSScriptRoot\Platform\PhoneArrivalJitter.cs" "$PSScriptRoot\Platform\PhoneWorker.cs" "$PSScriptRoot\Platform\AudioDiagnostics.cs" "$PSScriptRoot\Platform\PhoneAudioQuality.cs" "$PSScriptRoot\Platform\AudioTimingTrace.cs" "$PSScriptRoot\Platform\OutputComparison.cs" "$PSScriptRoot\Platform\PhonePcmPacket.cs" "$PSScriptRoot\Platform\CaptureThreadScheduling.cs" "$PSScriptRoot\Platform\ProcessCapture.cs" "$PSScriptRoot\Platform\GraphPlan.cs" "$PSScriptRoot\Platform\GraphBlockEvaluator.cs" "$PSScriptRoot\Platform\GraphRuntime.cs" "$PSScriptRoot\Platform\BlockModules.cs" "$PSScriptRoot\Platform\NetworkAudio.cs" "$PSScriptRoot\Platform\AppOutputPolicy.cs" "$PSScriptRoot\Platform\NativeAppRoutes.cs" "$PSScriptRoot\Platform\ApplicationIdentity.cs" "$PSScriptRoot\Platform\DefaultOutputRoute.cs" "$PSScriptRoot\Platform\VirtualCableRouting.cs"
if($LASTEXITCODE -ne 0){throw 'Routing build failed'}
$wpf=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
& $compiler /nologo /target:library /reference:System.Xaml.dll /out:"$release\AirBridge.UI.dll" /reference:"$wpf\PresentationFramework.dll" /reference:"$wpf\PresentationCore.dll" /reference:"$wpf\WindowsBase.dll" "$PSScriptRoot\UI\Visuals.cs" "$PSScriptRoot\UI\Motion.cs" "$PSScriptRoot\UI\LiquidIndicator.cs" "$PSScriptRoot\UI\AudioBoard.cs" "$PSScriptRoot\UI\BoardLayout.cs"
if($LASTEXITCODE -ne 0){throw 'UI build failed'}
& $compiler /nologo /target:winexe /win32manifest:"$PSScriptRoot\app.manifest" /platform:x64 /optimize+ /reference:Microsoft.CSharp.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xaml.dll /reference:"$wpf\PresentationFramework.dll" /reference:"$wpf\PresentationCore.dll" /reference:"$wpf\WindowsBase.dll" /reference:"$release\AirBridge.Core.dll" /reference:"$release\AirBridge.Routing.dll" /reference:"$release\AirBridge.UI.dll" @refs /win32icon:"$PSScriptRoot\Assets\AirBridge.ico" /out:"$release\AirBridge.exe" @(Get-ChildItem "$PSScriptRoot\Desktop\*.cs" | ForEach-Object {$_.FullName})
if($LASTEXITCODE -ne 0){throw 'Desktop build failed'}
foreach($dir in @('UI','Assets')){$null=New-Item -ItemType Directory -Path (Join-Path $release $dir) -Force}
Copy-Item "$PSScriptRoot\UI\Shell.xaml" "$release\UI\Shell.xaml" -Force
Copy-Item "$PSScriptRoot\Assets\AirBridge.ico" "$release\Assets\AirBridge.ico" -Force
Copy-Item "$PSScriptRoot\THIRD-PARTY-NOTICES.txt" $release -Force
@{Version=$Version;Architecture='x64';Runtime='C# + WPF';DriverIncluded=$false;PowerShellRuntime=$false;BuiltUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content (Join-Path $release 'build-info.json') -Encoding UTF8
Copy-Item "$PSScriptRoot\README.txt","$PSScriptRoot\AirBridge.exe.config","$PSScriptRoot\RTP-L16-example.sdp" $release -Force
Write-Host "Built $release"







