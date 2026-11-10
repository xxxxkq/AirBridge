using System;
using System.Linq;
using System.Reflection;
using AirBridge.Routing;
using NAudio.Wave;
class JitterTests {
 static int passed;
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);passed++;Console.WriteLine("PASS "+text);}
 static void Main(){
  var order=new CaptureCompletionOrder<long>();long item;
  for(int i=2;i<=16;i++)order.Store(i,i);
  Check(order.Count==15&&!order.CanStore(17)&&order.CanStore(1),"out-of-order completions bounded; oldest slot reserved");
  Check(!order.TryTake(out item),"missing earliest frame never skipped");
  order.Store(1,1);for(int i=1;i<=16;i++)if(!order.TryTake(out item)||item!=i)throw new Exception("PCM reordered");
  Check(order.Count==0&&order.Next==17&&!order.CanStore(16),"completion order preserved; stale frame rejected");
  var random=new Random(59);for(int batch=0;batch<1000;batch++){long first=order.Next;var seq=Enumerable.Range(0,8).OrderBy(x=>random.Next()).ToArray();foreach(int i in seq)order.Store(first+i,first+i);for(int i=0;i<8;i++)if(!order.TryTake(out item)||item!=first+i)throw new Exception("random order");}
  Check(order.Count==0,"8000 randomized completions delivered once in order");
  var low=new PhoneJitterBuffer();low.Configure(10,false);for(int i=0;i<1000;i++){low.CanRead(0);CheckLow(low);}
  Check(low.TargetMilliseconds==10&&!low.ShouldDrain(500),"disabled buffering never grows or accelerates");
  var p=new PhoneJitterBuffer();Check(!p.CanRead(29)&&p.CanRead(30),"adaptive starts with 30ms");
  for(int i=0;i<30;i++){p.CanRead(0);p.CanRead(p.TargetMilliseconds);}
  Check(p.TargetMilliseconds==120,"repeated shortages bounded at 120ms");
  p.CanRead(0);int events=p.Rebuffers;for(int i=0;i<1000;i++)p.CanRead(0);
  Check(p.Rebuffers==events,"pause does not repeatedly count failures");
  for(int i=0;i<9000;i++)p.CanRead(200);
  Check(p.TargetMilliseconds==30&&p.ShouldDrain(60),"stable playback lowers target and enables gentle drain");
  var fixedPolicy=new PhoneJitterBuffer();fixedPolicy.Configure(80,false);for(int i=0;i<2000;i++){fixedPolicy.CanRead(80);fixedPolicy.CanRead(0);}
  Check(fixedPolicy.TargetMilliseconds==80&&!fixedPolicy.ShouldDrain(200),"fixed diagnostic policy preserved");
  var fade=new PhoneGapSmoother();var tone=Enumerable.Repeat(.5f,882).ToArray();fade.Process(tone,441);
  Check(tone[0]>0&&tone[0]<.01f&&tone[176]==.5f,"resume ramp reaches original level in 2ms");
  var gap=new float[882];fade.Process(gap,0);Check(gap[0]<.5f&&gap[0]>.48f&&gap.Skip(176).All(x=>x==0),"gap fades to zero; no repeated old audio");
  fade.Process(gap,0);Check(gap.All(x=>x==0),"long pause stays silent");
  tone=Enumerable.Repeat(.5f,882).ToArray();fade.Process(tone,441);Check(tone[0]<.01f&&tone.Last()==.5f,"recovery fades in without overshoot");
  SourceDrain();Console.WriteLine("PASS total="+passed+"; offline, no endpoints opened");
 }
 static void CheckLow(PhoneJitterBuffer p){if(!p.CanRead(10)||p.TargetMilliseconds!=10)throw new Exception("low mode");}
 static void SourceDrain(){var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;var type=typeof(GraphRuntime).GetNestedType("Source",BindingFlags.NonPublic);var s=Activator.CreateInstance(type,flags,null,new object[]{"p:offline",null},null);var buffer=new BufferedWaveProvider(new WaveFormat(44100,16,2)){ReadFully=true};type.GetField("buffer",flags).SetValue(s,buffer);type.GetField("sample",flags).SetValue(s,buffer.ToSampleProvider());type.GetMethod("ConfigureJitter").Invoke(s,new object[]{30,true});
  var frame=new byte[1764];for(int i=0;i<frame.Length;i+=2)frame[i+1]=16;var inject=type.GetMethod("Inject");var read=type.GetMethod("Read");for(int i=0;i<6;i++)inject.Invoke(s,new object[]{frame});int before=buffer.BufferedBytes;
  for(int i=0;i<2400;i++){inject.Invoke(s,new object[]{frame});var b=(float[])read.Invoke(s,null);if(b.Any(x=>float.IsNaN(x)||Math.Abs(x)>.126))throw new Exception("drain signal");}
  var metrics=(AudioReadMetrics)type.GetField("Metrics").GetValue(s);
  Check(buffer.BufferedBytes<before&&(long)type.GetField("AdaptiveTrimmedSampleFrames").GetValue(s)>0,"stable drain reduces actual queue, not only target label");
  Check(metrics.MissingBytes==0&&metrics.QueueClearCount==0,"gentle drain avoids queue clears or invented shortages");
  type.GetMethod("ConfigureJitter").Invoke(s,new object[]{10,false});inject.Invoke(s,new object[]{new byte[880]});read.Invoke(s,null);
  Check(buffer.BufferedBytes==0,"low-latency partial frame consumed at render demand");
 }
}
