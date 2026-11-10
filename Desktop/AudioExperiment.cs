using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Threading.Tasks;using AirBridge.Routing;
namespace AirBridge.Desktop {
 internal sealed class AudioExperiment {
  public static bool Enabled;int phase=-1;DateTime next,started;Task switchTask;string path;RouteState[] baseline;readonly Dictionary<string,double> buffers=new Dictionary<string,double>();int samples;
  readonly int[] sizes={80,80,120,120,160,160};readonly bool[] logs={true,false,false,true,true,false};
  public void Tick(GraphRuntime engine,string root){if(!Enabled||DateTime.UtcNow<next)return;next=DateTime.UtcNow.AddMilliseconds(500);
   try{if(path==null){path=Path.Combine(root,"audio-ab-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".csv");File.WriteAllText(path,"phase,buffer_ms,phone_diagnostics,source,seconds,frames,signal_samples,rebuffer_events,mean_buffer_ms,valid\r\n");}
    if(switchTask!=null){if(!switchTask.IsCompleted)return;switchTask.GetAwaiter().GetResult();switchTask=null;started=DateTime.UtcNow;baseline=null;buffers.Clear();samples=0;}
    var states=engine.Snapshot().Where(s=>s.Target=="graph"&&s.Source!=null&&s.Source.IndexOf("BTHENUM",StringComparison.OrdinalIgnoreCase)>=0).OrderBy(s=>s.Source).ToArray();
    if(phase<0){if(states.Length<2||states.Any(s=>s.Status!="running"||s.PhoneFrames<200))return;Start(engine,0);return;}
    double seconds=(DateTime.UtcNow-started).TotalSeconds;if(seconds<10)return;
    if(baseline==null){baseline=states;foreach(var s in states)buffers[s.Source]=0;started=DateTime.UtcNow.AddSeconds(-10);return;}
    samples++;foreach(var s in states)if(buffers.ContainsKey(s.Source))buffers[s.Source]+=s.BufferMilliseconds;
    if(seconds<40)return;
    foreach(var before in baseline){var after=states.FirstOrDefault(s=>s.Source==before.Source);bool valid=after!=null&&after.Status=="running"&&after.PhoneFrames>=before.PhoneFrames&&after.NonZeroSamples>before.NonZeroSamples;var culture=System.Globalization.CultureInfo.InvariantCulture;File.AppendAllText(path,String.Join(",",new[]{(phase+1).ToString(),sizes[phase].ToString(),logs[phase]?"on":"off","\""+before.Source.Replace("\"","\"\"")+"\"",(seconds-10).ToString("F2",culture),after==null?"0":(after.PhoneFrames-before.PhoneFrames).ToString(),after==null?"0":(after.NonZeroSamples-before.NonZeroSamples).ToString(),after==null?"0":(after.RebufferCount-before.RebufferCount).ToString(),(buffers[before.Source]/Math.Max(1,samples)).ToString("F2",culture),valid?"yes":"no"})+Environment.NewLine);}
    if(++phase==sizes.Length){Enabled=false;engine.ConfigurePhoneBuffers(30,true,true);File.WriteAllText(path+".done","Completed; restored adaptive 30ms baseline and diagnostics.");return;}Start(engine,phase);
   }catch(Exception ex){Enabled=false;try{engine.ConfigurePhoneBuffers(30,true,true);}catch{}if(path!=null)File.WriteAllText(path+".error",ex.ToString());}
  }
  void Start(GraphRuntime engine,int index){phase=index;switchTask=Task.Run(()=>engine.ConfigurePhoneBuffers(sizes[index],false,logs[index]));}
 }
}
