using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using AirBridge.Routing;
namespace AirBridge.Desktop {
 internal static class OutputRecovery {
  const string Gate=@"Local\AirBridge.DefaultOutputRecovery.v1";static Process watcher;static bool watcherReady;
  static string Journal(string root){return Path.Combine(root,"AirBridge-default-output-recovery.xml");}
  internal static string UpdateWatcherStamp(){return UpdateInstaller.Stamp(watcher);}
  static bool Acquire(Mutex gate,int timeout){try{return gate.WaitOne(timeout);}catch(AbandonedMutexException){return true;}}
  static void Restore(string root){
   Exception failure=null;
   try{new NativeAppRoutes(Path.Combine(root,"AirBridge-app-routing-recovery.xml")).Restore();}catch(Exception ex){failure=ex;}
   try{new DefaultOutputRoute(Journal(root)).Restore();}catch(Exception ex){failure=ex;}
   if(failure!=null)throw failure;
  }
  public static void RestoreStartup(string root){using(var gate=new Mutex(false,Gate)){if(!Acquire(gate,10000))throw new InvalidOperationException("上次输出恢复仍在进行，请稍后启动。 / Previous output recovery is still running.");try{try{Restore(root);}catch(Exception ex){File.AppendAllText(Path.Combine(root,"output-recovery-error.log"),DateTime.Now.ToString("o")+" startup restore pending: "+ex+Environment.NewLine);}}finally{gate.ReleaseMutex();}}}
  public static void Ensure(string root){if(watcher!=null&&!watcher.HasExited){if(watcherReady)return;throw new InvalidOperationException("输出恢复守护进程尚未确认就绪。 / Recovery watcher has not confirmed readiness.");}if(watcher!=null)watcher.Dispose();watcherReady=false;string name=@"Local\AirBridge.OutputReady."+Guid.NewGuid().ToString("N");using(var ready=new EventWaitHandle(false,EventResetMode.ManualReset,name))using(var self=Process.GetCurrentProcess()){
   watcher=Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"AirBridge.exe"),"--output-watch "+self.Id+" "+self.StartTime.ToUniversalTime().Ticks+" \""+root+"\" "+name){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
   if(!ready.WaitOne(5000)||watcher.HasExited)throw new InvalidOperationException("输出恢复守护进程未就绪，未接管系统输出。 / Output recovery watcher is unavailable.");
   watcherReady=true;
  }}
  public static int Watch(string[] args){string root=args[3];try{using(var gate=new Mutex(false,Gate)){if(!Acquire(gate,10000))return 2;try{
    Process parent=null;try{parent=Process.GetProcessById(Int32.Parse(args[1]));if(parent.StartTime.ToUniversalTime().Ticks!=Int64.Parse(args[2])){parent.Dispose();parent=null;}}catch(ArgumentException){}
    using(var ready=EventWaitHandle.OpenExisting(args[4]))ready.Set();
    if(parent!=null)using(parent)parent.WaitForExit();
    Restore(root);
   }finally{gate.ReleaseMutex();}}return 0;}catch(Exception ex){try{File.AppendAllText(Path.Combine(root,"output-recovery-error.log"),DateTime.Now.ToString("o")+" "+ex+Environment.NewLine);}catch{}return 1;}}
 }
}
