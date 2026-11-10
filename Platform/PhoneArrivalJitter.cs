using System;
using System.Collections.Generic;
namespace AirBridge.Routing {
 // Measures delivered PCM, including driver and IPC batching, NOT RF/L2CAP packets.
 public sealed class PhoneArrivalJitter {
  struct Arrival {public double Gap,Clipped,Audio;}
  readonly Queue<Arrival> arrivals=new Queue<Arrival>();double last=-1,previousAudio=10,sum,squares,elapsed,audio;
  public double StandardDeviationMilliseconds{get;private set;}
  public bool Valid{get{return arrivals.Count>=20;}}
  public bool SupplyDeficit{get{return Valid&&elapsed>0&&audio/elapsed<.90;}}
  public void Reset(){arrivals.Clear();last=-1;sum=squares=elapsed=audio=0;StandardDeviationMilliseconds=0;}
  public void Observe(double now,double duration,PlaybackTuning settings){
   if(last<0){last=now;previousAudio=duration;return;}double gap=now-last;last=now;
   if(gap<0||gap>=settings.JitterPauseMs){Reset();last=now;previousAudio=duration;return;}
   var sample=new Arrival{Gap=gap,Clipped=Math.Min(settings.JitterClipMs,gap),Audio=previousAudio};previousAudio=duration;
   arrivals.Enqueue(sample);sum+=sample.Clipped;squares+=sample.Clipped*sample.Clipped;elapsed+=sample.Gap;audio+=sample.Audio;
   int maximum=Math.Max(20,settings.JitterWindowMs/10);while(arrivals.Count>maximum){sample=arrivals.Dequeue();sum-=sample.Clipped;squares-=sample.Clipped*sample.Clipped;elapsed-=sample.Gap;audio-=sample.Audio;}
   double mean=sum/arrivals.Count;StandardDeviationMilliseconds=Math.Sqrt(Math.Max(0,squares/arrivals.Count-mean*mean));
  }
  public int Suggested(PlaybackTuning settings){return Math.Max(settings.MinimumBufferMs,Math.Min(settings.MaximumBufferMs,(int)Math.Ceiling((10+StandardDeviationMilliseconds*settings.JitterMultiplierPercent/100)/10)*10));}
 }
 public sealed class PhoneCutCrossfade {
  float[] old;int position;
  public void Reset(){old=null;position=0;}
  // Keep the earliest unplayed continuation if another trim happens before playback.
  public void Begin(byte[] pcm){if(old!=null||pcm.Length==0)return;old=new float[pcm.Length/2];for(int i=0;i<old.Length;i++)old[i]=(short)(pcm[i*2]|pcm[i*2+1]<<8)/32768f;position=0;}
  public void Apply(float[] block,int validFrames){if(old==null)return;int frames=old.Length/2;for(int f=0;f<validFrames&&position<frames;f++,position++){float mix=frames<=1?1:position/(float)(frames-1);for(int c=0;c<2;c++)block[f*2+c]=old[position*2+c]*(1-mix)+block[f*2+c]*mix;}if(position>=frames)Reset();}
 }
}
