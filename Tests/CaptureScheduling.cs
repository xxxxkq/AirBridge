using System;
using System.Collections.Generic;
using System.Reflection;
using AirBridge.Routing;
class CaptureSchedulingTests {
 static void Main(){
  var bins=new CaptureFrameBins();for(int i=0;i<500;i++)bins.Frame(i*10);
  if(bins.Finish(5000)!="100,100,100,100,100")throw new Exception("Normal bins");
  for(int i=0;i<200;i++)bins.Frame(8000+i*10);
  if(bins.Finish(10000)!="0,0,0,100,100")throw new Exception("Pause/resume bins");
  if(bins.Finish(15000)!="0,0,0,0,0")throw new Exception("Paused bins");
  Console.WriteLine("PASS normal, pause/resume and idle 1-second counts");
  var type=typeof(GraphRuntime).Assembly.GetType("AirBridge.Routing.CaptureThreadScheduling");var lines=new List<string>();
  Action<string> log=s=>{lines.Add(s);Console.WriteLine(s);};string previous=Environment.GetEnvironmentVariable("AIRBRIDGE_CAPTURE_MMCSS");
  try{
   Environment.SetEnvironmentVariable("AIRBRIDGE_CAPTURE_MMCSS","0");using(var lease=(IDisposable)Activator.CreateInstance(type,new object[]{log})){}
   if(!lines.Contains("capture-scheduling disabled"))throw new Exception("Disabled path");lines.Clear();
   Environment.SetEnvironmentVariable("AIRBRIDGE_CAPTURE_MMCSS",null);
   try{using(var lease=(IDisposable)Activator.CreateInstance(type,new object[]{log})){throw new ApplicationException("simulated capture failure");}}catch(ApplicationException){}
   bool enabled=lines.Exists(s=>s.Contains("enabled task="));
   if(enabled&&!lines.Contains("capture-scheduling reverted"))throw new Exception("Scheduling not reverted after exception");
   if(!enabled&&!lines.Exists(s=>s.Contains("unavailable")))throw new Exception("Missing fallback result");
   Console.WriteLine(enabled?"PASS native task registered and reverted on exception":"PASS unavailable fallback; native scheduling not verified");
  }finally{Environment.SetEnvironmentVariable("AIRBRIDGE_CAPTURE_MMCSS",previous);}
  Console.WriteLine("No Bluetooth or audio endpoint opened; live processes unchanged");
 }
}
