using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AirBridge.Routing;
class PhoneWorkers {
 static int checks;static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);checks++;}
 static bool Alive(int pid){try{using(var p=Process.GetProcessById(pid))return !p.HasExited;}catch(ArgumentException){return false;}}
 static void Until(Func<bool> test){var timer=Stopwatch.StartNew();while(!test()&&timer.ElapsedMilliseconds<6000)Thread.Sleep(20);}
 static int Main(string[] args){try{
  if(args.Length==2&&args[0]=="orphan"){var owned=new PhoneWorkerClient("test","diagnostic-hang",null,null);owned.Start();File.WriteAllText(args[1],owned.ProcessId.ToString());Thread.Sleep(Timeout.Infinite);return 0;}
  PhoneWorkerClient.DiagnosticPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"lifecycle-test.log");File.WriteAllText(PhoneWorkerClient.DiagnosticPath,"");
  var slow=new PhoneWorkerClient("cancel","diagnostic-slow",null,null);var pending=Task.Run(()=>{try{slow.Start();}catch(OperationCanceledException){}catch(ObjectDisposedException){}});Until(()=>slow.ProcessId!=0);slow.Dispose();Check(pending.Wait(6000)&&!slow.Connected,"cancelled opening cannot publish late ready");
  int bad=0,callbacks=0;Action<byte[],int> receive=(data,count)=>{for(int i=0;i<count;i+=2)if(data[i]!=0x34||data[i+1]!=0x12)Interlocked.Increment(ref bad);Interlocked.Increment(ref callbacks);};
  var a=new PhoneWorkerClient("A","diagnostic-pcm",receive,null);var b=new PhoneWorkerClient("B","diagnostic-pcm",receive,null);
  try{Task.WaitAll(Task.Run(()=>a.Start()),Task.Run(()=>b.Start()));Until(()=>a.Frames>=25&&b.Frames>=25);Console.WriteLine("A="+a.Frames+" "+a.Error+" B="+b.Frames+" "+b.Error);Check(a.Frames>=25&&b.Frames>=25,"two isolated streams deliver frames");Check(a.ProcessId!=b.ProcessId,"different owners");Check(bad==0&&callbacks>0&&a.Peak==0x1234&&a.NonZeroSamples>0&&a.NonZeroSamples%882==0,"full PCM payload and metrics preserved");
   int pid=a.ProcessId;using(var p=Process.GetProcessById(pid))p.Kill();Until(()=>a.Error!="");Check(a.Error!=""&&!a.Connected,"worker failure surfaced without terminating parent");long before=b.Frames;Until(()=>b.Frames>=before+20);Check(b.Frames>=before+20&&b.Connected,"other phone unaffected by worker crash");
   pid=b.ProcessId;b.Dispose();Check(!Alive(pid),"graceful release leaves no worker");b.Dispose();
  }finally{a.Dispose();b.Dispose();}
  using(var exiting=new PhoneWorkerClient("exit","diagnostic-exit",null,null)){try{exiting.Start();}catch{}Until(()=>exiting.Error!="");Check(exiting.Error!="","unexpected native-style process exit reported");}
  var stuck=new PhoneWorkerClient("hang","diagnostic-hang",null,null);stuck.Start();int hanging=stuck.ProcessId;var watch=Stopwatch.StartNew();stuck.Dispose();Check(!Alive(hanging)&&watch.ElapsedMilliseconds<6000,"hung shutdown forcibly bounded to owned worker");
  string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"orphan-worker.pid");if(File.Exists(file))File.Delete(file);
  using(var parent=Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"orphan \""+file+"\""){UseShellExecute=false,CreateNoWindow=true})){
   try{Until(()=>File.Exists(file));Check(File.Exists(file),"child assigned to parent job");int orphan=Int32.Parse(File.ReadAllText(file));parent.Kill();parent.WaitForExit(5000);Until(()=>!Alive(orphan));Check(!Alive(orphan),"parent crash kills worker automatically");}finally{if(!parent.HasExited)parent.Kill();if(File.Exists(file))File.Delete(file);}
  }
  var hungA=new PhoneWorkerClient("shutdown-A","diagnostic-hang",null,null);var hungB=new PhoneWorkerClient("shutdown-B","diagnostic-hang",null,null);hungA.Start();hungB.Start();int pidA=hungA.ProcessId,pidB=hungB.ProcessId;var closingWatch=Stopwatch.StartNew();var closing=PhoneWorkerClient.ShutdownAllAsync();Check(!closing.IsCompleted,"shutdown returns without blocking caller");closing.GetAwaiter().GetResult();Check(!Alive(pidA)&&!Alive(pidB)&&closingWatch.ElapsedMilliseconds<6500,"serialized hung owners release within bounded total budget");Console.WriteLine("Shutdown elapsed ms="+closingWatch.ElapsedMilliseconds);using(var lateClient=new PhoneWorkerClient("late","diagnostic-pcm",null,null)){bool rejected=false;try{lateClient.Start();}catch(ObjectDisposedException){rejected=true;}Check(rejected,"late worker rejected after shutdown");}
  Check(PhoneWorkerClient.FlushDiagnostics(2000),"diagnostics flushed off audio thread");
  var owners=new System.Collections.Generic.Stack<string>();bool serial=true;foreach(string line in File.ReadAllLines(PhoneWorkerClient.DiagnosticPath)){string gen=System.Text.RegularExpressions.Regex.Match(line,"generation=([0-9]+)").Groups[1].Value;if(line.Contains("control-open begin")||line.Contains("control-close begin")){if(owners.Count>0&&owners.Peek()!=gen)serial=false;owners.Push(gen);}if(line.Contains("control-open end")||line.Contains("control-close end")){if(owners.Count==0||owners.Pop()!=gen)serial=false;}}Check(serial&&owners.Count==0,"different owners never overlap control operations");
  Console.WriteLine("Passed "+checks+" checks; no audio hardware opened.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
