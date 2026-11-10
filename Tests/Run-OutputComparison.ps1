param(
 [Parameter(Mandatory=$true)][string]$Release,
 [Parameter(Mandatory=$true)][string]$SpeakerId,
 [Parameter(Mandatory=$true)][string]$HeadphoneId,
 [Parameter(Mandatory=$true)][string]$ResultDirectory,
 [switch]$Run,
 [switch]$WithoutEtw
)
$ErrorActionPreference='Stop'
$releasePath=[IO.Path]::GetFullPath($Release)
$resultPath=[IO.Path]::GetFullPath($ResultDirectory)
$exe=Join-Path $releasePath 'AirBridge.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'AirBridge.exe not found'}
if((Test-Path -LiteralPath $resultPath) -and @(Get-ChildItem -LiteralPath $resultPath -Force).Count){throw 'Choose a new empty result directory'}
$config=@{SpeakerId=$SpeakerId;HeadphoneId=$HeadphoneId;SettleSeconds=15;SampleSeconds=60;ResultDirectory=$resultPath}
if(!$Run){
 $config|ConvertTo-Json
 Write-Output 'PLAN ONLY. No application, tracing session, audio endpoint or setting changed. Run requires both phones playing, phone buffering OFF, all existing AirBridge processes exited. Four phases take 5 minutes, plus startup and restoration. ETW requires an elevated shell; -WithoutEtw cannot diagnose CPU/DPC scheduling.'
 return
}
if(Get-Process -Name AirBridge -ErrorAction SilentlyContinue){throw 'Exit the running AirBridge once before starting this controlled run. No process will be killed.'}
$settingsPath=Join-Path $env:LOCALAPPDATA 'AirBridge\settings.json'
if(!(Test-Path -LiteralPath $settingsPath)){throw 'Existing AirBridge settings required'}
$settings=Get-Content -LiteralPath $settingsPath -Raw|ConvertFrom-Json
if($settings.PhoneAntiStutter -ne $false){throw 'Phone buffering must already be OFF; settings have not been changed'}
if(!$WithoutEtw){
 $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
 if(!(New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'ETW requires elevation. No changes made.'}
 $null=Get-Command wpr.exe -ErrorAction Stop
}
# Keep config outside the empty result directory. Use a unique trace instance; never cancel somebody else's recording.
$parent=Split-Path $resultPath
$null=New-Item -ItemType Directory -Path $parent -Force
$configPath=Join-Path $parent ('comparison-config-'+[guid]::NewGuid().ToString('N')+'.json')
$config|ConvertTo-Json|Set-Content -LiteralPath $configPath -Encoding UTF8
$instance='AirBridgeCompare-'+[guid]::NewGuid().ToString('N')
$tracing=$false
try{
 if(!$WithoutEtw){
  & wpr.exe -start GeneralProfile -start Audio -start DotNET -filemode -instancename $instance
  if($LASTEXITCODE -ne 0){throw 'WPR could not start; AirBridge was not launched'}
  $tracing=$true
 }
 $process=Start-Process -FilePath $exe -ArgumentList @('--output-compare',('"'+$configPath+'"')) -WindowStyle Hidden -PassThru
 $watch=[Diagnostics.Stopwatch]::StartNew()
 $done=Join-Path $resultPath 'completed.json'
 while(!(Test-Path -LiteralPath $done)){
  if($process.HasExited){throw 'AirBridge exited before comparison completion; inspect launcher-error.log'}
  if($watch.Elapsed.TotalMinutes -ge 8){throw 'Comparison did not finish in time. AirBridge was not killed. Use Stop to cancel and restore; do not restart Bluetooth.'}
  Start-Sleep -Milliseconds 500
 }
 # Child timing writers observe trace-stop without stopping their audio channels.
 Start-Sleep -Seconds 2
 Get-Content -LiteralPath $done -Raw
 Copy-Item -LiteralPath $settingsPath -Destination (Join-Path $resultPath 'settings-after.json')
 Get-FileHash -LiteralPath $exe,(Join-Path $releasePath 'AirBridge.Routing.dll')|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $resultPath 'binary-hashes.json') -Encoding UTF8
}finally{
 if($process -and !(Test-Path -LiteralPath (Join-Path $resultPath 'completed.json')) -and !$process.HasExited){
  $null=New-Item -ItemType Directory -Path $resultPath -Force
  Set-Content -LiteralPath (Join-Path $resultPath 'comparison-cancel') -Value 'cancel and restore' -Encoding UTF8
  $restoreWatch=[Diagnostics.Stopwatch]::StartNew()
  while(!(Test-Path -LiteralPath (Join-Path $resultPath 'completed.json')) -and !$process.HasExited -and $restoreWatch.Elapsed.TotalSeconds -lt 30){Start-Sleep -Milliseconds 250}
  if(!(Test-Path -LiteralPath (Join-Path $resultPath 'completed.json'))){Write-Warning 'Restore is still pending. AirBridge was not killed; inspect its status before further operations.'}
 }
 if($tracing){
  $null=New-Item -ItemType Directory -Path $resultPath -Force
  & wpr.exe -stop (Join-Path $resultPath 'scheduling.etl') 'AirBridge output comparison; no audio recording' -skipPdbGen -instancename $instance
  if($LASTEXITCODE -ne 0){Write-Warning "WPR stop failed for owned instance $instance. Do not cancel unrelated sessions."}
 }
}
