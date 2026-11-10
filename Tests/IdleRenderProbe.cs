using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using AirBridge.Routing;
// Runs only the software render loop on an empty graph. No native loop or devices.
class IdleRenderProbe {
 static int Main(){try{Run();return 0;}catch(Exception ex){Console.WriteLine("FAIL "+ex.GetType().Name+": "+ex.Message);return 1;}}
 static void Run(){var flags=BindingFlags.Instance|BindingFlags.NonPublic;
  using(var graph=(GraphRuntime)Activator.CreateInstance(typeof(GraphRuntime),flags,null,new object[]{false},null)){
   var thread=new Thread(()=>typeof(GraphRuntime).GetMethod("Render",flags).Invoke(graph,null));thread.IsBackground=true;
   using(var process=Process.GetCurrentProcess()){
    thread.Start();Thread.Sleep(200);var before=process.TotalProcessorTime;var watch=Stopwatch.StartNew();Thread.Sleep(5000);
    var cpu=(process.TotalProcessorTime-before).TotalMilliseconds;
    if(graph.RenderProgress!=0)throw new Exception("Idle graph generated audio");
    var gate=typeof(GraphRuntime).GetField("gate",flags).GetValue(graph);
    var plan=typeof(GraphRuntime).GetField("plan",flags);
    lock(gate)plan.SetValue(graph,GraphPlan.Create(new[]{new GraphEdge{Source="test:clock",Target="o:offline",Gain=1}},new ModuleSpec[0]));
    Thread.Sleep(1000);long blocks=graph.RenderProgress;
    if(blocks<40||blocks>120)throw new Exception("Software-only graph clock: "+blocks);
    var outputs=(System.Collections.IDictionary)typeof(GraphRuntime).GetField("outputs",flags).GetValue(graph);
    lock(gate)outputs.Add("o:sentinel",null);
    long parked=graph.RenderProgress;Thread.Sleep(200);
    if(graph.RenderProgress!=parked)throw new Exception("Software clock consumed a hardware-output graph");
    lock(gate)outputs.Clear();Thread.Sleep(200);
    if(graph.RenderProgress<=parked)throw new Exception("Software clock did not resume");
    typeof(GraphRuntime).GetField("stopping",flags).SetValue(graph,true);
    if(!thread.Join(500))throw new Exception("Idle renderer did not stop promptly");
    Console.WriteLine("PASS idle/software-only/hardware handoff/resume/stop; software_blocks_per_second="+blocks);
    Console.WriteLine("Idle render sample: cpu_ms="+cpu+" duration_ms=5000 (timer precision may report zero)");
   }
  }
 }
}
