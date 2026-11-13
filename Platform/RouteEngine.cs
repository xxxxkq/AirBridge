using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
namespace AirBridge.Routing {
 public sealed class RouteSpec {public string Source,Target;public bool Loopback,Phone;public float Gain=1;public ModuleSpec[] Modules=new ModuleSpec[0];}
 public sealed class RouteState {public string PhoneQualityState;public double PhoneMissingPercent,PhoneObservedMilliseconds;public long AdaptiveTrimmedSampleFrames;public long QueueTrimCount,QueueTrimmedBytes;public double ArrivalJitterMilliseconds;public int JitterSuggestedMilliseconds;public bool InputSupplyDeficit;public long UnderflowEvents,ZeroFilledBlocks,ZeroFilledSampleFrames,StartupSilenceBytes,QueueClearCount,QueueDroppedBytes,QueueOverflowCount,OutputReadBytes,DiagnosticsDropped,DiagnosticWriteErrors;public double MaxOutputReadIntervalMilliseconds,MaxOutputReadWorkMilliseconds;public double UnderflowMilliseconds,MaxRenderIntervalMilliseconds,MaxRenderWorkMilliseconds;public long QuietTrimmedBytes;public double BufferMilliseconds;public int BufferTargetMilliseconds,RebufferCount;public string Source,Target,Status,Error;public long NonZeroSamples,Peak;public long Bytes,PhoneFrames,ShortReportedFrames,TailSignalFrames,InputNearFullScaleSamples,UnderflowBytes;}
 public sealed class InputLevel {public float Volume;public bool Muted;}
 public sealed class RouteEngine:IDisposable {
  readonly object gate=new object();readonly List<Input> inputs=new List<Input>();bool disposed;
  public static void Validate(RouteSpec[] specs){if(specs==null||specs.Length>16)throw new ArgumentException("最多同时使用 16 条音频路径。");var keys=new HashSet<string>();foreach(var s in specs){if(s==null||String.IsNullOrWhiteSpace(s.Source)||String.IsNullOrWhiteSpace(s.Target)||s.Loopback)throw new ArgumentException("需要独立输入和播放输出，不支持系统混音节点。");GraphCompiler.CheckGain(s.Gain);if(s.Modules==null)throw new ArgumentException("缺少处理链。");foreach(var m in s.Modules)GraphCompiler.ValidateModule(m);string key=s.Source+"|"+s.Target+"|"+String.Join(",",s.Modules.Select(m=>m.Id));if(!keys.Add(key))throw new ArgumentException("重复音频路径。");}}
  public static string PhoneAvailabilityError(string id){try{PhoneCapture.FindPath(id);return "";}catch(Exception ex){return ex.GetBaseException().Message;}}
  public static bool PhoneSupported(string id){return PhoneAvailabilityError(id)=="";}
  public Task ApplyAsync(RouteSpec[] specs){Validate(specs);return Task.Run(()=>{lock(gate){if(disposed)throw new ObjectDisposedException("RouteEngine");using(var e=new MMDeviceEnumerator()){foreach(var s in specs){using(var t=e.GetDevice(s.Target)){if(t.State!=DeviceState.Active||t.DataFlow!=DataFlow.Render)throw new InvalidOperationException("输出设备不可用。");}if(s.Phone)PhoneCapture.FindPath(s.Source);else using(var c=e.GetDevice(s.Source)){if(c.State!=DeviceState.Active||c.DataFlow!=DataFlow.Capture)throw new InvalidOperationException("录音输入不可用。");}}}Stop();try{foreach(var group in specs.GroupBy(s=>(s.Phone?"p:":"c:")+s.Source)){var input=new Input(group.ToArray());inputs.Add(input);input.Start();}}catch{Stop();throw;}}});}
  public void UpdateModules(ModuleSpec[] modules){foreach(var m in modules)GraphCompiler.ValidateModule(m);lock(gate)foreach(var input in inputs)input.Update(modules);}
  public InputLevel GetPhoneLevel(string id){lock(gate){var input=inputs.FirstOrDefault(i=>i.Phone&&i.Id==id);return input==null?null:new InputLevel{Volume=input.Volume,Muted=input.Muted};}}
  public void SetPhoneLevel(string id,float volume,bool muted){GraphCompiler.CheckGain(volume);lock(gate){var input=inputs.FirstOrDefault(i=>i.Phone&&i.Id==id);if(input==null)throw new InvalidOperationException("手机独立输入未运行。");input.Volume=volume;input.Muted=muted;}}
  public RouteState[] Snapshot(){lock(gate)return inputs.SelectMany(i=>i.Snapshot()).ToArray();}
  public Task StopPhoneAsync(string id){return Task.Run(()=>{lock(gate){foreach(var i in inputs.Where(i=>i.Phone&&i.Id==id).ToArray()){i.Dispose();inputs.Remove(i);}}});}
  public Task StopAsync(){return Task.Run(()=>{lock(gate)Stop();});}
  void Stop(){foreach(var i in inputs)i.Dispose();inputs.Clear();}
  public void Dispose(){lock(gate){if(disposed)return;disposed=true;Stop();}}
  sealed class Input:IDisposable {
   readonly RouteSpec[] specs;readonly List<Output> outputs=new List<Output>();MMDevice device;WasapiCapture capture;PhoneWorkerClient phone;volatile bool stopped;volatile string error="";long bytes;public readonly string Id;public readonly bool Phone;public volatile float Volume=1;public volatile bool Muted;
   public Input(RouteSpec[] specs){this.specs=specs;Id=specs[0].Source;Phone=specs[0].Phone;}
   public void Start(){WaveFormat format;if(Phone)format=new WaveFormat(44100,16,2);else{using(var e=new MMDeviceEnumerator())device=e.GetDevice(Id);capture=new WasapiCapture(device);format=capture.WaveFormat;}
    foreach(var s in specs){var output=new Output(s,format,()=>Muted?0:Volume,Fail);outputs.Add(output);output.Open();}
    if(Phone){phone=new PhoneWorkerClient(Id,Data,Fail);phone.Start();}else{capture.DataAvailable+=(s,e)=>Data(e.Buffer,e.BytesRecorded);capture.RecordingStopped+=(s,e)=>{if(!stopped)Fail(e.Exception??new Exception("输入设备停止采集。"));};capture.StartRecording();}
   }
   void Data(byte[] b,int n){if(stopped||error.Length>0)return;try{foreach(var output in outputs)output.Add(b,n);Interlocked.Add(ref bytes,n);}catch(Exception ex){Fail(ex);}}
   void Fail(Exception ex){if(stopped)return;error=ex.Message;ThreadPool.QueueUserWorkItem(_=>Dispose());}
   public void Update(ModuleSpec[] modules){foreach(var o in outputs)o.Update(modules);}
   public RouteState[] Snapshot(){return specs.Select(s=>new RouteState{Source=Id,Target=s.Target,Status=error.Length>0?"error":stopped?"stopped":"running",Error=error,Bytes=Interlocked.Read(ref bytes),PhoneFrames=phone==null?0:Interlocked.Read(ref phone.Frames),ShortReportedFrames=phone==null?0:Interlocked.Read(ref phone.ShortReportedFrames),TailSignalFrames=phone==null?0:Interlocked.Read(ref phone.TailSignalFrames),InputNearFullScaleSamples=phone==null?0:Interlocked.Read(ref phone.ClippedSamples),UnderflowBytes=outputs.Sum(o=>o.UnderflowBytes)}).ToArray();}
   public void Dispose(){lock(this){if(stopped)return;stopped=true;try{if(phone!=null)phone.Dispose();}catch{}try{if(capture!=null){capture.StopRecording();capture.Dispose();}}catch{}foreach(var o in outputs)o.Dispose();if(device!=null)device.Dispose();}}
  }
  sealed class MeteredProvider:IWaveProvider {readonly BufferedWaveProvider buffer;public long MissingBytes;public MeteredProvider(BufferedWaveProvider buffer){this.buffer=buffer;}public WaveFormat WaveFormat{get{return buffer.WaveFormat;}}public int Read(byte[] data,int offset,int count){int available=buffer.BufferedBytes;if(available<count)Interlocked.Add(ref MissingBytes,count-available);return buffer.Read(data,offset,count);}}
  sealed class Output:IDisposable {
   readonly RouteSpec spec;readonly WaveFormat format;readonly Func<float> gain;readonly Action<Exception> fail;volatile ModuleSpec[] modules;BufferedWaveProvider buffer;MeteredProvider meter;public long UnderflowBytes{get{return meter==null?0:Interlocked.Read(ref meter.MissingBytes);}}PlaybackOutput player;bool playing,stopping;readonly object gate=new object();
   public Output(RouteSpec spec,WaveFormat format,Func<float> gain,Action<Exception> fail){this.spec=spec;this.format=format;this.gain=gain;this.fail=fail;modules=spec.Modules;}
   public void Open(){buffer=new BufferedWaveProvider(format){BufferDuration=TimeSpan.FromSeconds(2),ReadFully=true,DiscardOnBufferOverflow=false};meter=new MeteredProvider(buffer);player=new PlaybackOutput(spec.Target,format.Encoding==WaveFormatEncoding.Pcm&&format.BitsPerSample==16?(IWaveProvider)new PcmPassThrough(meter,()=>modules,gain,spec.Gain):new EffectsProvider(meter.ToSampleProvider(),()=>modules,gain,spec.Gain).ToWaveProvider(),100,null);player.PlaybackStopped+=(s,e)=>{if(!stopping)fail(e.Exception??new Exception("输出设备停止播放。"));};}
   public void Add(byte[] data,int count){lock(gate){if(stopping)return;if(buffer.BufferedDuration.TotalMilliseconds>500)throw new InvalidOperationException("输出跟不上输入，已停止此输入的转发，请检查输出设备。");buffer.AddSamples(data,0,count);if(!playing&&buffer.BufferedDuration.TotalMilliseconds>=200){player.Play();playing=true;}}}
   public void Update(ModuleSpec[] updated){modules=modules.Select(old=>updated.FirstOrDefault(m=>m.Id==old.Id)??old).ToArray();}
   public void Dispose(){lock(gate){if(stopping)return;stopping=true;try{if(player!=null){player.Stop();player.Dispose();}}catch{}}}
  }
 }
}
