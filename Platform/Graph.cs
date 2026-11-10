using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.Wave;
using NAudio.Dsp;
namespace AirBridge.Routing {
 public sealed class ModuleSpec { public string Id,Kind,Preset="flat",Mode="mix",SelectedSource="",Host="127.0.0.1",Path=""; public int Port=5004,PayloadType=96;public float Gain=1,Semitones=0,DelayMs=150,Feedback=.25f; public bool Enabled=true,Bypass; public ModuleSpec Copy(){return (ModuleSpec)MemberwiseClone();} }
 public sealed class GraphEdge { public string Source,Target; public float Gain=1; }
 public static class GraphCompiler {
  public static RouteSpec[] Compile(GraphEdge[] edges,ModuleSpec[] modules) {
   if(edges==null||modules==null||edges.Length>64||modules.Length>24)throw new ArgumentException("连线或模块数量超出限制。");
   var map=new Dictionary<string,ModuleSpec>();foreach(var m in modules){ValidateModule(m);if(map.ContainsKey(m.Id))throw new ArgumentException("模块 ID 重复。");map.Add(m.Id,m);}
   var keys=new HashSet<string>();foreach(var e in edges){if(e==null||String.IsNullOrEmpty(e.Source)||String.IsNullOrEmpty(e.Target)||!keys.Add(e.Source+"|"+e.Target))throw new ArgumentException("无效或重复连线。");CheckGain(e.Gain);if(!(e.Source.StartsWith("p:")||e.Source.StartsWith("c:")||map.ContainsKey(e.Source))||!(e.Target.StartsWith("o:")||map.ContainsKey(e.Target)))throw new ArgumentException("只允许独立输入 → 处理模块 → 播放输出。");}
   foreach(var m in modules){if(edges.Count(e=>e.Target==m.Id)>1)throw new ArgumentException("一个处理模块只能接入一个输入；可从模块连接多个输出。");Visit(m.Id,edges,new HashSet<string>());}
   var result=new List<RouteSpec>();foreach(var e in edges.Where(e=>e.Source.StartsWith("p:")||e.Source.StartsWith("c:")))Walk(e.Source,e.Target,e.Gain,new List<ModuleSpec>(),edges,map,result);
   RouteEngine.Validate(result.ToArray());return result.ToArray();
  }
  static void Visit(string id,GraphEdge[] edges,HashSet<string> path){if(!path.Add(id))throw new ArgumentException("连线不能形成回路。");foreach(var e in edges.Where(e=>e.Source==id))Visit(e.Target,edges,path);path.Remove(id);}
  static void Walk(string source,string id,float gain,List<ModuleSpec> chain,GraphEdge[] edges,Dictionary<string,ModuleSpec> map,List<RouteSpec> result){
   if(result.Count>=16)throw new ArgumentException("最多同时使用 16 条完整音频路径。");
   if(id.StartsWith("o:")){result.Add(new RouteSpec{Source=source.Substring(2),Phone=source.StartsWith("p:"),Target=id.Substring(2),Gain=gain,Modules=chain.ToArray()});return;}
   var next=new List<ModuleSpec>(chain);next.Add(map[id]);var outgoing=edges.Where(e=>e.Source==id).ToArray();if(outgoing.Length==0)throw new ArgumentException("处理模块尚未连接到输出设备。");foreach(var e in outgoing)Walk(source,e.Target,gain*e.Gain,next,edges,map,result);
  }
  internal static void CheckGain(float g){if(Single.IsNaN(g)||Single.IsInfinity(g)||g<0||g>1)throw new ArgumentException("音量必须为 0–100%。");}
  public static void ValidateModule(ModuleSpec m){if(m==null||String.IsNullOrEmpty(m.Id)||!m.Id.StartsWith("m:")||!(new[]{"volume","mute","equalizer","mixer","voice","record","rtpin","rtpout","delay","limiter"}).Contains(m.Kind))throw new ArgumentException("无效模块。");CheckGain(m.Gain);if(m.Kind=="rtpin"||m.Kind=="rtpout"){System.Net.IPAddress ip;if(!System.Net.IPAddress.TryParse(m.Host,out ip))throw new ArgumentException("RTP 地址必须填写 IP。");}if(Single.IsNaN(m.Semitones)||m.Semitones < -12||m.Semitones>12||Single.IsNaN(m.DelayMs)||m.DelayMs<0||m.DelayMs>1000||Single.IsNaN(m.Feedback)||m.Feedback<0||m.Feedback>.8f)throw new ArgumentException("Invalid effect parameter");if(m.Port<1024||m.Port>65535||m.PayloadType<96||m.PayloadType>127)throw new ArgumentException("RTP port must be 1024–65535 and payload 96–127");if(!(new[]{"mix","select","robot","pitch"}).Contains(m.Mode))throw new ArgumentException("Invalid module mode");if(!(new[]{"flat","bass","voice","treble","warm"}).Contains(m.Preset))throw new ArgumentException("未知均衡器预设。");}
 }
 // Each route owns its filter history. Configuration changes are atomic and never reopen Bluetooth.
 public sealed class EffectsProvider : ISampleProvider {
  readonly ISampleProvider source;readonly Func<ModuleSpec[]> configuration;readonly Func<float> inputGain;readonly float gain;
  ModuleSpec[] current;BiQuadFilter[][][] filters;int channel;
  public WaveFormat WaveFormat{get{return source.WaveFormat;}}
  public EffectsProvider(ISampleProvider source,Func<ModuleSpec[]> configuration,Func<float> inputGain,float gain){this.source=source;this.configuration=configuration;this.inputGain=inputGain;this.gain=gain;}
  void Configure(ModuleSpec[] modules){current=modules;filters=new BiQuadFilter[modules.Length][][];for(int i=0;i<modules.Length;i++){var m=modules[i];if(m.Kind!="equalizer"||!m.Enabled||m.Bypass||m.Preset=="flat")continue;float[] db=m.Preset=="bass"?new float[]{5,0,-1}:m.Preset=="voice"?new float[]{-3,4,1}:m.Preset=="treble"?new float[]{-1,0,4}:new float[]{3,1,-2};filters[i]=new BiQuadFilter[WaveFormat.Channels][];for(int c=0;c<WaveFormat.Channels;c++){filters[i][c]=new BiQuadFilter[3];for(int b=0;b<3;b++)filters[i][c][b]=BiQuadFilter.PeakingEQ(WaveFormat.SampleRate,Math.Min(new[]{100f,1000f,8000f}[b],WaveFormat.SampleRate*.4f),.8f,db[b]);}}}
  public int Read(float[] buffer,int offset,int count){var modules=configuration();if(!Object.ReferenceEquals(modules,current))Configure(modules);int read=source.Read(buffer,offset,count);float level=inputGain()*gain;for(int n=offset;n<offset+read;n++){float v=buffer[n]*level;for(int i=0;i<current.Length;i++){var m=current[i];if(!m.Enabled||m.Bypass)continue;if(m.Kind=="mute")v=0;else if(m.Kind=="volume")v*=m.Gain;else if(filters[i]!=null){v*=.56f;foreach(var f in filters[i][channel])v=f.Transform(v);}}buffer[n]=Math.Max(-1,Math.Min(1,v));channel=(channel+1)%WaveFormat.Channels;}return read;}
 }
}
