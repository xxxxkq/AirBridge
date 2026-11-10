using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AirBridge.Desktop;
using AirBridge.Platform;
using AirBridge.Routing;
class MigrationTests {
 sealed class Owner:IDisposable {public int Closes;public bool Open=true,Fail;public void Dispose(){Closes++;if(Fail)throw new InvalidOperationException("close failure");Open=false;}}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Main(string[] args){Run(args).GetAwaiter().GetResult();}
 static async Task Run(string[] args){
  var desired=GraphPlan.Create(new[]{new GraphEdge{Source="p:phone",Target="o:default"},new GraphEdge{Source="a:1:10",Target="o:speaker"},new GraphEdge{Source="a:2:20",Target="o:speaker"}},new ModuleSpec[0]);
  var identities=new[]{new RememberedApp{Id="a:1:10",Path="edge.exe"},new RememberedApp{Id="a:2:20",Path="player.exe"}};
  var partial=ApplicationRouteSelection.Select(desired,identities,new[]{new ApplicationInput{Id="a:2:20",Path="player.exe"}});
  Check(partial.Edges.Length==2&&partial.PhoneIds.Single()=="phone"&&partial.NativeAppEdges.Single().Source=="a:2:20","Missing app blocked independent routes");
  Check(desired.Edges.Length==3&&desired.Edges[1].Source=="a:1:10","Filtering mutated desired graph");
  var recovered=ApplicationRouteSelection.Select(desired,identities,new[]{new ApplicationInput{Id="a:3:30",Path="edge.exe"},new ApplicationInput{Id="a:2:20",Path="player.exe"}});
  Check(recovered.Edges.Length==3&&recovered.NativeAppEdges.Any(e=>e.Source=="a:3:30"),"Restarted app not rebound");
  var ambiguous=ApplicationRouteSelection.Select(desired,identities,new[]{new ApplicationInput{Id="a:3:30",Path="edge.exe"},new ApplicationInput{Id="a:4:40",Path="edge.exe"}});
  Check(ambiguous.Edges.Length==1,"Ambiguous identity routed to wrong process");
  Check(ApplicationRouteSelection.Signature(partial.NativeAppEdges)==ApplicationRouteSelection.Signature(partial.NativeAppEdges.Reverse().ToArray()),"Unstable route signature");
  var effectGraph=GraphPlan.Create(new[]{new GraphEdge{Source="a:1:10",Target="m:eq"},new GraphEdge{Source="m:eq",Target="m:gain"},new GraphEdge{Source="m:gain",Target="o:speaker"},new GraphEdge{Source="p:phone",Target="o:default"}},new[]{new ModuleSpec{Id="m:eq",Kind="equalizer"},new ModuleSpec{Id="m:gain",Kind="volume"}});
  var missingEffect=ApplicationRouteSelection.Select(effectGraph,identities,new ApplicationInput[0]);
  Check(missingEffect.Edges.Length==1&&missingEffect.PhoneIds.Single()=="phone","Missing app effect chain blocked phone");
  var resumedEffect=ApplicationRouteSelection.Select(effectGraph,identities,new[]{new ApplicationInput{Id="a:5:50",Path="edge.exe"}});
  Check(resumedEffect.RequiresVirtualApp&&resumedEffect.VirtualApplications.Single()=="a:5:50"&&resumedEffect.Edges.Length==4,"App restart lost effects");
  var owner=new Owner();var pending=new TaskCompletionSource<bool>();var barrier=PhoneRelease.Begin(owner,pending.Task);Check(!owner.Open&&!barrier.IsCompleted,"Immediate close or release barrier lost");owner.Open=true;pending.SetResult(true);await barrier;Check(!owner.Open&&owner.Closes==2,"Late Open survived release");owner.Fail=true;barrier=PhoneRelease.Begin(owner,null);Check(barrier.IsFaulted,"Close failure hidden");try{await barrier;}catch(InvalidOperationException){}
  var settings=new SettingsStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"migration-test-data"),true);settings.Set("ResumeGraph",true);settings.Set("LastRunningGraph",new RunningGraph{Edges=new[]{new GraphEdge{Source="pc:system",Target="o:default"}},Modules=new ModuleSpec[0],Applications=new RememberedApp[0]});
  using(var phones=new PhoneService(settings)){
   var s=phones.Get("test-phone");pending=new TaskCompletionSource<bool>();s.Operation=pending.Task;s.Connected=true;s.Paused=false;barrier=phones.Take(new[]{s.Id})[0];Check(s.Managed&&s.Paused&&!s.Connected&&!barrier.IsCompleted,"Graph did not acquire ownership");Check(Object.ReferenceEquals(phones.Release(s),barrier),"Repeated release lost barrier");Check(Object.ReferenceEquals(phones.Take(new[]{s.Id})[0],barrier),"Reapply lost release barrier");pending.SetResult(true);await barrier;await phones.Restore(s.Id,false);Check(!s.Managed&&s.Paused,"Manual disconnect resumed phone");phones.SetAutoConnect(true);Check(!s.Paused,"Enabling reconnect did not clear pause");
   using(var routing=new RouteCoordinator(settings,phones)){Check(routing.ResumePending,"Running intent not restored");await routing.Stop();Check(!settings.Bool("ResumeGraph")&&!routing.ResumePending,"Stop did not clear resume intent");Check(routing.Engine.Snapshot().Length==0,"Stop left graph sources");}
  }
  using(var phones=new PhoneService(settings)){
   var iphone=phones.Get("fixture-iphone");var android=phones.Get("fixture-android");android.Connected=true;var late=new TaskCompletionSource<bool>();android.Operation=late.Task;
   var barriers=phones.TakeGraph(new[]{iphone.Id});Check(phones.GraphOwnsPlayback&&iphone.Managed&&android.GraphSuspended&&!android.Connected,"Unrouted phone bypasses graph");Check(barriers.Length==2&&barriers.Any(t=>!t.IsCompleted),"Unrouted late-open release not awaited");
   await phones.Connect("new-phone",true);Check(phones.Get("new-phone").Connection==null&&phones.Get("new-phone").Detail.Contains("graph"),"Manual ordinary receiver bypasses graph");
   phones.TakeGraph(new[]{iphone.Id});Check(android.WasConnected,"Reapply overwrote original receiver state");late.SetResult(true);await Task.WhenAll(barriers);await phones.Disconnect(android.Id);
   await phones.RestoreGraph();Check(!phones.GraphOwnsPlayback&&!iphone.Managed&&!android.GraphSuspended&&android.Paused,"Stop failed to restore ownership or lost manual disconnect");
  }
  using(var engine=new GraphRuntime()){var close=new TaskCompletionSource<bool>();engine.PendingPhoneReleases=new[]{close.Task};var applying=engine.ApplyAsync(GraphPlan.Create(new GraphEdge[0],new ModuleSpec[0]));await Task.Delay(100);Check(!applying.IsCompleted,"Graph without phone nodes skipped receiver release");close.SetResult(true);await applying;}
  using(var phones=new PhoneService(settings))using(var routing=new RouteCoordinator(settings,phones)){
   phones.GraphOwnsPlayback=true;var selected=phones.Get("fixture-reconnect");selected.Managed=true;selected.Status="error";int opens=0;routing.PhoneOpener=id=>{Check(id==selected.Id,"Connected wrong phone");opens++;return Task.FromResult(true);};
   await routing.ConnectPhone(selected.Id);Check(opens==1&&selected.Connected&&selected.Managed&&selected.Connection==null,"Managed reconnect opened ordinary receiver or did not retry");Check(routing.Engine.Snapshot().Length==0,"Connect implicitly applied routes");
   routing.PhoneOpener=id=>{throw new InvalidOperationException("fixture failure");};bool failed=false;try{await routing.ConnectPhone(selected.Id);}catch(InvalidOperationException){failed=true;}Check(failed&&!selected.Connected&&selected.Status=="error"&&selected.Detail=="fixture failure"&&!routing.Busy,"Failed connect did not surface error or release busy flag");
  }
  using(var phones=new PhoneService(settings))using(var routing=new RouteCoordinator(settings,phones)){
   phones.GraphOwnsPlayback=true;var pendingOpen=new TaskCompletionSource<bool>();int opens=0;routing.PhoneOpener=id=>{opens++;return pendingOpen.Task;};
   var opening=routing.ConnectPhone("fixture-race");Check(routing.Busy,"Connect not serialized");bool rejected=false;try{await routing.ConnectPhone("fixture-race");}catch(InvalidOperationException){rejected=true;}Check(rejected&&opens==1,"Duplicate connect was not rejected");
   await routing.Stop();Check(!settings.Bool("ResumeGraph"),"Stop during opening lost intent");pendingOpen.SetResult(true);await opening;Check(!routing.Busy&&!phones.GraphOwnsPlayback&&!routing.ResumePending,"Deferred Stop did not finish");
   phones.GraphOwnsPlayback=true;await routing.DisconnectPhone("fixture-paused");Check(routing.IsPhonePaused("fixture-paused")&&phones.Get("fixture-paused").Paused,"Disconnect did not preserve pause");using(var reopened=new PhoneService(settings)){Check(reopened.Get("fixture-paused").Paused,"Restart lost manual pause");reopened.SetAutoConnect(true);Check(reopened.Get("fixture-paused").Paused,"Auto toggle overrode manual pause");}
   var pausedPlan=GraphPlan.Create(new[]{new GraphEdge{Source="p:fixture-paused",Target="o:default"}},new ModuleSpec[0]);rejected=false;try{await routing.Apply(pausedPlan,true);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Startup resume reopened manually disconnected phone");
   routing.PhoneOpener=id=>Task.FromResult(true);await routing.ConnectPhone("fixture-paused");Check(!routing.IsPhonePaused("fixture-paused"),"Explicit connect did not clear pause");
  }
  using(var phones=new PhoneService(settings))using(var routing=new RouteCoordinator(settings,phones)){
   phones.GraphOwnsPlayback=true;var session=phones.Get("fixture-exit");var release=new TaskCompletionSource<bool>();session.Release=release.Task;int opened=0;routing.PhoneOpener=id=>{opened++;return Task.FromResult(true);};var opening=routing.ConnectPhone(session.Id);var closing=routing.ShutdownAsync();Check(!closing.IsCompleted,"Shutdown did not await release barrier");Check(Object.ReferenceEquals(closing,routing.ShutdownAsync()),"Repeated exit created competing cleanup");release.SetResult(true);await opening;await closing;Check(opened==0,"Exit allowed late phone open after release");
  }
  if(args.Length>0){string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"legacy-test-data");Directory.CreateDirectory(root);File.Copy(args[0],Path.Combine(root,"settings.json"),true);var legacy=new SettingsStore(root,false);var before=SettingsStore.Json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(args[0]));Check(legacy.Bool("ResumeGraph")==Convert.ToBoolean(before["ResumeGraph"]),"Legacy running intent lost");Check(legacy.Items<AirBridge.UI.AudioLink>("AudioLinks").Length>0,"Legacy graph lost");var graph=legacy.Get<RunningGraph>("LastRunningGraph");if(graph!=null)GraphPlan.Create(graph.Edges,graph.Modules);legacy.Save();var after=SettingsStore.Json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(legacy.Path));Check(before.Keys.All(after.ContainsKey),"Legacy settings keys dropped");}
  Console.WriteLine("PASS: late-open cleanup, close failure propagation, repeated release barrier, graph ownership, manual disconnect, reconnect opt-in, startup intent, Stop intent, legacy settings roundtrip; no real audio opened.");
 }
}
