using System;using System.IO;using System.Linq;using System.Diagnostics;using System.Reflection;using System.Threading;using AirBridge.Routing;using AirBridge.Platform;using NAudio.Wave;using NAudio.Wave.SampleProviders;using NAudio.CoreAudioApi;
class VirtualCableLive {
 static string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"virtual-live-test");
 static void Check(bool b,string m){if(!b)throw new Exception(m);}
 static void Watch(){var t=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"AirBridge.exe")).GetType("AirBridge.Desktop.OutputRecovery",true);t.GetMethod("Ensure",BindingFlags.Public|BindingFlags.Static).Invoke(null,new object[]{root});}
 static GraphRuntime Start(string original,bool multi){var physical=Audio.List().Where(e=>e.Active&&!VirtualCableRouting.IsCablePlayback(e)).ToArray();var extra=physical.FirstOrDefault(e=>e.Id!=original&&e.Name.IndexOf("Realtek",StringComparison.OrdinalIgnoreCase)>=0);Check(!multi||extra!=null,"Need second physical output");var edges=new System.Collections.Generic.List<GraphEdge>{new GraphEdge{Source="pc:system",Target="m:gain"},new GraphEdge{Source="m:gain",Target="o:default"}};if(multi)edges.Add(new GraphEdge{Source="m:gain",Target="o:"+extra.Id});var graph=new GraphRuntime{VirtualPcEnabled=true,DefaultRoutingJournal=Path.Combine(root,"AirBridge-default-output-recovery.xml"),AppRoutingJournal=Path.Combine(root,"apps.xml"),EnsureRecoveryWatcher=Watch};try{graph.ApplyAsync(GraphPlan.Create(edges.ToArray(),new[]{new ModuleSpec{Id="m:gain",Kind="volume",Gain=.5f}})).GetAwaiter().GetResult();return graph;}catch{graph.Dispose();throw;}}
 static int Main(string[] args){Directory.CreateDirectory(root);if(args.Length>0&&args[0]=="child"){try{using(var g=Start(args[1],false)){File.WriteAllText(Path.Combine(root,"child-ready"),"ready");Thread.Sleep(60000);}return 0;}catch(Exception ex){File.WriteAllText(Path.Combine(root,"child-error"),ex.ToString());return 1;}}
 string a=Audio.DefaultId(0),b=Audio.DefaultId(1),comm=Audio.DefaultId(2);try{
 Check(Process.GetProcessesByName("AirBridge").Length==0,"Close main app before live test");
 using(var graph=Start(b,true)){
  var cable=VirtualCableRouting.Detect(Audio.List(),Audio.CaptureList());Check(Audio.DefaultId(1)==cable.Playback,"Default did not switch");
  using(var e=new MMDeviceEnumerator())using(var d=e.GetDevice(cable.Playback))using(var tone=new WasapiOut(d,AudioClientShareMode.Shared,true,100)){
   tone.Init(new SampleToWaveProvider(new SignalGenerator(48000,2){Type=SignalGeneratorType.Sin,Frequency=440,Gain=.015}));tone.Play();Thread.Sleep(1600);Check(graph.ActiveEdges().Length>=3,"No signals through multi-output effect chain");tone.Stop();
  }
  Check(graph.Snapshot().Count(s=>s.Source=="pc:system")==1,"Duplicate PC capture");Check(!graph.Snapshot().Any(s=>s.Status=="error"),"Graph errors");
  Check(graph.TryUpdateEffect(new ModuleSpec{Id="m:gain",Kind="volume",Gain=.25f}),"Live effect update failed");
  graph.StopAsync().GetAwaiter().GetResult();Check(Audio.DefaultId(0)==a&&Audio.DefaultId(1)==b,"Stop restoration failed");
 }
 // Watcher belongs to this test process and holds the recovery lock until exit;
 // crash scenario is run in a separate invocation after this process has exited.
 Check(Audio.DefaultId(2)==comm,"Communications output changed");File.WriteAllText(Path.Combine(root,"normal-result.txt"),"PASS actual PC->VB-CABLE->volume->two outputs, signal, effect update, Stop restore, communication role unchanged");return 0;
 }catch(Exception ex){File.WriteAllText(Path.Combine(root,"normal-result.txt"),"FAIL "+ex);return 1;}finally{new DefaultOutputRoute(Path.Combine(root,"AirBridge-default-output-recovery.xml")).Restore();}}
}
