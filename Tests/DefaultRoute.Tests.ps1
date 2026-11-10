param([string]$Release,[switch]$Live)
$ErrorActionPreference='Stop'
foreach($dll in @('AirBridge.Core.dll','NAudio.Core.dll','NAudio.Wasapi.dll','AirBridge.Routing.dll')){Add-Type -Path (Join-Path $Release $dll)}
Add-Type -Path (Join-Path $PSScriptRoot 'DefaultRoute.Tests.cs') -ReferencedAssemblies @('System.Core.dll',(Join-Path $Release 'AirBridge.Core.dll'),(Join-Path $Release 'AirBridge.Routing.dll'))
[DefaultRouteTests]::Run((Join-Path $PSScriptRoot ('artifacts/default-'+[Guid]::NewGuid().ToString('N'))))
if(-not $Live){exit}
$before=@([AirBridge.Platform.Audio]::DefaultId(0),[AirBridge.Platform.Audio]::DefaultId(1));$comm=[AirBridge.Platform.Audio]::DefaultId(2)
$target=[AirBridge.Platform.Audio]::List() | Where-Object {$_.Active -and $_.Id -ne $before[1]} | Select-Object -First 1
if(-not $target){throw 'No alternate active endpoint available'}
$engine=New-Object AirBridge.Routing.GraphRuntime;$engine.DefaultRoutingJournal=Join-Path $Release 'test-default-output-recovery.xml'
$edge=New-Object AirBridge.Routing.GraphEdge;$edge.Source='pc:system';$edge.Target='o:'+$target.Id;$edge.Gain=1
$plan=[AirBridge.Routing.GraphPlan]::Create(@($edge),@())
try{
 $engine.ApplyAsync($plan).GetAwaiter().GetResult()
 if([AirBridge.Platform.Audio]::DefaultId(0) -ne $target.Id -or [AirBridge.Platform.Audio]::DefaultId(1) -ne $target.Id){throw 'Default target did not switch'}
 if($engine.SourceStarts -ne 0 -or @($engine.Snapshot()).Count -ne 1){throw 'Native route started duplicate capture/render'}
 $engine.ApplyAsync($plan).GetAwaiter().GetResult();$engine.StopAsync().GetAwaiter().GetResult()
 if([AirBridge.Platform.Audio]::DefaultId(0) -ne $before[0] -or [AirBridge.Platform.Audio]::DefaultId(1) -ne $before[1]){throw 'Stop did not restore original roles'}
 $engine.ApplyAsync($plan).GetAwaiter().GetResult();$engine.Dispose();$engine=$null
 if([AirBridge.Platform.Audio]::DefaultId(0) -ne $before[0] -or [AirBridge.Platform.Audio]::DefaultId(1) -ne $before[1] -or [AirBridge.Platform.Audio]::DefaultId(2) -ne $comm){throw 'Exit restore or communications role failed'}
 'PASS: live switch, zero capture/render, reapply, Stop restore, Dispose restore, communications unchanged'
}finally{if($engine){$engine.Dispose()};[AirBridge.Platform.OutputSwitch]::RestoreRoles($before[0],$before[1])}
