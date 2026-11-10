using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
namespace AirBridge.Routing {
 public sealed class ApplicationInput {public string Id {get;set;} public string Name {get;set;} public string Path {get;set;}}
 public sealed partial class GraphRuntime:IDisposable {
  public bool VirtualPcEnabled;public Action EnsureRecoveryWatcher;public bool VirtualPcActive{get;private set;}string virtualCapture="",virtualPhysical="",virtualSource="";bool virtualDefaultAlias;
  public bool VirtualPathFailed{get{lock(gate){Source s;return VirtualPcActive&&((sources.TryGetValue(virtualCapture,out s)&&s.Error!="")||outputs.Values.Any(o=>o.Error!=""));}}}

  readonly object gate=new object(),applyGate=new object();Dictionary<string,Source> sources=new Dictionary<string,Source>();Dictionary<string,Output> outputs=new Dictionary<string,Output>();Dictionary<string,Module> modules=new Dictionary<string,Module>();GraphPlan plan=GraphPlan.Create(new GraphEdge[0],new ModuleSpec[0]);readonly Thread thread,nativeThread;readonly object nativeGate=new object();readonly PcAudioControl native=new PcAudioControl();volatile bool applied,nativePassThrough,processCopy;public string AppRoutingJournal;NativeAppRoutes appRoutes;DefaultOutputRoute defaultRoute;public string DefaultRoutingJournal;public string PhoneDiagnosticPath{set{PhoneWorkerClient.DiagnosticPath=value;}}string nativeTarget="o:default";public bool HasManagedDefault{get{return applied&&nativePassThrough;}}GraphEdge[] managedApps=new GraphEdge[0];public int ManagedAppCount {get{lock(gate)return managedApps.Length;}}int pendingRestoresSnapshot;
  // Status polling must never wait for Bluetooth open/close or output policy changes.
  public int PendingAppRestores {get{if(!System.Threading.Monitor.TryEnter(applyGate))return Volatile.Read(ref pendingRestoresSnapshot);try{int count=(appRoutes==null?0:appRoutes.PendingCount)+(defaultRoute==null?0:defaultRoute.PendingCount);Volatile.Write(ref pendingRestoresSnapshot,count);return count;}finally{System.Threading.Monitor.Exit(applyGate);}}}
  public void LoadAppRecovery(){lock(applyGate){if(appRoutes==null)appRoutes=new NativeAppRoutes(AppRoutingJournal);if(defaultRoute==null&&!String.IsNullOrWhiteSpace(DefaultRoutingJournal))defaultRoute=new DefaultOutputRoute(DefaultRoutingJournal);}}
  public void RestoreAppRecovery(){lock(applyGate){LoadAppRecovery();appRoutes.Restore();if(defaultRoute!=null)defaultRoute.Restore();managedApps=new GraphEdge[0];}}
  public void RememberOutputSelection(string id){lock(applyGate){if(defaultRoute==null)return;var endpoint=AirBridge.Platform.Audio.List().FirstOrDefault(e=>e.Id==id&&e.Active);if(endpoint==null||VirtualCableRouting.IsCablePlayback(endpoint))return;defaultRoute.RememberUserSelection(id);}}
  public Task[] PendingPhoneReleases=new Task[0];
  public string Notice="";public volatile bool TakeOverNativePlayback;volatile bool stopping;public int SourceStarts;long blocks,nativeProgress;public string Error="";
  public long RenderProgress {get{return Interlocked.Read(ref blocks);}}
  public long NativeProgress {get{return Interlocked.Read(ref nativeProgress);}}
  readonly bool threadsStarted;
  public GraphRuntime():this(true){}
  internal GraphRuntime(bool startThreads){threadsStarted=startThreads;thread=new Thread(Render){IsBackground=true,Name="AirBridge graph audio"};nativeThread=new Thread(NativeLoop){IsBackground=true,Name="AirBridge original playback"};if(startThreads){thread.Start();nativeThread.Start();}}
  readonly object meterGate=new object();readonly Dictionary<string,long> signalAt=new Dictionary<string,long>();
  public static string EdgeKey(string source,string target){return source+"\n"+target;}
  public static bool HasSignal(float[] block,float gain){if(gain<=0)return false;double energy=0;foreach(float value in block)energy+=value*value;return energy*gain*gain/Math.Max(1,block.Length)>0.0000001;}
  void Meter(GraphEdge edge,float[] block,float gain){if(HasSignal(block,gain))MarkSignal(edge.Source==virtualCapture?virtualSource:edge.Source,virtualDefaultAlias&&edge.Target==virtualPhysical?"o:default":edge.Target);}
  void MarkSignal(string source,string target){lock(meterGate)signalAt[EdgeKey(source,target)]=Stopwatch.GetTimestamp();}
  public string[] ActiveEdges(){lock(meterGate){long now=Stopwatch.GetTimestamp();foreach(var key in signalAt.Where(p=>(now-p.Value)/(double)Stopwatch.Frequency>=.65).Select(p=>p.Key).ToArray())signalAt.Remove(key);return signalAt.Keys.ToArray();}}
  void MeterNative(){if(!applied)return;var apps=managedApps;if(!nativePassThrough&&apps.Length==0)return;using(var enumerator=new MMDeviceEnumerator())foreach(var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active))using(endpoint){if(endpoint.AudioEndpointVolume.Mute||endpoint.AudioEndpointVolume.MasterVolumeLevelScalar<=0)continue;var sessions=endpoint.AudioSessionManager.Sessions;for(int i=0;i<sessions.Count;i++)using(var session=sessions[i]){uint pid=session.GetProcessID;if(pid==(uint)Process.GetCurrentProcess().Id||(session.DisplayName??"").IndexOf("A2DP SNK",StringComparison.OrdinalIgnoreCase)>=0||session.SimpleAudioVolume.Mute||session.SimpleAudioVolume.Volume<=0||session.AudioMeterInformation.MasterPeakValue<.001)continue;var app=apps.FirstOrDefault(a=>UInt32.Parse(a.Source.Split(':')[1])==pid);if(app!=null&&app.Source!=virtualSource)MarkSignal(app.Source,app.Target);else if(nativePassThrough&&virtualSource!="pc:system")MarkSignal("pc:system",nativeTarget);}}}
  public static ApplicationInput[] Applications(){var found=new Dictionary<uint,ApplicationInput>();using(var e=new MMDeviceEnumerator())foreach(var d in e.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active))using(d){var sessions=d.AudioSessionManager.Sessions;for(int i=0;i<sessions.Count;i++)using(var s=sessions[i]){uint pid=s.GetProcessID;if(pid==0||pid==(uint)Process.GetCurrentProcess().Id||found.ContainsKey(pid)||(s.DisplayName??"").IndexOf("A2DP SNK",StringComparison.OrdinalIgnoreCase)>=0)continue;try{using(var p=Process.GetProcessById((int)pid)){found[pid]=new ApplicationInput{Id="a:"+pid+":"+p.StartTime.ToUniversalTime().Ticks,Name=p.ProcessName+" · "+pid,Path=ApplicationIdentity.ProcessPath(p)};}}catch{}}}return found.Values.OrderBy(x=>x.Name).ToArray();}
  // Reconcile application policy without rebuilding phone capture or render buffers.
  public Task UpdateApplicationRoutesAsync(GraphEdge[] edges){return Task.Run(()=>{lock(applyGate){if(stopping)throw new ObjectDisposedException("GraphRuntime");if(edges.Any(e=>!e.Source.StartsWith("a:")||!e.Target.StartsWith("o:")||e.Gain!=1))throw new ArgumentException("Invalid native application route");LoadAppRecovery();if(VirtualPcActive)edges=edges.Select(e=>new GraphEdge{Source=e.Source,Target=e.Target=="o:default"?virtualPhysical:e.Target,Gain=e.Gain}).ToArray();appRoutes.Apply(edges);lock(gate)managedApps=edges;}});}
  public Task ApplyAsync(GraphPlan next){return Task.Run(()=>Apply(next));}
  public static void ValidateLivePlan(GraphPlan next,bool virtualEnabled=false){var checkedPlan=GraphPlan.Create(next.Edges,next.Modules);if(!virtualEnabled)checkedPlan.RequireLiveCapabilities();else VirtualCableRouting.Owner(checkedPlan);}
  GraphPlan requestedPlan=GraphPlan.Create(new GraphEdge[0],new ModuleSpec[0]);
  void Apply(GraphPlan next,bool keepPhones=true){lock(applyGate){
   if(stopping)throw new ObjectDisposedException("GraphRuntime");
   next=GraphPlan.Create(next.Edges,next.Modules);ValidateLivePlan(next,VirtualPcEnabled);
   string owner=VirtualPcEnabled?VirtualCableRouting.Owner(next):"";
   bool handoff=VirtualPcActive&&virtualSource!=owner&&next.Edges.Length>0;
   if(!handoff){ApplyCore(next,keepPhones);requestedPlan=next;return;}
   var previous=requestedPlan;var previousApps=managedApps.ToArray();
   try{
    // Remove only the shared virtual capture. Phone sources and physical renderers stay alive.
    ReleaseVirtualCapture();
    if(virtualSource=="pc:system")defaultRoute.Restore();
    else if(virtualSource.StartsWith("a:"))appRoutes.Apply(previousApps.Where(e=>e.Source!=virtualSource).ToArray());
    if(owner.StartsWith("a:")){
     var cable=VirtualCableRouting.Detect(AirBridge.Platform.Audio.List(),AirBridge.Platform.Audio.CaptureList());
     var deadline=DateTime.UtcNow.AddSeconds(3);
     while(true){try{NativeAppRoutes.RequireExclusiveCable(owner,cable.Playback);break;}
      catch(InvalidOperationException){if(DateTime.UtcNow>=deadline)throw new InvalidOperationException("其他应用尚未释放虚拟通道，切换已取消。请暂停并恢复该应用播放后重试。");Thread.Sleep(100);}}
    }
    ApplyCore(next,keepPhones);requestedPlan=next;
   }catch(Exception failure){
    try{if(defaultRoute!=null)defaultRoute.Restore();if(appRoutes!=null)appRoutes.Apply(previousApps);ApplyCore(previous,keepPhones);}
    catch(Exception rollback){Error="自动切换失败，恢复待完成："+rollback.GetBaseException().Message;throw new InvalidOperationException(Error,failure);}
    throw new InvalidOperationException("自动切换未完成，已恢复上次连线："+failure.GetBaseException().Message,failure);
   }
  }}
  void ReleaseVirtualCapture(){Source old=null;lock(gate){
   sources.TryGetValue(virtualCapture,out old);sources.Remove(virtualCapture);
   // Drop effect tails belonging to the departing input, including shared downstream effects.
   var affected=new HashSet<string>{virtualCapture};bool changed;
   do{changed=false;foreach(var edge in plan.Edges)if(affected.Contains(edge.Source)&&affected.Add(edge.Target))changed=true;}while(changed);
   foreach(var id in affected){Module module;if(modules.TryGetValue(id,out module)&&module.Kind!="record"&&module.Kind!="rtpout"){module.Dispose();modules[id]=new Module(module.Kind);}Output output;if(outputs.TryGetValue(id,out output))output.ClearQueued();}
   lock(meterGate)signalAt.Clear();
  }if(old!=null)old.Dispose();}
  void ApplyCore(GraphPlan next,bool keepPhones=true){lock(applyGate){if(stopping)throw new ObjectDisposedException("GraphRuntime");next=GraphPlan.Create(next.Edges,next.Modules);ValidateLivePlan(next,VirtualPcEnabled);
   string owner=VirtualPcEnabled?VirtualCableRouting.Owner(next):"";bool virtualNow=owner!="";VirtualCableEndpoints cable=null;string physical="";bool defaultAlias=next.Outputs.Contains("o:default");
   if(virtualNow){LoadAppRecovery();if(defaultRoute==null)throw new InvalidOperationException("Output recovery journal required.");physical=defaultRoute.OriginalId(1);var playback=AirBridge.Platform.Audio.List();cable=VirtualCableRouting.Detect(playback,AirBridge.Platform.Audio.CaptureList());next=VirtualCableRouting.Prepare(next,cable,playback,physical);if(owner.StartsWith("a:"))NativeAppRoutes.RequireExclusiveCable(owner,cable.Playback);if(EnsureRecoveryWatcher==null)throw new InvalidOperationException("Output recovery watcher required.");EnsureRecoveryWatcher();}

   var pending=PendingPhoneReleases;
   foreach(var task in pending){if(Task.WhenAny(task,Task.Delay(6500)).GetAwaiter().GetResult()!=task)throw new TimeoutException("旧手机连接仍在释放，请稍后重新应用连线；未打开第二个取流通道。");task.GetAwaiter().GetResult();}
   PendingPhoneReleases=new Task[0];
   var appEdges=next.NativeAppEdges;
   next=GraphPlan.Create(next.Edges.Where(e=>!e.Source.StartsWith("a:")).ToArray(),next.Modules);
   var pcEdge=next.Edges.FirstOrDefault(e=>e.Source=="pc:system");bool pass=pcEdge!=null;
   if(pass)next=GraphPlan.Create(next.Edges.Where(e=>e.Source!="pc:system").ToArray(),next.Modules);
   bool copy=next.Sources.Any(id=>id=="pc:system"||id.StartsWith("a:"));
   var desiredSources=new Dictionary<string,Source>();var desiredOutputs=new Dictionary<string,Output>();var addedSources=new List<Source>();var addedOutputs=new List<Output>();
   bool appPolicyApplied=false;
   try{
    foreach(var id in next.Sources){Source existing;ModuleSpec config=next.Modules.FirstOrDefault(m=>m.Id==id);if(sources.TryGetValue(id,out existing)&&existing.Error==""&&existing.SameConfig(config)){desiredSources[id]=existing;continue;}
     // The old source may still own a KS pin or UDP socket even after reporting an error.
     // Never open its replacement until disposal has joined the owning worker.
     if(existing!=null){lock(gate){sources.Remove(id);}existing.Dispose();}
     var source=new Source(id,config);source.ApplyTuning(tuning,phoneBuffering);addedSources.Add(source);source.Start();SourceStarts++;desiredSources[id]=source;}
    if(keepPhones)foreach(var old in sources.Where(s=>s.Key.StartsWith("p:")&&s.Value.Error==""&&!desiredSources.ContainsKey(s.Key)))desiredSources.Add(old.Key,old.Value);
    if(next.Outputs.Contains("o:default")){using(var e=new MMDeviceEnumerator())using(var d=e.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia)){if(next.Outputs.Contains("o:"+d.ID))throw new InvalidOperationException("系统默认播放器和同一物理设备不能重复输出，请只保留一个输出节点。");}}
    foreach(var id in next.Outputs){Output existing;if(outputs.TryGetValue(id,out existing)&&existing.Error==""){desiredOutputs[id]=existing;continue;}var output=new Output(id.Substring(2),this);addedOutputs.Add(output);output.Open();desiredOutputs[id]=output;}
    if(pcEdge!=null&&pcEdge.Target!="o:default"&&next.Outputs.Contains("o:default")&&next.Outputs.Contains(pcEdge.Target))throw new InvalidOperationException("切换后系统默认播放器与图中的物理输出重复，请只保留一个。");
    if(appEdges.Length>0||appRoutes!=null){LoadAppRecovery();appRoutes.Apply(appEdges);appPolicyApplied=true;}
    if(defaultRoute!=null||(pcEdge!=null&&pcEdge.Target!="o:default")){if(defaultRoute==null)defaultRoute=new DefaultOutputRoute(DefaultRoutingJournal);defaultRoute.Apply(pcEdge==null?null:pcEdge.Target.Substring(2));}
   }catch{foreach(var s in addedSources)s.Dispose();foreach(var o in addedOutputs)o.Dispose();if(appPolicyApplied)try{appRoutes.Apply(managedApps);}catch(Exception ex){Error="应用输出回滚待完成："+ex.Message;}throw;}
   Source[] oldSources;Output[] oldOutputs;Module[] oldModules;
   lock(gate){oldSources=sources.Values.Where(s=>!desiredSources.Values.Contains(s)).ToArray();oldOutputs=outputs.Values.Where(o=>!desiredOutputs.Values.Contains(o)).ToArray();var nextModules=new Dictionary<string,Module>();foreach(var spec in next.Modules.Where(m=>next.Order.Contains(m.Id)&&m.Kind!="rtpin")){Module old;if(modules.TryGetValue(spec.Id,out old)&&old.Kind==spec.Kind)nextModules[spec.Id]=old;else nextModules[spec.Id]=new Module(spec.Kind);}oldModules=modules.Values.Where(m=>!nextModules.Values.Contains(m)).ToArray();sources=desiredSources;outputs=desiredOutputs;modules=nextModules;plan=next;VirtualPcActive=virtualNow;virtualSource=owner;virtualCapture=virtualNow?"c:"+cable.Capture:"";virtualPhysical=virtualNow?"o:"+physical:"";virtualDefaultAlias=virtualNow&&defaultAlias;Error="";nativePassThrough=pass;nativeTarget=pcEdge==null?"o:default":pcEdge.Target;processCopy=copy;managedApps=appEdges;
    Notice=virtualNow?"VB-CABLE 音频处理中；停止后恢复原输出。":appEdges.Length>0?"应用由 Windows 直接播放到指定设备；如未立即切换，请暂停并继续播放。":copy?"电脑/应用取流保留原声；静音原应用会同时静音取流。" : pass?"电脑媒体由 Windows 原生播放到所选默认设备，不重复取流；停止时恢复原默认设备。":"";applied=true;}
   foreach(var s in oldSources)s.Dispose();foreach(var o in oldOutputs)o.Dispose();foreach(var m in oldModules)m.Dispose();foreach(var output in desiredOutputs.Values)output.Start();
  }}
  bool phoneBuffering=true;
  public void SetPhoneBuffering(bool enabled){lock(applyGate){if(stopping)throw new ObjectDisposedException("GraphRuntime");if(phoneBuffering==enabled)return;phoneBuffering=enabled;lock(gate)foreach(var source in sources.Values)source.ApplyTuning(tuning,enabled);}}
