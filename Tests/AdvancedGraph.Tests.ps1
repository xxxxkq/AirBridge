param([string]$Release,[switch]$NetworkOnly)
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $Release 'AirBridge.Core.dll');Add-Type -Path (Join-Path $Release 'NAudio.Core.dll');Add-Type -Path (Join-Path $Release 'NAudio.Wasapi.dll');Add-Type -Path (Join-Path $Release 'AirBridge.Routing.dll')
$net=Get-ChildItem "$env:WINDIR/Microsoft.NET/assembly/GAC_MSIL/netstandard" -Recurse -Filter netstandard.dll|Select-Object -First 1 -ExpandProperty FullName
Add-Type -Path (Join-Path $PSScriptRoot 'AdvancedGraph.Tests.cs') -ReferencedAssemblies @('System.Core.dll',(Join-Path $Release 'NAudio.Core.dll'),(Join-Path $Release 'AirBridge.Routing.dll'),$net)
if($NetworkOnly){[AdvancedGraphTests]::Network()}else{[AdvancedGraphTests]::Run((Join-Path $PSScriptRoot ('artifacts/'+[Guid]::NewGuid().ToString('N'))))}
