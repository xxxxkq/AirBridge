using System;
using AirBridge.Routing;
class PhoneAudioQualityTests {
 static int passed;static void Check(bool ok,string message){if(!ok)throw new Exception(message);passed++;Console.WriteLine("PASS "+message);}
 static void Main(){var q=new PhoneAudioQuality();Check(q.Snapshot(0).State=="waiting","initial waiting");q.Arrived(0);for(int i=0;i<10;i++)q.Read(i*10,1764,1764);Check(q.Snapshot(90).State=="buffering"&&q.Snapshot(90).ObservedMilliseconds==0,"startup reserve excluded");q.Read(100,0,1764);Check(q.Snapshot(100).MissingPercent==0&&q.Snapshot(100).ObservedMilliseconds==10,"first delivered frame starts window");
  q.Reset();for(int i=0;i<1000;i++){if(i%10!=9)q.Arrived(i*10);q.Read(i*10,i%10==9?1764:0,1764);}var s=q.Snapshot(9990);Check(s.State=="active"&&Math.Abs(s.MissingPercent-10)<.0001&&s.ObservedMilliseconds==10000,"10-second 10 percent shortage");
  for(int i=1000;i<2000;i++){q.Arrived(i*10);q.Read(i*10,0,1764);}Check(q.Snapshot(19990).MissingPercent==0,"old shortages expire from rolling window");
  q.Read(20000,1764,1764);Check(q.Snapshot(20000).MissingPercent>0,"short gap remains a real shortage");Check(q.Snapshot(22000).State=="waiting","2-second no-frame gap displays waiting, not 100 percent");
  q.Arrived(22010);q.Read(22010,1764,1764);Check(q.Snapshot(22010).State=="buffering","resume reserve excluded");q.Arrived(22020);q.Read(22020,0,1764);Check(q.Snapshot(22020).MissingPercent==0&&q.Snapshot(22020).ObservedMilliseconds==10,"resume clears pause history");
  q.Read(22030,882,1764);Check(Math.Abs(q.Snapshot(22030).MissingPercent-25)<.0001,"partial block shortage counted in bytes");
  var second=new PhoneAudioQuality();second.Arrived(22030);second.Read(22030,0,1764);Check(second.Snapshot(22030).MissingPercent==0&&q.Snapshot(22030).MissingPercent==25,"phones have independent windows");
  q.Reset();Check(q.Snapshot(22040).State=="waiting","configuration reset discards old measurements");
  q.Arrived(23000);q.Read(23000,0,1764);var before=q.Snapshot(23000);var metrics=new AudioReadMetrics();metrics.Trim(176400);Check(q.Snapshot(23000).MissingPercent==before.MissingPercent,"intentional trimming does not affect missing ratio");
  Console.WriteLine("PASS total="+passed+"; deterministic clock, no devices opened");
 }
}
