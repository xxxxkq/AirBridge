using System;
using System.Collections.Generic;
namespace AirBridge.Routing {
 public sealed class PhoneJitterBuffer {
  public readonly PhoneArrivalJitter Arrival=new PhoneArrivalJitter();int arrivalCount;
  public int SuggestedMilliseconds{get{return Arrival.Suggested(policy);}}
  public void ObserveArrival(double now,double duration){Arrival.Observe(now,duration,policy);if(++arrivalCount<100)return;arrivalCount=0;if(!adaptive||policy.JitterMeasurement==0||!Arrival.Valid||Arrival.SupplyDeficit)return;int desired=SuggestedMilliseconds;if(desired>TargetMilliseconds){TargetMilliseconds=Math.Min(desired,TargetMilliseconds+policy.IncreaseMs);stable=0;drain=false;}}
  public int TargetMilliseconds{get;private set;}public int Rebuffers{get;private set;}public bool Ready{get;private set;}PlaybackTuning policy=new PlaybackTuning();public void SetTuning(PlaybackTuning value){policy=value.Clone();}int stable;bool adaptive=true,drain;
  public PhoneJitterBuffer(){TargetMilliseconds=30;}
  public void Configure(int initial,bool automatic){if(initial<10||initial>500)throw new ArgumentOutOfRangeException("initial");adaptive=automatic;TargetMilliseconds=adaptive?Math.Max(policy.MinimumBufferMs,Math.Min(policy.MaximumBufferMs,initial)):initial;Reset();}
  public bool CanRead(double bufferedMilliseconds){if(!Ready){if(bufferedMilliseconds<TargetMilliseconds)return false;Ready=true;}if(bufferedMilliseconds<10){Ready=false;stable=0;drain=false;Rebuffers++;if(adaptive&&(policy.JitterMeasurement==0||!Arrival.Valid))TargetMilliseconds=Math.Min(policy.MaximumBufferMs,TargetMilliseconds+policy.IncreaseMs);return false;}
   if(adaptive&&++stable>=policy.StableSeconds*100){int floor=policy.JitterMeasurement!=0&&Arrival.Valid&&!Arrival.SupplyDeficit?SuggestedMilliseconds:policy.MinimumBufferMs;TargetMilliseconds=Math.Max(Math.Min(TargetMilliseconds,floor),TargetMilliseconds-policy.DecreaseMs);stable=0;drain=true;}return true;}
  public bool ShouldDrain(double bufferedMilliseconds){return policy.DrainFrames>0&&adaptive&&Ready&&drain&&bufferedMilliseconds>TargetMilliseconds+policy.DrainMarginMs;}
  public void Reset(){Ready=false;stable=0;drain=false;arrivalCount=0;Arrival.Reset();}
 }
 // Bound completed-but-not-yet-delivered data; reserve one place for the oldest
 // outstanding request. Never cancel, skip or reorder PCM to escape a slow request.
 public sealed class CaptureCompletionOrder<T> {
  readonly SortedDictionary<long,T> items=new SortedDictionary<long,T>();
  public long Next{get;private set;}public int Count{get{return items.Count;}}public const int Capacity=16;
  readonly int capacity;public CaptureCompletionOrder():this(Capacity){} public CaptureCompletionOrder(int limit){if(limit<2||limit>128)throw new ArgumentOutOfRangeException("limit");capacity=limit;Next=1;}
  public bool CanStore(long sequence){return sequence>=Next&&!items.ContainsKey(sequence)&&(items.Count<capacity-1||sequence==Next&&items.Count<capacity);}
  public void Store(long sequence,T value){if(!CanStore(sequence))throw new InvalidOperationException("Capture completion queue full or duplicate sequence");items.Add(sequence,value);}
  public bool TryTake(out T value){if(!items.TryGetValue(Next,out value))return false;items.Remove(Next++);return true;}
 }
 // A short deterministic ramp at a missing/recovered boundary, not replay of old audio.
 public sealed class PhoneGapSmoother {
  int Ramp=88;public void Configure(int ms){Ramp=(int)(ms*44.1);fadeIn=0;gap=true;left=right=0;}float left,right;int fadeIn;bool gap=true;
  public void Process(float[] samples,int validFrames){if(Ramp==0)return;int frames=samples.Length/2;validFrames=Math.Max(0,Math.Min(frames,validFrames));
   if(validFrames>0){if(gap)fadeIn=Ramp;for(int i=0;i<validFrames;i++){if(fadeIn>0){float gain=(Ramp-fadeIn+1)/(float)Ramp;samples[i*2]*=gain;samples[i*2+1]*=gain;fadeIn--;}left=samples[i*2];right=samples[i*2+1];}gap=false;}
   if(validFrames<frames){for(int i=validFrames;i<frames;i++){float gain=gap?0:Math.Max(0,1-(i-validFrames+1)/(float)Ramp);samples[i*2]=left*gain;samples[i*2+1]=right*gain;}gap=true;left=right=0;}
  }
 }
}
