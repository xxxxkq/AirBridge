param([string]$Release = '.\build\dev')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$releasePath = (Resolve-Path -LiteralPath $Release).Path
$testPath = Join-Path $repo 'build/tests/offline'
$null = New-Item -ItemType Directory -Path $testPath -Force
foreach ($name in @('AirBridge.Core.dll','AirBridge.Routing.dll','NAudio.Core.dll','NAudio.Wasapi.dll')) {
    Copy-Item -LiteralPath (Join-Path $releasePath $name) -Destination $testPath -Force
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$netstandard = Get-ChildItem (Join-Path $env:WINDIR 'Microsoft.NET/assembly/GAC_MSIL/netstandard') -Recurse -Filter netstandard.dll | Select-Object -First 1 -ExpandProperty FullName
if (-not $netstandard) { throw 'Missing .NET Framework netstandard facade' }
$refs = @('/reference:System.Core.dll','/reference:System.Web.Extensions.dll',('/reference:'+$netstandard))
foreach ($name in @('AirBridge.Core.dll','AirBridge.Routing.dll','NAudio.Core.dll','NAudio.Wasapi.dll')) {
    $refs += '/reference:' + (Join-Path $testPath $name)
}
foreach ($name in @('PhoneAudioQualityTests','PhoneJitterMeasuredTests','PlaybackTuningTests','JitterTests','OutputPreferenceTests')) {
    $exe = Join-Path $testPath ($name+'.exe')
    $sources = @((Join-Path $repo ('Tests/'+$name+'.cs')))
    if ($name -eq 'OutputPreferenceTests') { $sources += Join-Path $repo 'Tests/DefaultRoute.Tests.cs' }
    & $compiler /nologo /target:exe /platform:x64 @refs ('/out:'+$exe) @sources
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $name" }
    Copy-Item -LiteralPath (Join-Path $releasePath 'AirBridge.exe.config') -Destination ($exe+'.config') -Force
    & $exe
    if ($LASTEXITCODE -ne 0) { throw "Test failed: $name" }
}
Copy-Item -LiteralPath (Join-Path $releasePath 'AirBridge.exe.config') -Destination $testPath -Force
& (Join-Path $repo 'Tests/IPhonePipeline.ps1') -Release $testPath
Write-Output 'PASS: offline suites completed; no hardware playback or system output changes'
