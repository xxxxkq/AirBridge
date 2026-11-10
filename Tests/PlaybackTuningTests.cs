using System;
using System.Reflection;
using System.Web.Script.Serialization;
using AirBridge.Routing;
class PlaybackTuningTests {
 static int checks;static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);checks++;}
 static void Invalid(PlaybackTuning p,string name){try{p.Validate();}catch(ArgumentException){Check(true,name);return;}throw new Exception(name);}
 static void Main(){var p=new PlaybackTuning();p.Validate();Check(p.InitialBufferMs==30&&p.LowBufferMs==10&&p.OutputLatencyMs==100&&p.CaptureRequests==8&&p.CompletedFrames==16,"3.0.59 defaults preserved");
  var json=new JavaScriptSerializer();var round=json.Deserialize<PlaybackTuning>(json.Serialize(p));Check(json.Serialize(round)==json.Serialize(p),"settings round trip");
  var partial=json.Deserialize<PlaybackTuning>("{\"OutputLatencyMs\":60}");Check(partial.InitialBufferMs==30&&partial.OutputLatencyMs==60,"missing persisted fields retain defaults");
  foreach(var f in PlaybackTuning.Fields){var low=new PlaybackTuning();typeof(PlaybackTuning).GetProperty(f.Key).SetValue(low,f.Min-1,null);Invalid(low,"range lower "+f.Key);var high=new PlaybackTuning();typeof(PlaybackTuning).GetProperty(f.Key).SetValue(high,f.Max+1,null);Invalid(high,"range upper "+f.Key);}
  Invalid(new PlaybackTuning{MinimumBufferMs=50},"min/initial relationship");Invalid(new PlaybackTuning{InputOverflowMs=99},"other input overflow range");Invalid(new PlaybackTuning{OutputLatencyMs=400},"output overflow protects latency");Invalid(new PlaybackTuning{CaptureRequests=32,CompletedFrames=16},"capture capacity relationship");
  p=new PlaybackTuning{InitialBufferMs=60,MinimumBufferMs=20,MaximumBufferMs=100,IncreaseMs=20,DecreaseMs=5,StableSeconds=1,DrainMarginMs=30,DrainFrames=0};p.Validate();var jitter=new PhoneJitterBuffer();jitter.SetTuning(p);jitter.Configure(p.InitialBufferMs,true);Check(!jitter.CanRead(59)&&jitter.CanRead(60),"custom initial applied");jitter.CanRead(0);Check(jitter.TargetMilliseconds==80,"custom increment applied");for(int i=0;i<100;i++)jitter.CanRead(100);Check(jitter.TargetMilliseconds==75&&!jitter.ShouldDrain(200),"custom stability/decrease and drain disabled");
  var fade=new PhoneGapSmoother();fade.Configure(0);var samples=new[]{.5f,.5f};fade.Process(samples,1);Check(samples[0]==.5f,"zero fade disables processing");
  var type=typeof(GraphRuntime);using(var graph=(GraphRuntime)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{false},null)){p.OutputLatencyMs=60;graph.ApplyPlaybackTuning(p);p.OutputLatencyMs=70;Check(graph.GetPlaybackTuning().OutputLatencyMs==60,"applied snapshot isolated from caller");var saved=graph.GetPlaybackTuning();saved.OutputLatencyMs=0;try{graph.ApplyPlaybackTuning(saved);}catch(ArgumentException){}Check(graph.GetPlaybackTuning().OutputLatencyMs==60,"invalid configuration leaves runtime unchanged");graph.ApplyPlaybackTuning(new PlaybackTuning());Check(graph.GetPlaybackTuning().OutputLatencyMs==100,"restore defaults in idle runtime");}
  Console.WriteLine("PASS total="+checks+"; no endpoints opened");
 }
}