#if AIRBRIDGE_DEVELOPMENT
  public void ConfigurePhoneBuffers(int milliseconds,bool adaptive,bool diagnostics){PhoneWorkerClient[] workers;lock(gate){foreach(var source in sources.Values)source.ConfigureJitter(milliseconds,adaptive);workers=sources.Values.Where(s=>s.Phone!=null).Select(s=>s.Phone).ToArray();}foreach(var worker in workers)worker.SetDiagnostics(diagnostics);}
#endif
  public void FeedTest(string id,byte[] pcm){if(!id.StartsWith("test:"))throw new ArgumentException("Synthetic input only");lock(gate){Source source;if(!sources.TryGetValue(id,out source))throw new ArgumentException("Input not present");source.Inject(pcm);}}
  public bool TryUpdateEffect(ModuleSpec value){var replacement=value.Copy();GraphCompiler.ValidateModule(replacement);if(!new[]{"volume","mute","equalizer","voice","delay","limiter"}.Contains(replacement.Kind))return false;lock(gate){if(!plan.Order.Contains(replacement.Id)||!plan.Modules.Any(m=>m.Id==replacement.Id&&m.Kind==replacement.Kind))return false;plan=GraphPlan.Create(plan.Edges,plan.Modules.Select(m=>m.Id==replacement.Id?replacement:m).ToArray());requestedPlan=GraphPlan.Create(requestedPlan.Edges,requestedPlan.Modules.Select(m=>m.Id==replacement.Id?replacement.Copy():m).ToArray());return true;}}
  public void UpdateModules(ModuleSpec[] updated){foreach(var m in updated)GraphCompiler.ValidateModule(m);lock(gate){var replacements=plan.Modules.Select(old=>updated.FirstOrDefault(m=>m.Id==old.Id)??old).ToArray();plan=GraphPlan.Create(plan.Edges,replacements);}}
  void StopVirtualRendering(){Output[] stoppingOutputs;lock(gate){if(!VirtualPcActive)return;plan=GraphPlan.Create(new GraphEdge[0],new ModuleSpec[0]);stoppingOutputs=outputs.Values.ToArray();}foreach(var output in stoppingOutputs)output.StopForRestore();}

  public Task StopAsync(){return Task.Run(()=>{lock(applyGate){StopVirtualRendering();}Apply(GraphPlan.Create(new GraphEdge[0],new ModuleSpec[0]),false);applied=false;lock(nativeGate){native.Enabled=true;native.Tick();try{MeterNative();}catch{/* Metering must never interrupt playback. */}}});}
  public Task StopPhoneAsync(string id){return Task.Run(()=>{lock(applyGate){Source source=null;lock(gate){sources.TryGetValue("p:"+id,out source);sources.Remove("p:"+id);}if(source!=null)source.Dispose();}});}
  // Connect a single source without applying drafts, changing outputs, or inventing an edge.
  public Task OpenPhoneAsync(string id){return Task.Run(()=>{lock(applyGate){if(stopping)throw new ObjectDisposedException("GraphRuntime");string key="p:"+id;Source old;lock(gate){sources.TryGetValue(key,out old);if(old!=null&&old.Error=="")return;sources.Remove(key);}if(old!=null)old.Dispose();var source=new Source(key,null);source.ApplyTuning(tuning,phoneBuffering);try{source.Start();lock(gate)sources[key]=source;SourceStarts++;}catch{source.Dispose();throw;}}});}
  public InputLevel GetPhoneLevel(string id){lock(gate){Source s;return sources.TryGetValue("p:"+id,out s)?new InputLevel{Volume=s.Gain,Muted=s.Muted}:null;}}
  public void SetPhoneLevel(string id,float gain,bool mute){GraphCompiler.CheckGain(gain);lock(gate){Source s;if(!sources.TryGetValue("p:"+id,out s))throw new InvalidOperationException("手机输入未运行。");s.Gain=gain;s.Muted=mute;}}
  public RouteState[] Snapshot(){lock(gate){var values=sources.Select(s=>new RouteState{Source=s.Key==virtualCapture?virtualSource:s.Key.StartsWith("p:")?s.Key.Substring(2):s.Key,Target="graph",Status=s.Value.Error==""?"running":"error",Error=s.Value.Error,Bytes=s.Value.Bytes,NonZeroSamples=s.Value.Phone==null?0:s.Value.Phone.NonZeroSamples,Peak=s.Value.Phone==null?0:s.Value.Phone.Peak,PhoneFrames=s.Value.Phone==null?0:s.Value.Phone.Frames,ShortReportedFrames=s.Value.Phone==null?0:s.Value.Phone.ShortReportedFrames,TailSignalFrames=s.Value.Phone==null?0:s.Value.Phone.TailSignalFrames,InputNearFullScaleSamples=s.Value.Phone==null?0:s.Value.Phone.ClippedSamples,UnderflowBytes=s.Value.Underflow,UnderflowEvents=s.Value.Metrics.MissingEvents,ZeroFilledBlocks=s.Value.Metrics.MissingBlocks,ZeroFilledSampleFrames=s.Value.Metrics.MissingBytes/4,UnderflowMilliseconds=s.Value.Metrics.MissingMilliseconds,StartupSilenceBytes=s.Value.Metrics.StartupSilenceBytes,QueueClearCount=s.Value.Metrics.QueueClearCount,QueueTrimCount=s.Value.Metrics.QueueTrimCount,QueueTrimmedBytes=s.Value.Metrics.QueueTrimmedBytes,ArrivalJitterMilliseconds=s.Value.Jitter==null?0:s.Value.Jitter.Arrival.StandardDeviationMilliseconds,JitterSuggestedMilliseconds=s.Value.Jitter==null?0:s.Value.Jitter.SuggestedMilliseconds,InputSupplyDeficit=s.Value.Jitter!=null&&s.Value.Jitter.Arrival.SupplyDeficit,QueueDroppedBytes=s.Value.Metrics.QueueDroppedBytes,QueueOverflowCount=s.Value.Metrics.QueueOverflowCount,DiagnosticsDropped=PhoneWorkerClient.DroppedDiagnosticMessages,DiagnosticWriteErrors=PhoneWorkerClient.DiagnosticWriteErrors,QuietTrimmedBytes=s.Value.QuietTrimmedBytes,BufferMilliseconds=s.Value.BufferedMilliseconds,BufferTargetMilliseconds=s.Value.Jitter==null?80:s.Value.Jitter.TargetMilliseconds,RebufferCount=s.Value.Jitter==null?0:s.Value.Jitter.Rebuffers}).ToList();foreach(var a in managedApps.Where(a=>a.Source!=virtualSource))values.Add(new RouteState{Source=a.Source,Target=a.Target,Status="running",Error=""});if(nativePassThrough&&applied&&!VirtualPcActive)values.Add(new RouteState{Source="pc:system",Target=nativeTarget,Status="running",Error=""});foreach(var o in outputs)values.Add(new RouteState{Source=o.Key,Target=o.Key,Status=o.Value.Error==""?"running":"error",Error=o.Value.Error,Bytes=o.Value.Bytes,OutputReadBytes=o.Value.ReadBytes,MaxOutputReadIntervalMilliseconds=o.Value.MaxReadIntervalMilliseconds,MaxOutputReadWorkMilliseconds=o.Value.MaxReadWorkMilliseconds,UnderflowBytes=o.Value.Metrics.MissingBytes,UnderflowEvents=o.Value.Metrics.MissingEvents,ZeroFilledBlocks=o.Value.Metrics.MissingBlocks,ZeroFilledSampleFrames=o.Value.Metrics.MissingBytes/4,UnderflowMilliseconds=o.Value.Metrics.MissingMilliseconds,StartupSilenceBytes=o.Value.Metrics.StartupSilenceBytes,QueueClearCount=o.Value.Metrics.QueueClearCount,QueueDroppedBytes=o.Value.Metrics.QueueDroppedBytes,QueueOverflowCount=o.Value.Metrics.QueueOverflowCount});foreach(var m in modules.Where(m=>m.Value.Error!=""))values.Add(new RouteState{Source=m.Key,Status="error",Error=m.Value.Error});if(Error!="")values.Add(new RouteState{Source="graph",Status="error",Error=Error});foreach(var value in values){Source phone;if(sources.TryGetValue("p:"+value.Source,out phone)&&phone.Jitter!=null){var quality=phone.Quality.Snapshot(Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency);value.PhoneQualityState=plan.Sources.Contains("p:"+value.Source)?quality.State:"inactive";value.PhoneMissingPercent=quality.MissingPercent;value.PhoneObservedMilliseconds=quality.ObservedMilliseconds;value.AdaptiveTrimmedSampleFrames=phone.AdaptiveTrimmedSampleFrames;}value.MaxRenderIntervalMilliseconds=maxRenderInterval;value.MaxRenderWorkMilliseconds=maxRenderWork;}return values.ToArray();}}
  void NativeLoop(){while(!stopping){try{lock(nativeGate){if(applied||!native.Enabled){native.ExceptProcesses=managedApps.Select(e=>UInt32.Parse(e.Source.Split(':')[1])).Distinct().ToArray();native.Enabled=VirtualPcActive||!(applied&&TakeOverNativePlayback&&!nativePassThrough&&!processCopy);native.Tick();try{MeterNative();}catch{/* Metering must never interrupt playback. */}}}}catch(Exception ex){Error="原声控制："+ex.Message;}try{if(System.Threading.Monitor.TryEnter(applyGate)){try{RefreshDefaultOutput();}finally{System.Threading.Monitor.Exit(applyGate);}}}catch(Exception ex){Error="默认输出："+ex.Message;}Interlocked.Increment(ref nativeProgress);Thread.Sleep(500);}}
  void RefreshDefaultOutput(){if(!applied||stopping)return;lock(applyGate){Output old;lock(gate){if(!outputs.TryGetValue("o:default",out old))return;}
   string current;using(var e=new MMDeviceEnumerator())using(var d=e.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia))current=d.ID;
   if(old.DeviceId==current&&old.Error=="")return;
   lock(gate){if(outputs.ContainsKey("o:"+current)){Error="默认输出已变成图中的另一输出设备，请移除重复输出后应用。";return;}}
   var replacement=new Output("default",this);try{replacement.Open();}catch{replacement.Dispose();throw;}lock(gate){outputs["o:default"]=replacement;Error="";}old.Dispose();replacement.Start();
  }}
  double maxRenderInterval,maxRenderWork;long lastRenderAt;
  void MeasureRenderStart(){long now=Stopwatch.GetTimestamp();if(lastRenderAt!=0)maxRenderInterval=Math.Max(maxRenderInterval,(now-lastRenderAt)*1000.0/Stopwatch.Frequency);lastRenderAt=now;}
  void RenderBlock(){long tracing=AudioTimingTrace.Now();MeasureRenderStart();long work=Stopwatch.GetTimestamp();
   var consumed=GraphBlockEvaluator.Run(plan,
    id=>{Source source;return sources.TryGetValue(id,out source)?source.Read():null;},
    (id,block,spec)=>{Module module;return modules.TryGetValue(id,out module)?module.Process(block,spec):block;},
    (id,block)=>{Output output;if(outputs.TryGetValue(id,out output))output.Add(BlockProcessor.ToPcm(block));},Meter);
   foreach(var orphan in sources.Where(s=>!consumed.Contains(s.Key))){orphan.Value.Read();orphan.Value.Quality.Reset();}Interlocked.Increment(ref blocks);
   maxRenderWork=Math.Max(maxRenderWork,(Stopwatch.GetTimestamp()-work)*1000.0/Stopwatch.Frequency);AudioTimingTrace.Record("render-work","graph",tracing,blocks);
  }
  // One graph evaluation per block, shared by all outputs. Secondary output readers
  // consume their existing copies, never independently consume the source a second time.
  int ReadOutput(Output requester,byte[] data,int offset,int count){long waiting=AudioTimingTrace.Now();lock(gate){AudioTimingTrace.Record("graph-lock",requester.DeviceId,waiting,count);
   if(stopping||!outputs.Values.Contains(requester)||requester.Error!=""){Array.Clear(data,offset,count);return count;}
   try{int limit=(count+1763)/1764;while(requester.BufferedBytes<count&&limit-->0)RenderBlock();}
   catch(Exception ex){Error=ex.Message;}
   return requester.ReadBuffered(data,offset,count);
  }}
  // A software clock is only needed for graphs without any hardware playback sink
  // (e.g. recording/network-only). Hardware outputs pull at their own WASAPI deadlines.
  void Render(){var watch=Stopwatch.StartNew();double due=0;while(!stopping){
   bool softwareClock;lock(gate)softwareClock=outputs.Count==0&&(plan.Order.Length>0||sources.Count>0);
   if(!softwareClock){Thread.Sleep(50);due=watch.Elapsed.TotalMilliseconds;continue;}
   double remaining=due-watch.Elapsed.TotalMilliseconds;if(remaining>0){Thread.Sleep(1);continue;}due=watch.Elapsed.TotalMilliseconds+10;
   try{lock(gate){if(outputs.Count==0&&(plan.Order.Length>0||sources.Count>0))RenderBlock();}}catch(Exception ex){Error=ex.Message;}
  }}
  public void Dispose(){lock(applyGate){if(stopping)return;StopVirtualRendering();Exception restoreError=null;try{if(appRoutes!=null)appRoutes.Restore();}catch(Exception ex){restoreError=ex;}try{if(defaultRoute!=null)defaultRoute.Restore();}catch(Exception ex){restoreError=ex;}
   stopping=true;if(threadsStarted){thread.Join();nativeThread.Join();}lock(nativeGate){try{native.Dispose();}catch{}}
   Source[] oldSources;Output[] oldOutputs;Module[] oldModules;
   lock(gate){oldSources=sources.Values.ToArray();oldOutputs=outputs.Values.ToArray();oldModules=modules.Values.ToArray();sources.Clear();outputs.Clear();modules.Clear();}
   // Never join a playback thread while holding the graph lock its read callback needs.
   foreach(var s in oldSources)s.Dispose();foreach(var o in oldOutputs)o.Dispose();foreach(var m in oldModules)m.Dispose();if(restoreError!=null)throw restoreError;
  }}

  sealed class Source:IDisposable {
   public readonly string Id;readonly string network;readonly ModuleSpec config;BufferedWaveProvider buffer;ISampleProvider sample;MMDevice device;WasapiCapture capture;ProcessCapture process;RtpReceiver rtp;public PhoneWorkerClient Phone;public string Error="";public long Bytes,Underflow;public readonly AudioReadMetrics Metrics=new AudioReadMetrics();public float Gain=1;public bool Muted;bool primed;readonly object gate=new object();
   PlaybackTuning tuning=new PlaybackTuning();public void ApplyTuning(PlaybackTuning value,bool enabled){lock(gate){tuning=value.Clone();smoother.Configure(value.FadeMs);if(Jitter!=null){Jitter.SetTuning(value);ConfigureJitter(enabled?value.InitialBufferMs:value.LowBufferMs,enabled);}}}
   readonly PhoneCutCrossfade cut=new PhoneCutCrossfade();int silentPackets;readonly PhoneGapSmoother smoother=new PhoneGapSmoother();public long AdaptiveTrimmedSampleFrames;public long QuietTrimmedBytes;public readonly PhoneJitterBuffer Jitter;public readonly PhoneAudioQuality Quality=new PhoneAudioQuality();
   public void ConfigureJitter(int milliseconds,bool adaptive){lock(gate){if(Jitter==null)return;Jitter.Configure(milliseconds,adaptive);silentPackets=0;cut.Reset();Quality.Reset();if(buffer!=null){Metrics.Clear(buffer.BufferedBytes,false);buffer.ClearBuffer();}}}
public double BufferedMilliseconds{get{lock(gate)return buffer==null?0:buffer.BufferedDuration.TotalMilliseconds;}}
   public Source(string id,ModuleSpec config){Id=id;if(id.StartsWith("p:"))Jitter=new PhoneJitterBuffer();this.config=config;network=ConfigKey(config);}
   static string ConfigKey(ModuleSpec m){return m==null?"":m.Host+"|"+m.Port+"|"+m.PayloadType+"|"+m.Enabled;}
   public bool SameConfig(ModuleSpec m){return network==ConfigKey(m);}
   public void Start(){var format=new WaveFormat(44100,16,2);if(Id.StartsWith("c:")){using(var e=new MMDeviceEnumerator())device=e.GetDevice(Id.Substring(2));capture=new WasapiCapture(device);format=capture.WaveFormat;}
    buffer=new BufferedWaveProvider(format){BufferDuration=TimeSpan.FromSeconds(2),ReadFully=true,DiscardOnBufferOverflow=true};sample=buffer.ToSampleProvider();if(sample.WaveFormat.Channels==1)sample=new MonoToStereoSampleProvider(sample);if(sample.WaveFormat.Channels!=2)throw new NotSupportedException("录音输入目前需要单声道或双声道。");if(sample.WaveFormat.SampleRate!=44100)sample=new WdlResamplingSampleProvider(sample,44100);
    if(capture!=null){capture.DataAvailable+=(s,e)=>Add(e.Buffer,e.BytesRecorded);capture.RecordingStopped+=(s,e)=>{if(e.Exception!=null)Error=e.Exception.Message;};capture.StartRecording();}
    else if(Id.StartsWith("p:")){Phone=new PhoneWorkerClient(Id.Substring(2),Add,Fail);Phone.Tuning=tuning.Clone();Phone.Start();}
    else if(Id=="pc:system"||Id.StartsWith("a:")){uint pid=(uint)Process.GetCurrentProcess().Id;if(Id.StartsWith("a:")){var parts=Id.Split(':');pid=UInt32.Parse(parts[1]);using(var p=Process.GetProcessById((int)pid)){if(parts.Length!=3||p.StartTime.ToUniversalTime().Ticks!=Int64.Parse(parts[2]))throw new InvalidOperationException("应用已退出或重启，请刷新应用列表。");}}process=new ProcessCapture(Add,Fail);process.Start(pid,Id=="pc:system");}
    else if(config!=null&&config.Kind=="rtpin"&&config.Enabled)rtp=new RtpReceiver(config.Host,config.Port,config.PayloadType,Add,Fail);
   }
   void Fail(Exception ex){Error=ex.Message;}
   public void Inject(byte[] data){Add(data,data.Length);}
   void Add(byte[] data,int count){long waiting=Stopwatch.GetTimestamp();lock(gate){AudioTimingTrace.Record("source-lock",Id,waiting,count);Bytes+=count;if(Jitter!=null){Quality.Arrived(waiting*1000.0/Stopwatch.Frequency);Jitter.ObserveArrival(waiting*1000.0/Stopwatch.Frequency,count/176.4);bool silence=true;for(int i=0;i<count;i++)if(data[i]!=0){silence=false;break;}silentPackets=silence?silentPackets+1:0;if(silentPackets>=10&&buffer.BufferedDuration.TotalMilliseconds>Jitter.TargetMilliseconds+20){QuietTrimmedBytes+=count;return;}}if(Jitter!=null)TrimPhoneQueue(count);else if(buffer.BufferedDuration.TotalMilliseconds>tuning.InputOverflowMs){AudioTimingTrace.Record("input-overflow-clear",Id,AudioTimingTrace.Now(),buffer.BufferedBytes);Metrics.Clear(buffer.BufferedBytes,true);buffer.ClearBuffer();primed=false;}buffer.AddSamples(data,0,count);}}
   void TrimPhoneQueue(int incoming){int available=buffer.BufferedBytes;double hard=Math.Max(tuning.PhoneHardLimitMs,Jitter.TargetMilliseconds+10);if((available+incoming)/176.4<=hard)return;
    int target=(int)Math.Ceiling(Jitter.TargetMilliseconds*44.1)*4;int discard=Math.Max(0,available-Math.Max(0,target-incoming));discard-=discard%4;if(discard==0)return;
    int fadeBytes=Math.Min(discard,(int)(tuning.CutCrossfadeMs*44.1)*4);if(fadeBytes>0){var old=new byte[fadeBytes];buffer.Read(old,0,old.Length);cut.Begin(old);}
    int rest=discard-fadeBytes;var scratch=new byte[Math.Min(4096,rest)];while(rest>0){int n=Math.Min(rest,scratch.Length);buffer.Read(scratch,0,n);rest-=n;}
    Metrics.Trim(discard);AudioTimingTrace.Record("input-finite-trim",Id,AudioTimingTrace.Now(),discard,available+incoming,Math.Max(0,available-discard)+incoming);
   }
   public float[] Read(){lock(gate){var b=new float[882];bool ready;
    if(Jitter!=null){ready=Jitter.CanRead(buffer.BufferedDuration.TotalMilliseconds);if(Jitter.TargetMilliseconds==10&&buffer.BufferedBytes>0)ready=true;}
    else{if(!primed&&buffer.BufferedDuration.TotalMilliseconds>=80)primed=true;ready=primed&&buffer.BufferedBytes>0;if(!ready)primed=false;}
    if(!ready){AudioTimingTrace.Record("source-padding",Id,AudioTimingTrace.Now(),1764,buffer.BufferedBytes);Metrics.Read(1764,1764);if(Jitter!=null)Quality.Read(Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency,1764,1764);Underflow=Metrics.MissingBytes;if(Jitter!=null){smoother.Process(b,0);float level=Muted?0:Gain;for(int i=0;i<b.Length;i++)b[i]*=level;}return b;}
    int missing=Math.Max(0,1764-(int)Math.Min(1764,Math.Floor(buffer.BufferedDuration.TotalMilliseconds*44.1)*4));
    if(Jitter!=null&&Jitter.ShouldDrain(buffer.BufferedDuration.TotalMilliseconds)&&buffer.BufferedBytes>=1764+tuning.DrainFrames*4){
     // Consume one extra stereo sample frame (0.227%), not an entire packet.
     // Interpolate back to 441 frames so output timing and other sources do not move.
     int extra=tuning.DrainFrames;var input=new float[882+extra*2];sample.Read(input,0,input.Length);for(int f=0;f<441;f++){double position=f*(440.0+extra)/440;int a=(int)position,c=Math.Min(440+extra,a+1);float mix=(float)(position-a);for(int channel=0;channel<2;channel++)b[f*2+channel]=input[a*2+channel]*(1-mix)+input[c*2+channel]*mix;}AdaptiveTrimmedSampleFrames+=extra;AudioTimingTrace.Record("source-adaptive-drain",Id,AudioTimingTrace.Now(),extra,buffer.BufferedBytes);
    }else sample.Read(b,0,b.Length);
    if(Jitter!=null){cut.Apply(b,441-missing/4);smoother.Process(b,441-missing/4);}AudioTimingTrace.Record("source-read",Id,AudioTimingTrace.Now(),1764,missing,buffer.BufferedBytes);Metrics.Read(missing,1764);if(Jitter!=null)Quality.Read(Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency,missing,1764);Underflow=Metrics.MissingBytes;float gain=Muted?0:Gain;for(int i=0;i<b.Length;i++)b[i]*=gain;return b;
   }}

   public void Dispose(){if(Phone!=null)Phone.Dispose();if(process!=null)process.Dispose();if(rtp!=null)rtp.Dispose();if(capture!=null){capture.StopRecording();capture.Dispose();}if(device!=null)device.Dispose();}
  }
  sealed class Output:IDisposable {
   readonly int latency;readonly string id;string deviceId="";readonly GraphRuntime graph;MMDevice device;BufferedWaveProvider buffer;WasapiOut player;bool playing,paused;volatile bool stopping;
   readonly object outputGate=new object();public readonly AudioReadMetrics Metrics=new AudioReadMetrics();public long Bytes,ReadBytes;public string Error="";
   public double MaxReadIntervalMilliseconds,MaxReadWorkMilliseconds;long lastReadAt;
   public Output(string id,GraphRuntime graph):this(id,graph,graph.tuning.OutputLatencyMs){}
   public Output(string id,GraphRuntime graph,int latency){this.id=id;this.graph=graph;this.latency=latency;InitializeBuffer();}
   public void Pause(){paused=true;if(player!=null)player.Stop();playing=false;Error="";ClearQueued();}
   void InitializeBuffer(){buffer=new BufferedWaveProvider(new WaveFormat(44100,16,2)){BufferDuration=TimeSpan.FromSeconds(2),ReadFully=true,DiscardOnBufferOverflow=true};}
   public string DeviceId{get{return deviceId;}}
   public int BufferedBytes{get{lock(outputGate)return buffer.BufferedBytes;}}
   public void Open(){using(var e=new MMDeviceEnumerator())device=id=="default"?e.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia):e.GetDevice(id);deviceId=device.ID;player=new WasapiOut(device,AudioClientShareMode.Shared,true,latency);player.Init(new DemandProvider(this));player.PlaybackStopped+=(s,e)=>{if(!stopping&&!paused)Error=e.Exception==null?"输出停止":e.Exception.Message;};}
   public void Start(){if(playing||stopping||player==null)return;playing=true;paused=false;Error="";try{player.Play();}catch(Exception ex){Error=ex.Message;}}
   sealed class DemandProvider:IWaveProvider {readonly Output owner;public DemandProvider(Output owner){this.owner=owner;}public WaveFormat WaveFormat{get{return owner.buffer.WaveFormat;}}public int Read(byte[] data,int offset,int count){return owner.Read(data,offset,count);}}
   public int Read(byte[] data,int offset,int count){long begin=Stopwatch.GetTimestamp();try{if(stopping){Array.Clear(data,offset,count);return count;}return graph.ReadOutput(this,data,offset,count);}finally{AudioTimingTrace.Record("output-read",DeviceId,begin,count);lock(outputGate){if(lastReadAt!=0)MaxReadIntervalMilliseconds=Math.Max(MaxReadIntervalMilliseconds,(begin-lastReadAt)*1000.0/Stopwatch.Frequency);lastReadAt=begin;MaxReadWorkMilliseconds=Math.Max(MaxReadWorkMilliseconds,(Stopwatch.GetTimestamp()-begin)*1000.0/Stopwatch.Frequency);}}}
   public int ReadBuffered(byte[] data,int offset,int count){lock(outputGate){int missing=Math.Max(0,count-buffer.BufferedBytes);Metrics.Read(missing,count);ReadBytes+=count;return buffer.Read(data,offset,count);}}
   public void Add(byte[] b){if(Error!=""||stopping)return;lock(outputGate){if(buffer.BufferedDuration.TotalMilliseconds>graph.tuning.OutputOverflowMs){AudioTimingTrace.Record("output-overflow-clear",DeviceId,AudioTimingTrace.Now(),buffer.BufferedBytes);Metrics.Clear(buffer.BufferedBytes,true);buffer.ClearBuffer();}buffer.AddSamples(b,0,b.Length);Bytes+=b.Length;}}
   public void ClearQueued(){lock(outputGate){Metrics.Clear(buffer.BufferedBytes,false);buffer.ClearBuffer();}}
   public void StopForRestore(){stopping=true;Error="输出已停止，等待恢复";try{if(player!=null)player.Stop();}catch(Exception ex){Error=ex.Message;}ClearQueued();}
   public void Dispose(){stopping=true;if(player!=null){player.Stop();player.Dispose();}if(device!=null)device.Dispose();}
  }

  sealed class Module:IDisposable {public readonly string Kind;readonly BlockProcessor processor=new BlockProcessor();WaveRecorder recorder;RtpSender sender;string key="";public string Error="";public Module(string kind){Kind=kind;}
   public float[] Process(float[] data,ModuleSpec spec){if(Kind!="record"&&Kind!="rtpout")return processor.Process(data,spec);string next=spec.Enabled+"|"+spec.Path+"|"+spec.Host+"|"+spec.Port+"|"+spec.PayloadType;if(next!=key){Dispose();key=next;Error="";}
    if(!spec.Enabled||Error!="")return data;
    try{var pcm=BlockProcessor.ToPcm(data);if(Kind=="record"){if(recorder==null)recorder=new WaveRecorder(spec.Path);recorder.Add(pcm);}else{if(sender==null)sender=new RtpSender(spec.Host,spec.Port,spec.PayloadType);sender.Add(pcm,pcm.Length);}}catch(Exception ex){Error=ex.Message;try{Dispose();}catch{}}return data;
   }
   public void Dispose(){try{if(recorder!=null)recorder.Dispose();}catch(Exception ex){Error=ex.Message;}finally{recorder=null;if(sender!=null)sender.Dispose();sender=null;}}
  }
 }
}
