using System;
namespace AirBridge.Routing {
 public sealed class PhoneQualityReading {public string State;public double MissingPercent,ObservedMilliseconds;}
 // Fixed 100ms buckets: bounded memory; measures render demand, never RF packet loss.
 public sealed class PhoneAudioQuality {
  sealed class Bucket {public long Stamp=-1,Total,Missing;}
  readonly object gate=new object();readonly Bucket[] buckets=new Bucket[100];double arrival=-1,lastRead=-1;bool started;
  public PhoneAudioQuality(){for(int i=0;i<buckets.Length;i++)buckets[i]=new Bucket();}
  void Clear(){foreach(var b in buckets){b.Stamp=-1;b.Total=b.Missing=0;}started=false;lastRead=-1;}
  public void Reset(){lock(gate){Clear();arrival=-1;}}
  public void Arrived(double now){lock(gate){if(arrival<0||now-arrival>=2000)Clear();arrival=now;}}
  public void Read(double now,int missingBytes,int totalBytes){lock(gate){lastRead=now;if(arrival<0||now-arrival>=2000)return;
   missingBytes=Math.Max(0,Math.Min(missingBytes,totalBytes));if(totalBytes<=0)return;
   if(!started){if(missingBytes==totalBytes)return;started=true;}
   long stamp=(long)(now/100);var b=buckets[(int)(stamp%100)];if(b.Stamp!=stamp){b.Stamp=stamp;b.Total=b.Missing=0;}b.Total+=totalBytes;b.Missing+=missingBytes;
  }}
  public PhoneQualityReading Snapshot(double now){lock(gate){if(arrival<0||now-arrival>=2000)return new PhoneQualityReading{State="waiting"};if(!started||lastRead<0||now-lastRead>=2000)return new PhoneQualityReading{State="buffering"};long stamp=(long)(now/100),total=0,missing=0;foreach(var b in buckets)if(b.Stamp<=stamp&&b.Stamp>stamp-100){total+=b.Total;missing+=b.Missing;}return new PhoneQualityReading{State=total==0?"buffering":"active",MissingPercent=total==0?0:missing*100.0/total,ObservedMilliseconds=total/176.4};}}
 }
}
