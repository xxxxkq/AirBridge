param([Parameter(Mandatory=$true)][string]$Release)
$ErrorActionPreference='Stop'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
foreach($test in @('OutputComparisonTests','TimingTraceTests')){
 & $csc /nologo /target:exe /platform:x64 /reference:"$Release\AirBridge.Routing.dll" /out:"$Release\$test.exe" "$PSScriptRoot\$test.cs"
 if($LASTEXITCODE -ne 0){throw 'Compilation failed'}
 Copy-Item "$Release\AirBridge.exe.config" "$Release\$test.exe.config" -Force
 & "$Release\$test.exe"
 if($LASTEXITCODE -ne 0){throw "$test failed"}
}
$trace=Join-Path $Release ('trace-test-'+[guid]::NewGuid().ToString('N'))
& "$Release\TimingTraceTests.exe" $trace
if($LASTEXITCODE -ne 0){throw 'Enabled trace test failed'}
