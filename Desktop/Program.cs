using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Runtime.InteropServices;
namespace AirBridge.Desktop {
 internal static class Program {
  internal static bool ValidReleaseArguments(string[] args){return args.Length==0||(args.Length==1&&args[0]=="--background")||(args.Length==2&&(args[0]=="--phone-worker"||args[0]=="--apply-update"))||(args.Length==5&&args[0]=="--output-watch");}
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SetCurrentProcessExplicitAppUserModelID(string id);
  [STAThread]static int Main(string[] args){
#if !AIRBRIDGE_DEVELOPMENT
   if(!ValidReleaseArguments(args))return 2;
#endif
if(args.Length==2&&args[0]=="--apply-update")return UpdateInstaller.Apply(args[1]);if(args.Length==5&&args[0]=="--output-watch")return OutputRecovery.Watch(args);if(args.Length==2&&args[0]=="--phone-worker"){try{return AirBridge.Routing.PhoneWorkerHost.Run(args[1]);}finally{AirBridge.Routing.AudioTimingTrace.Close();}}
#if AIRBRIDGE_DEVELOPMENT
AudioExperiment.Enabled=args.Contains("--audio-ab-test");bool test=args.Contains("--self-test");
#else
bool test=false;
#endif
string root=test?Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test-data"):AirBridge.Storage.DataLocation.LegacyRoot;Mutex mutex=null;EventWaitHandle signal=null;MainView view=null;
   try{SetCurrentProcessExplicitAppUserModelID("AirBridge.Desktop");if(!test){bool created;mutex=new Mutex(true,@"Local\AirBridge.Desktop.v1",out created);signal=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\AirBridge.Show.v1");if(!created){signal.Set();return 0;}}
    if(!test){if(!String.IsNullOrEmpty(Environment.GetEnvironmentVariable(AirBridge.Storage.DataLocation.EnvironmentKey)))root=AirBridge.Storage.DataLocation.Current;else{var storage=AirBridge.Storage.DataLocation.Resolve(ApplicationLaunchPath.Current,AirBridge.Storage.DataLocation.LegacyRoot,true,AirBridge.Storage.DataLocation.RecoveryIsQuiet);root=storage.Root;Environment.SetEnvironmentVariable(AirBridge.Storage.DataLocation.EnvironmentKey,root);Environment.SetEnvironmentVariable("AIRBRIDGE_STORAGE_NOTICE",storage.Notice);}}
#if AIRBRIDGE_DEVELOPMENT
    int comparisonIndex=Array.IndexOf(args,"--output-compare");if(comparisonIndex>=0){if(test||AudioExperiment.Enabled||comparisonIndex+1>=args.Length)throw new ArgumentException("Invalid comparison command");OutputComparisonCommand.Prepare(args[comparisonIndex+1]);}
#endif
    Directory.CreateDirectory(root);if(!test&&File.Exists(Path.Combine(root,"settings.json"))&&!File.Exists(Path.Combine(root,"settings-before-managed.json")))File.Copy(Path.Combine(root,"settings.json"),Path.Combine(root,"settings-before-managed.json"));
    if(!test)OutputRecovery.RestoreStartup(root);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};view=new MainView(new SettingsStore(root,test),signal,args.Contains("--background"));app.DispatcherUnhandledException+=(s,e)=>{view.Error(e.Exception);if(test){view.TestFailed=true;File.AppendAllText(Path.Combine(root,"unhandled-test-error.log"),e.Exception.ToString());}e.Handled=true;};app.Run(view.Window);return view.TestFailed?1:0;
   }catch(Exception ex){if(view!=null)try{view.Shutdown();}catch{}Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"launcher-error.log"),ex.ToString());if(!test)MessageBox.Show(ex.GetBaseException().Message+"\n"+root,"AirBridge");return 1;}
   finally{AirBridge.Routing.AudioTimingTrace.Close();if(signal!=null)signal.Dispose();if(mutex!=null){try{mutex.ReleaseMutex();}catch{}mutex.Dispose();}}
  }
 }
}
