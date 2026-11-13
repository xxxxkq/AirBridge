using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using AirBridge.Routing;
using NAudio.Wave;

// Deterministic PCM-only tests: never open Bluetooth, WASAPI, or a system endpoint.
class IPhonePipeline {
 static readonly BindingFlags All=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static readonly Type Source=typeof(GraphRuntime).GetNestedType("Source",BindingFlags.NonPublic);
 static readonly Type Output=typeof(GraphRuntime).GetNestedType("Output",BindingFlags.NonPublic);
 static int passed;
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
 static object Field(object o,string name){return o.GetType().GetField(name,All).GetValue(o);}
 static void Set(object o,string name,object value){o.GetType().GetField(name,All).SetValue(o,value);}
 static object Call(object o,string name,params object[] args){return o.GetType().GetMethod(name,All).Invoke(o,args);}
 sealed class PullProvider:IWaveProvider {readonly object output;public PullProvider(object value){output=value;}public WaveFormat WaveFormat{get{return new WaveFormat(44100,16,2);}}public int Read(byte[] data,int offset,int count){return (int)Call(output,"Read",data,offset,count);}}
 static byte[] Tone(){var b=new byte[1764];for(int i=0;i<b.Length;i+=2)b[i+1]=16;return b;}
 static object NewSource(){var s=Activator.CreateInstance(Source,All,null,new object[]{"p:offline",null},null);var b=new BufferedWaveProvider(new WaveFormat(44100,16,2)){BufferDuration=TimeSpan.FromSeconds(2),ReadFully=true};Set(s,"buffer",b);Set(s,"sample",b.ToSampleProvider());Call(s,"ConfigureJitter",10,false);return s;}
 static void Put(object s){Call(s,"Inject",Tone());}
 static AudioReadMetrics Metrics(object o){return (AudioReadMetrics)Field(o,"Metrics");}
 static bool SourceRead(object s){return ((float[])Call(s,"Read")).Any(x=>x!=0);}
 static GraphRuntime NewGraph(object s,params string[] ids){var g=(GraphRuntime)Activator.CreateInstance(typeof(GraphRuntime),All,null,new object[]{false},null);((IDictionary)Field(g,"sources")).Add("p:offline",s);var map=(IDictionary)Field(g,"outputs");foreach(var id in ids)map.Add(id,Activator.CreateInstance(Output,All,null,new object[]{id,g},null));Set(g,"plan",GraphPlan.Create(ids.Select(id=>new GraphEdge{Source="p:offline",Target=id,Gain=1}).ToArray(),new ModuleSpec[0]));return g;}
 static object Out(GraphRuntime g,string id){return ((IDictionary)Field(g,"outputs"))[id];}
 static byte[] Pull(object o,int count=1764){var b=new byte[count];Call(o,"Read",b,0,count);return b;}
 static bool Signal(byte[] b){return b.Any(x=>x!=0);}
 static void Main(string[] args){
  var s=NewSource();SourceRead(s);Check(Metrics(s).StartupSilenceBytes==1764&&Metrics(s).MissingBytes==0,"startup silence separate");Put(s);SourceRead(s);for(int i=0;i<10;i++)SourceRead(s);Check(Metrics(s).MissingBytes==17640&&Metrics(s).MissingEvents==1&&Metrics(s).MissingBlocks==10,"100ms shortage counts every zero-filled read");Put(s);Check(SourceRead(s),"paused source resumes at 10ms without reconnect");SourceRead(s);Check(Metrics(s).MissingEvents==2,"separate shortage episodes");
  var overflow=NewSource();for(int i=0;i<52;i++)Put(overflow);Check(Metrics(overflow).QueueClearCount==0&&Metrics(overflow).QueueTrimCount==2&&Metrics(overflow).QueueDroppedBytes==40*1764,"input finite trimming has exact loss counter");
  using(var log=new AsyncAudioLog(4)){var entered=new ManualResetEvent(false);var release=new ManualResetEvent(false);log.Post(()=>{entered.Set();release.WaitOne();});entered.WaitOne();var watch=Stopwatch.StartNew();for(int i=0;i<1000;i++)log.Post(()=>{});Check(watch.ElapsedMilliseconds<1000&&log.Dropped==996,"blocked log sink does not block producer; bounded drops counted");release.Set();Check(log.Flush(2000),"asynchronous log drains");log.Post(()=>{throw new Exception("simulated disk error");});Check(log.Flush(2000)&&log.Errors==1,"log I/O failure isolated");}
  if(args.Contains("stats-only")){Console.WriteLine("PASS total="+passed);return;}
  var normal=NewSource();var g=NewGraph(normal,"o:A");var a=Out(g,"o:A");for(int i=0;i<100;i++){Put(normal);if(!Signal(Pull(a)))throw new Exception("normal signal");}Check(Metrics(normal).MissingBytes==0,"100 normal frames, no invented silence");
  // A logical old software tick passes without data. No output request yet: do not consume.
  Check((long)Field(a,"Bytes")==100*1764,"no eager output queue filling");Put(normal);Check(Signal(Pull(a))&&Metrics(normal).MissingBytes==0,"late frame before output deadline survives");
  for(int i=0;i<100;i++){if(i%10!=9)Put(normal);Pull(a);}Check(Metrics(normal).MissingBytes==10*1764&&Metrics(normal).MissingEvents==10,"sustained 10 percent supply deficit remains visible");
  for(int i=0;i<100;i++)Pull(a);Put(normal);Check(Signal(Pull(a))&&Metrics(normal).MissingBytes==110*1764,"one second pause and immediate resume, no reconnection");
  var partial=NewSource();var pg=NewGraph(partial,"o:A");Put(partial);Check(Signal(Pull(Out(pg,"o:A"),880))&&Signal(Pull(Out(pg,"o:A"),884))&&Metrics(partial).MissingBytes==0,"partial output reads reuse one source block");
  var multi=NewSource();var mg=NewGraph(multi,"o:A","o:B");Put(multi);Check(Pull(Out(mg,"o:A")).SequenceEqual(Pull(Out(mg,"o:B")))&&Metrics(multi).MissingBytes==0,"two outputs share one source read");
  var old=Out(g,"o:A");var replacement=Activator.CreateInstance(Output,All,null,new object[]{"A",g},null);((IDictionary)Field(g,"outputs"))["o:A"]=replacement;Put(normal);Check(!Signal(Pull(old))&&Signal(Pull(replacement)),"replaced output cannot consume source frames");
  Call(replacement,"Add",Tone());var shortRead=new byte[3528];Call(replacement,"ReadBuffered",shortRead,0,shortRead.Length);Check(Metrics(replacement).MissingBytes==1764,"output short-read counts actual missing bytes");
  for(int i=0;i<42;i++)Call(replacement,"Add",Tone());Check(Metrics(replacement).QueueOverflowCount==1&&Metrics(replacement).QueueDroppedBytes==41*1764,"output queue clearing has exact loss counter");
  var adapted=NewSource();var ag=NewGraph(adapted,"o:A","o:B");var converted=OutputFormatAdapter.Convert(new PullProvider(Out(ag,"o:A")),new WaveFormatExtensible(48000,32,2));var convertedBytes=new byte[480*8];
  for(int i=0;i<100;i++){Put(adapted);converted.Read(convertedBytes,0,convertedBytes.Length);Pull(Out(ag,"o:B"));}Check(Metrics(adapted).MissingBytes==0,"48k conversion and 44.1k output share graph frames without new shortages");
  for(int i=0;i<10;i++){converted.Read(convertedBytes,0,convertedBytes.Length);Pull(Out(ag,"o:B"));}Check(Enumerable.Range(0,convertedBytes.Length/4).All(i=>Math.Abs(BitConverter.ToSingle(convertedBytes,i*4))<0.000001),"converted output decays to silence during pause without repeating old samples");Put(adapted);converted.Read(convertedBytes,0,convertedBytes.Length);Check(Enumerable.Range(0,convertedBytes.Length/4).Any(i=>Math.Abs(BitConverter.ToSingle(convertedBytes,i*4))>0.0001),"converted output resumes without reopening the source");
  Console.WriteLine("PASS total="+passed+"; buffer=10ms/adaptive=false; hardware never opened");
 }
}
