using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Runtime.InteropServices;
namespace AirBridge.Desktop {
 internal static class Program {
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SetCurrentProcessExplicitAppUserModelID(string id);
  [STAThread]static int Main(string[] args){if(args.Length==5&&args[0]=="--output-watch")return OutputRecovery.Watch(args);if(args.Length==2&&args[0]=="--phone-worker"){try{return AirBridge.Routing.PhoneWorkerHost.Run(args[1]);}finally{AirBridge.Routing.AudioTimingTrace.Close();}}AudioExperiment.Enabled=args.Contains("--audio-ab-test");bool test=args.Contains("--self-test");string root=test?Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test-data"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AirBridge");Mutex mutex=null;EventWaitHandle signal=null;MainView view=null;
   try{SetCurrentProcessExplicitAppUserModelID("AirBridge.Desktop");if(!test){bool created;mutex=new Mutex(true,@"Local\AirBridge.Desktop.v1",out created);signal=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\AirBridge.Show.v1");if(!created){signal.Set();return 0;}}
    int comparisonIndex=Array.IndexOf(args,"--output-compare");if(comparisonIndex>=0){if(test||AudioExperiment.Enabled||comparisonIndex+1>=args.Length)throw new ArgumentException("Invalid comparison command");OutputComparisonCommand.Prepare(args[comparisonIndex+1]);}
    Directory.CreateDirectory(root);if(!test&&File.Exists(Path.Combine(root,"settings.json"))&&!File.Exists(Path.Combine(root,"settings-before-managed.json")))File.Copy(Path.Combine(root,"settings.json"),Path.Combine(root,"settings-before-managed.json"));
    if(!test)OutputRecovery.RestoreStartup(root);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};view=new MainView(new SettingsStore(root,test),signal,args.Contains("--background"));app.DispatcherUnhandledException+=(s,e)=>{view.Error(e.Exception);if(test){view.TestFailed=true;File.AppendAllText(Path.Combine(root,"unhandled-test-error.log"),e.Exception.ToString());}e.Handled=true;};app.Run(view.Window);return view.TestFailed?1:0;
   }catch(Exception ex){if(view!=null)try{view.Shutdown();}catch{}Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"launcher-error.log"),ex.ToString());if(!test)MessageBox.Show(ex.GetBaseException().Message+"\n"+root,"AirBridge");return 1;}
   finally{AirBridge.Routing.AudioTimingTrace.Close();if(signal!=null)signal.Dispose();if(mutex!=null){try{mutex.ReleaseMutex();}catch{}mutex.Dispose();}}
  }
 }
}
