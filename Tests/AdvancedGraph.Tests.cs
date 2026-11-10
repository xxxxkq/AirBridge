using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using AirBridge.Routing;
using NAudio.Wave;
public static class AdvancedGraphTests {
 static GraphEdge Edge(string a,string b){return new GraphEdge{Source=a,Target=b};}
 static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Invalid graph accepted");}
 public static void Run(string folder){Directory.CreateDirectory(folder);
  var app1=new ApplicationInput{Id="a:1:100",Path=@"C:\Apps\player.exe"};var app2=new ApplicationInput{Id="a:2:200",Path=app1.Path};
  Assert(AirBridge.Routing.ApplicationIdentity.Resolve(app1.Id,app1.Path,new[]{app1,app2})==app1,"Explicit instance lost");
  Assert(AirBridge.Routing.ApplicationIdentity.Resolve("old",app1.Path.ToUpperInvariant(),new[]{app2})==app2,"Restart path match failed");
  Assert(AirBridge.Routing.ApplicationIdentity.Resolve("old",app1.Path,new[]{app1,app2})==null,"Ambiguous restart selected arbitrary instance");
  Assert(AirBridge.Routing.ApplicationIdentity.Resolve("old","",new[]{app1})==null,"Missing identity guessed");
  Assert(AirBridge.Routing.ApplicationIdentity.Status("old",app1.Path,null)=="closed","Null app list failed");
  var ramp=new BlockProcessor();var signal=Enumerable.Repeat(.5f,882).ToArray();var rampSpec=new ModuleSpec{Id="m:gain",Kind="volume",Gain=1};ramp.Process(signal,rampSpec);rampSpec.Gain=0;float last=.5f;
  for(int block=0;block<3;block++){var result=ramp.Process(signal,rampSpec);for(int i=0;i<result.Length;i+=2){Assert(result[i]<=last+.00001f&&last-result[i]<.0006f,"Gain discontinuity");Assert(result[i]==result[i+1],"Stereo ramp mismatch");last=result[i];}}
  Assert(last==0&&signal.All(v=>v==.5f),"Ramp did not settle or mutated source");rampSpec.Bypass=true;for(int i=0;i<3;i++)last=ramp.Process(signal,rampSpec)[880];Assert(Math.Abs(last-.5)<.00001,"Bypass did not restore dry audio");

  var eqProcessor=new BlockProcessor();var eqSpec=new ModuleSpec{Id="m:eq",Kind="equalizer",Preset="bass"};eqProcessor.Process(signal,eqSpec);eqSpec.Bypass=true;float[] dry=null;for(int n=0;n<3;n++)dry=eqProcessor.Process(signal,eqSpec);Assert(dry.SequenceEqual(signal)&&signal.All(v=>v==.5f),"EQ bypass changed dry source");
  var mixer=new ModuleSpec{Id="m:mix",Kind="mixer"};var recorder=new ModuleSpec{Id="m:record",Kind="record",Path=folder};var voice=new ModuleSpec{Id="m:voice",Kind="voice",Mode="pitch",Semitones=5};
  var edges=new[]{Edge("test:a","m:mix"),Edge("test:b","m:mix"),Edge("m:mix","m:voice"),Edge("m:voice","m:record")};
  var plan=GraphPlan.Create(edges,new[]{mixer,voice,recorder});Assert(plan.Sources.Length==2,"Many-to-one sources lost");
  Reject(()=>GraphPlan.Create(new[]{Edge("m:mix","m:voice"),Edge("m:voice","m:mix")},new[]{mixer,voice}));
  GraphPlan.Create(new[]{Edge("pc:system","o:speaker"),Edge("a:1:1","o:speaker")},new ModuleSpec[0]);
  var nativePlan=GraphPlan.Create(new[]{Edge("pc:system","o:default"),Edge("a:1:1","o:speaker")},new ModuleSpec[0]);if(nativePlan.NativeAppEdges.Length!=1)throw new Exception("Native application classification failed");
  Assert(GraphPlan.Create(new[]{Edge("a:1:1","o:speaker"),Edge("a:1:1","o:other")},new ModuleSpec[0]).RequiresVirtualApp,"App fanout not classified as virtual");
  Assert(GraphPlan.Create(new[]{new GraphEdge{Source="a:1:1",Target="o:speaker",Gain=.5f}},new ModuleSpec[0]).RequiresVirtualApp,"App gain not classified as virtual");

  GraphPlan.Create(new[]{Edge("pc:system","o:speaker")},new ModuleSpec[0]);
  Assert(GraphPlan.Create(new[]{Edge("pc:system","o:speaker"),Edge("pc:system","o:other")},new ModuleSpec[0]).RequiresVirtualPc,"PC fan-out lost driver requirement");
  Assert(GraphPlan.Create(new[]{new GraphEdge{Source="pc:system",Target="o:speaker",Gain=.5f}},new ModuleSpec[0]).RequiresVirtualPc,"PC gain lost driver requirement");
  var retry=typeof(GraphRuntime).Assembly.GetType("AirBridge.Routing.PhoneCapture").GetMethod("ShouldRetryPin",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
  Assert((bool)retry.Invoke(null,new object[]{1450,0}),"Resource release error not retried");
  Assert(!(bool)retry.Invoke(null,new object[]{1450,10}),"Retries unbounded");
  Assert(!(bool)retry.Invoke(null,new object[]{5,0}),"Access denial retried");
  var a=Enumerable.Repeat(.8f,882).ToArray();var b=Enumerable.Repeat(.2f,882).ToArray();Assert(!GraphRuntime.HasSignal(new float[882],1),"Silence marked active");Assert(!GraphRuntime.HasSignal(a,0),"Zero gain marked active");Assert(GraphRuntime.HasSignal(a,1),"Signal not detected");var selectedMixer=new ModuleSpec{Id="m:select",Kind="mixer",Mode="select",SelectedSource="test:b"};var activityPlan=GraphPlan.Create(new[]{Edge("test:a","m:select"),Edge("test:b","m:select"),Edge("m:select","o:test")},new[]{selectedMixer});var active=new System.Collections.Generic.List<string>();GraphBlockEvaluator.Run(activityPlan,id=>id=="test:a"?a:id=="test:b"?b:null,(id,data,spec)=>data,(id,data)=>{},(edge,data,gain)=>{if(GraphRuntime.HasSignal(data,gain))active.Add(edge.Source);});Assert(!active.Contains("test:a")&&active.Contains("test:b")&&active.Contains("m:select"),"Inactive mixer branch animated");
  Assert(Math.Abs(BlockProcessor.Mix(new[]{a,b},new[]{1f,1f},"mix",0)[0]-.5)<.0001,"Mixer gain failed");Assert(Math.Abs(BlockProcessor.Mix(new[]{a,b},new[]{1f,1f},"select",1)[0]-.2)<.0001,"Input selector failed");
  var effect=new BlockProcessor();var pitch=new ModuleSpec{Id="m:pitch",Kind="voice",Mode="pitch",Semitones=12};int crossings=0,sampleNumber=0;float previous=0;double energy=0;
  for(int block=0;block<200;block++){var tone=new float[882];for(int i=0;i<441;i++){tone[i*2]=tone[i*2+1]=(float)(.2*Math.Sin(2*Math.PI*220*sampleNumber++/44100));}var changed=effect.Process(tone,pitch);if(block>60)for(int i=0;i<882;i+=2){Assert(!Single.IsNaN(changed[i])&&Math.Abs(changed[i])<=1,"Pitch produced invalid samples");if(previous<0&&changed[i]>=0)crossings++;previous=changed[i];energy+=changed[i]*changed[i];}}
  double frequency=crossings/1.39;Assert(frequency>390&&frequency<490&&energy>1,"Pitch did not shift 220 Hz to 440 Hz: "+frequency);
  using(var runtime=new GraphRuntime()){
   runtime.ApplyAsync(plan).GetAwaiter().GetResult();Assert(runtime.SourceStarts==2,"Input startup count");
   var bypassVoice=voice.Copy();bypassVoice.Bypass=true;Assert(runtime.TryUpdateEffect(bypassVoice),"Live effect rejected");bypassVoice.Bypass=false;
   var current=(GraphPlan)typeof(GraphRuntime).GetField("plan",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(runtime);Assert(current.Modules.Single(m=>m.Id==voice.Id).Bypass,"Live config not copied");
   Assert(!runtime.TryUpdateEffect(recorder),"Recorder accepted as live DSP");Assert(!runtime.TryUpdateEffect(new ModuleSpec{Id="m:missing",Kind="volume"}),"Missing DSP accepted");Assert(runtime.SourceStarts==2,"Parameter update reopened source");runtime.TryUpdateEffect(voice);

   for(int n=0;n<30;n++){runtime.FeedTest("test:a",BlockProcessor.ToPcm(a));runtime.FeedTest("test:b",BlockProcessor.ToPcm(b));Thread.Sleep(10);}
   var select=new ModuleSpec{Id=mixer.Id,Kind="mixer",Mode="select",SelectedSource="test:b"};runtime.UpdateModules(new[]{select,voice,recorder});runtime.ApplyAsync(GraphPlan.Create(edges,new[]{select,voice,recorder})).GetAwaiter().GetResult();Assert(runtime.SourceStarts==2,"Reapply reopened existing inputs");
   for(int n=0;n<20;n++){runtime.FeedTest("test:a",BlockProcessor.ToPcm(a));runtime.FeedTest("test:b",BlockProcessor.ToPcm(b));Thread.Sleep(10);}
   runtime.StopAsync().GetAwaiter().GetResult();Assert(runtime.Snapshot().Length==0,"Stop leaked sources");
  }
  var files=Directory.GetFiles(folder,"*.wav");Assert(files.Length==1,"Recording did not create one unique WAV");using(var wav=new WaveFileReader(files[0]))Assert(wav.Length>17640&&wav.WaveFormat.SampleRate==44100&&wav.WaveFormat.BitsPerSample==16,"Invalid finalized WAV");
  string direct=Path.Combine(folder,"direct");var directRecord=new ModuleSpec{Id="m:direct",Kind="record",Path=direct};var directPlan=GraphPlan.Create(new[]{Edge("test:pcm","m:direct")},new[]{directRecord});byte[] original=new byte[1764];for(int i=0;i<original.Length;i+=2){original[i]=0x34;original[i+1]=0x12;}
  using(var runtime=new GraphRuntime()){runtime.ApplyAsync(directPlan).GetAwaiter().GetResult();for(int n=0;n<35;n++){runtime.FeedTest("test:pcm",original);Thread.Sleep(10);}runtime.StopAsync().GetAwaiter().GetResult();}
  int valid=0;using(var wav=new WaveFileReader(Directory.GetFiles(direct,"*.wav").Single())){var bytes=new byte[(int)wav.Length];wav.Read(bytes,0,bytes.Length);for(int i=0;i<bytes.Length;i+=2){short value=BitConverter.ToInt16(bytes,i);Assert(value==0||value==0x1234,"Direct graph changed PCM samples");if(value!=0)valid++;}}Assert(valid>8820,"Too few direct PCM samples");
  Console.WriteLine("PASS: application identity/restart/ambiguity, stereo gain ramp, EQ bypass, live effect isolation, mixer/select, DAG rejection, pitch frequency, WAV finalization, input reuse and cleanup.");
 }
 public static void Network(){int port;using(var probe=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))port=((IPEndPoint)probe.Client.LocalEndPoint).Port;long bytes=0;bool mismatch=false;string error=null;byte[] pcm=new byte[1764];for(int i=0;i<pcm.Length;i+=2){pcm[i]=0x34;pcm[i+1]=0x12;}
  using(var rx=new RtpReceiver("127.0.0.1",port,96,(data,n)=>{Interlocked.Add(ref bytes,n);for(int i=0;i<n;i+=2)if(data[i]!=0x34||data[i+1]!=0x12)mismatch=true;},e=>error=e.Message)){
   using(var tx=new RtpSender("127.0.0.1",port,96)){for(int i=0;i<30;i++){tx.Add(pcm,pcm.Length);Thread.Sleep(10);}Thread.Sleep(150);Assert(bytes>30000&&!mismatch&&error==null,"RTP PCM roundtrip failed");}
  }
  using(var reused=new UdpClient(new IPEndPoint(IPAddress.Loopback,port))){}
  ushort seq;uint ssrc;Assert(RtpReceiver.Decode(new byte[]{0x80},96,out seq,out ssrc)==null,"Short RTP accepted");Console.WriteLine("PASS: localhost RTP L16 byte order, receive buffering and port cleanup.");
 }
}
