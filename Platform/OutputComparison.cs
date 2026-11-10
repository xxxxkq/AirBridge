#if AIRBRIDGE_DEVELOPMENT
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;

namespace AirBridge.Routing {
 public sealed class OutputComparisonConfig {
  public string SpeakerId,HeadphoneId;
  public int SettleSeconds=15,SampleSeconds=60;
  public void Validate(){if(String.IsNullOrWhiteSpace(SpeakerId)||String.IsNullOrWhiteSpace(HeadphoneId)||SpeakerId==HeadphoneId)throw new ArgumentException("请指定两个不同的物理输出端点。");if(SettleSeconds<1||SettleSeconds>30||SampleSeconds<1||SampleSeconds>120)throw new ArgumentException("Invalid comparison duration");}
 }
 public sealed class ComparisonSnapshot {
  public long Qpc=Stopwatch.GetTimestamp();public long Frequency=Stopwatch.Frequency;
  public Dictionary<string,string> Phones;public RouteState[] Routes;public string[] Outputs;
  public long TraceDropped=AudioTimingTrace.Dropped,TraceErrors=AudioTimingTrace.Errors;
 }
 public sealed class ComparisonPhase {
  public string Name,Endpoint;public ComparisonSnapshot Start,End;
 }
 // No device calls here: the same state machine is exercised with synthetic clocks/ports.
 public static class OutputComparisonRunner {
  public static void Check(ComparisonSnapshot initial,ComparisonSnapshot current,string endpoint){
   if(current.Phones==null||current.Phones.Count!=2||initial.Phones.Any(p=>!current.Phones.ContainsKey(p.Key)||current.Phones[p.Key]!=p.Value))throw new InvalidOperationException("手机会话发生变化，对照无效；不会自动重连。");
   foreach(var phone in current.Phones.Keys){var s=current.Routes.FirstOrDefault(x=>x.Source==phone);if(s==null||s.Status!="running"||s.BufferTargetMilliseconds!=10)throw new InvalidOperationException("手机状态或关闭缓冲的基线发生变化，对照已中止。");}
   if(endpoint!=null&&(current.Outputs.Length!=1||current.Outputs[0]!=endpoint))throw new InvalidOperationException("输出端点发生变化，对照无效。");
   if(current.Routes.Any(s=>s.Status=="error"))throw new InvalidOperationException("音频路径报错，对照已中止。");
   if(current.TraceDropped!=initial.TraceDropped||current.TraceErrors!=initial.TraceErrors)throw new InvalidOperationException("诊断丢失或写入失败，对照无效。");
  }
  public static async Task Run(OutputComparisonConfig config,Func<string,Task> change,Func<ComparisonSnapshot> snapshot,Func<Task> restore,Action<string,object> report,CancellationToken token,Func<int,CancellationToken,Task> delay=null){
   config.Validate();if(delay==null)delay=(ms,ct)=>Task.Delay(ms,ct);
   var initial=snapshot();Check(initial,initial,null);
   report("initial",initial);
   Exception failure=null;
   try{for(int phase=0;phase<4;phase++){
    token.ThrowIfCancellationRequested();string target=phase%2==0?config.SpeakerId:config.HeadphoneId;string name=(phase+1)+"-"+(phase%2==0?"speaker":"stereo");
    report("switch",name);AudioTimingTrace.Record("phase-switch",name,AudioTimingTrace.Now());await change(target);
    for(int i=0;i<config.SettleSeconds*4;i++){await delay(250,token);Check(initial,snapshot(),target);}
    var start=snapshot();Check(initial,start,target);report("sample-start",new ComparisonPhase{Name=name,Endpoint=target,Start=start});AudioTimingTrace.Record("phase-start",name,start.Qpc);
    for(int i=0;i<config.SampleSeconds*4;i++){await delay(250,token);var state=snapshot();Check(initial,state,target);if(i%4==3)report("sample",state);}
    var end=snapshot();Check(initial,end,target);report("phase",new ComparisonPhase{Name=name,Endpoint=target,Start=start,End=end});AudioTimingTrace.Record("phase-end",name,end.Qpc);
   }}catch(Exception ex){failure=ex;}
   try{await restore();report("restored",snapshot());}catch(Exception ex){report("restore-failed",ex.ToString());if(failure!=null)throw new AggregateException("对照和恢复均失败",failure,ex);throw;}
   if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
  }
 }
 public sealed partial class GraphRuntime {
  public sealed class ComparisonSession {
   readonly GraphRuntime engine;readonly GraphPlan original;readonly Dictionary<string,string> devices;
   readonly string[] phoneKeys;bool restored;readonly string headphoneId;
   internal ComparisonSession(GraphRuntime e,OutputComparisonConfig config){engine=e;headphoneId=config.HeadphoneId;config.Validate();lock(e.applyGate){lock(e.gate){
    if(e.phoneBuffering)throw new InvalidOperationException("对照要求关闭手机抗卡顿缓冲；未修改设置。");
    phoneKeys=e.sources.Where(p=>p.Value.Phone!=null&&p.Value.Error=="").Select(p=>p.Key).ToArray();
    if(phoneKeys.Length!=2||!e.applied)throw new InvalidOperationException("请先保持两台手机通道已运行，再启动对照。");
    original=GraphPlan.Create(e.plan.Edges,e.plan.Modules);devices=e.outputs.ToDictionary(p=>p.Key,p=>p.Value.DeviceId);
    if(devices.Count==0)throw new InvalidOperationException("没有可恢复的原输出。");
   }ValidateEndpoint(config.SpeakerId,false);ValidateEndpoint(config.HeadphoneId,true);}
   }
   public object Evidence(OutputComparisonConfig config){using(var enumerator=new MMDeviceEnumerator()){
    var endpoints=new[]{config.SpeakerId,config.HeadphoneId}.Concat(devices.Values).Distinct().Select(id=>{using(var d=enumerator.GetDevice(id))return new{Id=d.ID,Name=d.FriendlyName,State=d.State.ToString(),Format=d.AudioClient.MixFormat.ToString(),Channels=d.AudioClient.MixFormat.Channels};}).ToArray();
    using(var d=enumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia))return new{OriginalPlan=original,OriginalOutputs=devices,Endpoints=endpoints,DefaultOutput=d.ID,ProtocolEvidence="Endpoint identity and mix format only; radio codec/profile not independently observed"};
   }}
   static void ValidateEndpoint(string id,bool stereo){using(var e=new MMDeviceEnumerator())using(var d=e.GetDevice(id)){
    if(d.State!=DeviceState.Active||d.DataFlow!=DataFlow.Render)throw new InvalidOperationException("测试输出不可用："+id);
    if(stereo&&(d.AudioClient.MixFormat.Channels<2||d.FriendlyName.IndexOf("Hands-Free",StringComparison.OrdinalIgnoreCase)>=0||d.FriendlyName.IndexOf("免提",StringComparison.Ordinal)>=0))throw new InvalidOperationException("耳机端点不是预期立体声输出；不会回退到免提模式。");
   }}
   public ComparisonSnapshot Snapshot(){lock(engine.gate){return new ComparisonSnapshot{
    Phones=engine.sources.Where(p=>p.Value.Phone!=null).ToDictionary(p=>p.Key.Substring(2),p=>p.Value.Phone.DiagnosticIdentity),
    Routes=engine.Snapshot(),Outputs=engine.outputs.Values.Select(o=>o.DeviceId).OrderBy(x=>x).ToArray()};}}
   public Task Change(string id){return Task.Run(()=>{lock(engine.applyGate){ValidateEndpoint(id,id==headphoneId);
    // Only test rendering is replaced. No Phone.Start/Dispose, output-policy calls,
    // settings writes, pin state changes, or default-device changes are made.
    var testPlan=GraphPlan.Create(phoneKeys.Select(p=>new GraphEdge{Source=p,Target="o:"+id,Gain=1}).ToArray(),new ModuleSpec[0]);
    Replace(testPlan,new Dictionary<string,string>{{"o:"+id,id}});
   }});}
   void Replace(GraphPlan next,Dictionary<string,string> target){
    Output[] previous;lock(engine.gate){previous=engine.outputs.Values.ToArray();engine.outputs.Clear();engine.plan=GraphPlan.Create(new GraphEdge[0],new ModuleSpec[0]);}
    foreach(var o in previous)o.Dispose();
    var fresh=new Dictionary<string,Output>();try{foreach(var pair in target){var o=new Output(pair.Value,engine);fresh.Add(pair.Key,o);o.Open();}
     lock(engine.gate){engine.outputs=fresh;engine.plan=next;engine.lastRenderAt=0;engine.maxRenderInterval=engine.maxRenderWork=0;}
     foreach(var o in fresh.Values){o.Start();if(o.Error!="")throw new InvalidOperationException("输出启动失败："+o.Error);}
    }catch{foreach(var o in fresh.Values)o.Dispose();lock(engine.gate)engine.outputs.Clear();throw;}
   }
   public Task Restore(){return Task.Run(()=>{lock(engine.applyGate){if(restored)return;Replace(original,devices);restored=true;}});}
  }
  public ComparisonSession BeginOutputComparison(OutputComparisonConfig config){return new ComparisonSession(this,config);}
 }
}

#endif
