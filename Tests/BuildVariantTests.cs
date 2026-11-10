using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
class BuildVariantTests {
 const BindingFlags All=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static void Check(bool value,string detail){if(!value)throw new Exception(detail);Console.WriteLine("PASS "+detail);}
 [STAThread]static int Main(string[] args){try{if(args[0]=="runtime")Runtime(args[1],args[2]=="Development");else Bundle(args[1],args[2]);return 0;}catch(Exception ex){Console.WriteLine("FAIL "+ex);return 1;}}
 static void Runtime(string root,bool dev){
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string file=Path.Combine(root,new AssemblyName(e.Name).Name+".dll");return File.Exists(file)?Assembly.LoadFrom(file):null;};
  var exe=Assembly.LoadFrom(Path.Combine(root,"AirBridge.exe"));var routing=Assembly.LoadFrom(Path.Combine(root,"AirBridge.Routing.dll"));
  Check((exe.GetType("AirBridge.Desktop.AudioExperiment")!=null)==dev,"experiment type follows build variant");
  Check((exe.GetType("AirBridge.Desktop.MainView").GetMethod("SelfTest",All)!=null)==dev,"UI self-test follows build variant");
  Check((exe.GetType("AirBridge.Desktop.MainView").GetMethod("MigrationChecks",All)!=null)==dev,"UI fixture methods follow build variant");
  Check((routing.GetType("AirBridge.Routing.OutputComparisonRunner")!=null)==dev,"automatic output comparison follows build variant");
  var worker=routing.GetType("AirBridge.Routing.PhoneWorkerClient");
  if(!dev){try{Activator.CreateInstance(worker,new object[]{"test","diagnostic-pcm",null,null});throw new Exception("simulation accepted");}catch(TargetInvocationException ex){Check(ex.InnerException is ArgumentException,"Release rejects worker simulation before process creation");}}
  Check(exe.GetType("AirBridge.Desktop.OutputRecovery").GetMethod("Watch",All)!=null,"output recovery watcher retained");
  Check(worker.GetMethod("ShutdownAllAsync",All)!=null,"worker shutdown retained");
  var valid=exe.GetType("AirBridge.Desktop.Program").GetMethod("ValidReleaseArguments",All);
  foreach(string flag in new[]{"--self-test","--audio-ab-test","--output-compare"})Check(!(bool)valid.Invoke(null,new object[]{new[]{flag}}),"Release argument allowlist excludes "+flag);
  Check((bool)valid.Invoke(null,new object[]{new[]{"--background"}}),"background startup retained");
 }
 static void Bundle(string file,string cache){
  cache=Path.Combine(cache,Guid.NewGuid().ToString("N"));
  var host=Assembly.LoadFrom(file).GetType("AirBridge.Packaging.SingleFileHost");var prepare=host.GetMethod("PreparePayload",All);
  var paths=new string[3];Parallel.For(0,3,i=>paths[i]=(string)prepare.Invoke(null,new object[]{cache}));
  Check(paths.All(p=>p==paths[0]),"concurrent extraction reuses one verified runtime");
  string path=paths[0];Check(Directory.GetFiles(path,"*",SearchOption.AllDirectories).Length==15,"payload contains only 15 allowlisted runtime files");
  Check(File.Exists(Path.Combine(path,"UI","Shell.xaml"))&&File.Exists(Path.Combine(path,"AirBridge.Capture.dll")),"WPF resources and native capture retained");
  Check(File.Exists(Path.Combine(path,"LICENSE"))&&File.Exists(Path.Combine(path,"THIRD-PARTY-NOTICES.txt")),"third-party notices included");
  File.AppendAllText(Path.Combine(path,"README.txt"),"tamper");var repair=(string)prepare.Invoke(null,new object[]{cache});
  Check(repair!=path,"damaged cache is not reused or overwritten while potentially loaded");
  Check(!File.ReadAllText(Path.Combine(repair,"README.txt")).EndsWith("tamper"),"repair payload verified");
  var setup=new AppDomainSetup{ApplicationBase=repair,ConfigurationFile=Path.Combine(repair,"AirBridge.exe.config")};
  var domain=AppDomain.CreateDomain("PayloadEntryCheck",null,setup);
  try{Check(domain.ExecuteAssembly(Path.Combine(repair,"AirBridge.exe"),new[]{"--self-test"})==2,"extracted runtime loads in isolated domain and rejects test entry without UI");}finally{AppDomain.Unload(domain);}
  var valid=host.GetMethod("ValidArguments",All);Check(!(bool)valid.Invoke(null,new object[]{new[]{"--self-test"}}),"single-file launcher rejects development entry");
  Console.WriteLine("Bundle test cache: "+cache);
 }
}
