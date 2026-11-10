using System;using System.Diagnostics;using System.IO;using System.Linq;using System.Threading;using AirBridge.Routing;
class TimingTraceTests {
 static void Main(string[] args){
  if(args.Length==0){if(AudioTimingTrace.Enabled||AudioTimingTrace.Now()!=0)throw new Exception("Trace must be off by default");Console.WriteLine("PASS trace off by default");return;}
  string dir=Path.GetFullPath(args[0]);Directory.CreateDirectory(dir);Environment.SetEnvironmentVariable("AIRBRIDGE_TIMING_DIRECTORY",dir);
  if(!AudioTimingTrace.Enabled)throw new Exception("Trace unavailable");var watch=Stopwatch.StartNew();
  for(int i=0;i<2000;i++)AudioTimingTrace.Record("synthetic", "test,quoted\"key",AudioTimingTrace.Now(),i,1764,0);
  var elapsed=watch.Elapsed.TotalMilliseconds;
  var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
  var graph=Activator.CreateInstance(typeof(GraphRuntime),flags,null,new object[]{false},null);
  var outputType=typeof(GraphRuntime).GetNestedType("Output",System.Reflection.BindingFlags.NonPublic);
  var output=Activator.CreateInstance(outputType,flags,null,new object[]{"offline",graph},null);
  var outputs=(System.Collections.IDictionary)typeof(GraphRuntime).GetField("outputs",flags).GetValue(graph);outputs.Add("o:offline",output);
  object gate=typeof(GraphRuntime).GetField("gate",flags).GetValue(graph);var started=new ManualResetEvent(false);Exception failure=null;
  var reader=new Thread(()=>{try{started.Set();outputType.GetMethod("Read",flags).Invoke(output,new object[]{new byte[1764],0,1764});}catch(Exception ex){failure=ex;}});
  lock(gate){reader.Start();started.WaitOne();Thread.Sleep(60);}reader.Join();if(failure!=null)throw failure;
  File.WriteAllText(Path.Combine(dir,"trace-stop"),"stop");
  for(int i=0;i<100&&AudioTimingTrace.Enabled;i++)Thread.Sleep(25);
  if(AudioTimingTrace.Enabled)throw new Exception("stop marker failed");Thread.Sleep(100);AudioTimingTrace.Close();
  var text=File.ReadAllText(Directory.GetFiles(dir,"timing-*.csv").Single());
  if(!text.Contains("# frequency=")||!text.Contains("# dropped=0,errors=0")||text.Split('\n').Count(s=>s.Contains("synthetic"))!=2000)throw new Exception("Trace incomplete");
  var lockLine=text.Split('\n').First(s=>s.Contains("\"graph-lock\""));var fields=lockLine.Split(',');double waitMs=(Int64.Parse(fields[1])-Int64.Parse(fields[0]))*1000.0/Stopwatch.Frequency;
  if(waitMs<20)throw new Exception("Injected graph lock delay was not measured");
  Console.WriteLine("PASS 2000 numeric events, escaped keys, QPC metadata, automatic stop and complete footer; producer ms="+elapsed.ToString("F2")+"; measured injected graph lock ms="+waitMs.ToString("F2"));
 }
}
