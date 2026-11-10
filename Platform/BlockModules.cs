using System;
using System.IO;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
namespace AirBridge.Routing {
 public sealed class BlockSamples:ISampleProvider {public float[] Data=new float[882];int at;public WaveFormat WaveFormat{get{return WaveFormat.CreateIeeeFloatWaveFormat(44100,2);}}public void Set(float[] data){Data=data;at=0;}public int Read(float[] b,int offset,int count){int n=Math.Min(count,Data.Length-at);Array.Copy(Data,at,b,offset,n);at+=n;return n;}}
 public sealed class BlockProcessor {
  readonly BlockSamples input=new BlockSamples();readonly EffectsProvider basic;readonly SmbPitchShiftingSampleProvider pitch;ModuleSpec[] effect;readonly float[] delay=new float[88200];int delayAt;double phase;
  public BlockProcessor(){effect=new ModuleSpec[0];basic=new EffectsProvider(input,()=>effect,()=>1,1);pitch=new SmbPitchShiftingSampleProvider(input);}
  bool gainReady,wetReady;float currentGain,wet;
  static float Move(float value,float target){const float step=1f/882;return value<target?Math.Min(target,value+step):Math.Max(target,value-step);}
  public float[] Process(float[] samples,ModuleSpec spec){
   bool active=spec.Enabled&&!spec.Bypass;
   if(spec.Kind=="volume"||spec.Kind=="mute"){
    float target=active?(spec.Kind=="mute"?0:spec.Gain):1;if(!gainReady){gainReady=true;currentGain=target;}
    var adjusted=new float[samples.Length];for(int i=0;i<samples.Length;i+=2){currentGain=Move(currentGain,target);adjusted[i]=samples[i]*currentGain;if(i+1<samples.Length)adjusted[i+1]=samples[i+1]*currentGain;}return adjusted;
   }
   float targetWet=active?1:0;if(!wetReady){wetReady=true;wet=targetWet;}if(wet==0&&targetWet==0)return samples;
   var processing=spec;if(!active){processing=spec.Copy();processing.Enabled=true;processing.Bypass=false;}
   var result=ProcessWet(samples,processing);
   for(int i=0;i<result.Length;i+=2){wet=Move(wet,targetWet);result[i]=samples[i]*(1-wet)+result[i]*wet;if(i+1<result.Length)result[i+1]=samples[i+1]*(1-wet)+result[i+1]*wet;}return result;
  }
  float[] ProcessWet(float[] samples,ModuleSpec spec){
   if(spec.Kind=="volume"||spec.Kind=="mute"||spec.Kind=="equalizer"){if(effect.Length!=1||effect[0].Kind!=spec.Kind||effect[0].Enabled!=spec.Enabled||effect[0].Preset!=spec.Preset||effect[0].Gain!=spec.Gain)effect=new[]{spec};input.Set(samples);var b=new float[samples.Length];basic.Read(b,0,b.Length);return b;}
   if(spec.Kind=="voice"&&spec.Mode!="robot"){if(spec.Semitones==0)return (float[])samples.Clone();pitch.PitchFactor=(float)Math.Pow(2,spec.Semitones/12);input.Set(samples);var b=new float[samples.Length];pitch.Read(b,0,b.Length);return b;}
   var result=(float[])samples.Clone();for(int i=0;i<result.Length;i+=2){float carrier=(float)Math.Sin(phase);phase+=2*Math.PI*70/44100;if(phase>2*Math.PI)phase-=2*Math.PI;
    for(int c=0;c<2;c++){int k=i+c;float v=result[k];if(spec.Kind=="voice")v*=carrier;else if(spec.Kind=="delay"){int distance=Math.Max(2,(int)(spec.DelayMs*44.1)*2);int index=(delayAt-distance+delay.Length)%delay.Length;float echo=delay[index];delay[delayAt]=v+echo*spec.Feedback;delayAt=(delayAt+1)%delay.Length;v=v*.8f+echo*.4f;}else if(spec.Kind=="limiter"){float threshold=Math.Max(.05f,spec.Gain);v=Math.Max(-threshold,Math.Min(threshold,v));}result[k]=Math.Max(-1,Math.Min(1,v));}
   }return result;
  }
  public static float[] Mix(float[][] sources,float[] gains,string mode,int selected){var output=new float[882];if(sources.Length==0)return output;if(mode=="select"){if(selected<0||selected>=sources.Length)return output;for(int i=0;i<882;i++)output[i]=sources[selected][i]*gains[selected];return output;}float scale=1f/Math.Max(1,sources.Length);for(int s=0;s<sources.Length;s++)for(int i=0;i<882;i++)output[i]+=sources[s][i]*gains[s]*scale;return output;}
  public static byte[] ToPcm(float[] samples){var b=new byte[samples.Length*2];for(int i=0;i<samples.Length;i++){int value=(int)Math.Round(Math.Max(-1,Math.Min(32767.0/32768,samples[i]))*32768);b[2*i]=(byte)value;b[2*i+1]=(byte)(value>>8);}return b;}
 }
 public sealed class WaveRecorder:IDisposable {
  readonly BlockingCollection<byte[]> pending=new BlockingCollection<byte[]>(256);readonly Task writer;volatile string error;public string FilePath;public long Bytes;
  public WaveRecorder(string folder){if(String.IsNullOrWhiteSpace(folder))throw new ArgumentException("请选择录音目录。");folder=System.IO.Path.GetFullPath(folder);Directory.CreateDirectory(folder);writer=Task.Run(()=>{try{WaveFileWriter wave=null;try{foreach(var block in pending.GetConsumingEnumerable()){if(wave==null||wave.Length>=1024L*1024*1024){if(wave!=null)wave.Dispose();FilePath=System.IO.Path.Combine(folder,"AirBridge-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".wav");wave=new WaveFileWriter(new FileStream(FilePath,FileMode.CreateNew,FileAccess.Write,FileShare.Read),new WaveFormat(44100,16,2));}wave.Write(block,0,block.Length);Bytes+=block.Length;}}finally{if(wave!=null)wave.Dispose();}}catch(Exception ex){error=ex.Message;}});}
  public void Add(byte[] pcm){if(error!=null)throw new IOException(error);if(!pending.TryAdd(pcm))throw new IOException("录音写盘跟不上音频，已停止录音。");}
  public void Dispose(){pending.CompleteAdding();writer.GetAwaiter().GetResult();pending.Dispose();if(error!=null)throw new IOException(error);}
 }
}
