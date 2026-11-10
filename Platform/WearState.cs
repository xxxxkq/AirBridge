using System;
using System.Linq;
namespace AirBridge {
 public static class WearAdvertisements {
  public static PodReading Select(PodReading[] readings,string model,DateTime now){
   var fresh=readings.Where(r=>r.Model==model&&(now-r.Seen).TotalSeconds>=0&&(now-r.Seen).TotalSeconds<=12&&r.Rssi>=-85).OrderBy(r=>r.Address,StringComparer.Ordinal).ToArray();
   if(fresh.Length==1)return fresh[0];
   if(fresh.Length!=2||fresh[0].LeftBroadcast==fresh[1].LeftBroadcast||Math.Abs(fresh[0].Rssi-fresh[1].Rssi)>30)return null;
   var a=fresh[0];var b=fresh[1];
   if(Conflict(a.Left,b.Left)||Conflict(a.Right,b.Right)||Conflict(a.Case,b.Case))return null;
   var left=a.LeftBroadcast?a:b;var right=a.LeftBroadcast?b:a;var merged=a.Copy();
   merged.Address=a.Address+"+"+b.Address;merged.Seen=a.Seen>b.Seen?a.Seen:b.Seen;
   merged.LeftInEar=left.LeftInEar;merged.RightInEar=right.RightInEar;
   merged.Left=left.Left;merged.LeftAt=left.LeftAt;merged.LeftCharging=left.LeftCharging;
   merged.Right=right.Right;merged.RightAt=right.RightAt;merged.RightCharging=right.RightCharging;
   var box=a.CaseAt>=b.CaseAt?a:b;merged.Case=box.Case;merged.CaseAt=box.CaseAt;merged.CaseCharging=box.CaseCharging;
   return merged;
  }
  static bool Conflict(int a,int b){return a>=0&&b>=0&&Math.Abs(a-b)>10;}
 }
 // No action on initial observation or on lost/ambiguous advertisements.
 public sealed class WearState {
  string identity=""; int stable=-1,candidate=-1,samples; DateTime since,last;
  public int Count {get{return stable;}}
  public void Reset(){identity="";stable=candidate=-1;samples=0;last=DateTime.MinValue;}
  public int Observe(string device,PodReading reading,DateTime now){
   if(reading==null||(now-reading.Seen).TotalSeconds>12||reading.Seen>now){Reset();return 0;}
   string key=device+"|"+reading.Model;
   if(identity!=key){Reset();identity=key;}
   if(reading.Seen<=last)return 0;
   last=reading.Seen;
   int count=(reading.LeftInEar?1:0)+(reading.RightInEar?1:0);
   if(candidate!=count){candidate=count;since=now;samples=1;return 0;}
   samples++;
   if(samples<2||(now-since).TotalSeconds<0.45||stable==candidate)return 0;
   int before=stable;stable=candidate;
   return before<0?0:stable<before?-1:1;
  }
 }
}
