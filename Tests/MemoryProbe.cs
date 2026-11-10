using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AirBridge.Routing;
class MemoryProbe {
 static string path;static object view;static Type type;static readonly List<WeakReference> weak=new List<WeakReference>();
 [DllImport("user32.dll")]static extern int GetGuiResources(IntPtr process,int flag);
 static object Call(string name,params object[] args){return type.GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(view,args);}
 static void Sample(string phase,int iteration){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();using(var p=Process.GetCurrentProcess()){p.Refresh();string line=String.Join(",",new[]{DateTime.UtcNow.ToString("o"),phase,iteration.ToString(),(GC.GetTotalMemory(true)/1048576d).ToString("F3",System.Globalization.CultureInfo.InvariantCulture),(p.PrivateMemorySize64/1048576d).ToString("F3",System.Globalization.CultureInfo.InvariantCulture),p.HandleCount.ToString(),p.Threads.Count.ToString(),GetGuiResources(p.Handle,0).ToString(),GetGuiResources(p.Handle,1).ToString(),weak.Count(w=>w.IsAlive).ToString()});File.AppendAllText(path,line+Environment.NewLine);Console.WriteLine(line);}}
 static async Task Ui(){Window host=null;try{var asm=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"AirBridge.exe"));type=asm.GetType("AirBridge.Desktop.MainView");var st=Activator.CreateInstance(asm.GetType("AirBridge.Desktop.SettingsStore"),new object[]{Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"memory-fixture"),true});view=Activator.CreateInstance(type,new object[]{st,null,false});var original=(Window)type.GetField("Window").GetValue(view);var content=original.Content;original.Content=null;host=new Window{Content=content,Resources=original.Resources,Width=1080,Height=820,Left=-20000,Top=-20000,ShowInTaskbar=false,ShowActivated=false};host.Show();
  for(int batch=0;batch<11;batch++){for(int i=0;i<30;i++){
   foreach(string page in new[]{"Devices","Audio","General","Hotkeys","Settings","Info"})Call("Navigate",page);
   var panel=(StackPanel)original.FindName("GeneralOptions");if(panel.Children.Count>0)weak.Add(new WeakReference(panel.Children[0]));Call("BuildPreferences");Call("RefreshGraph");Call("RenderDevices");
   if(i%3==0){var tray=(Window)Call("BuildTrayPanel");weak.Add(new WeakReference(tray));tray.Left=-20000;tray.Top=-20000;tray.ShowActivated=false;tray.Show();tray.Close();}
   await Task.Delay(5);host.UpdateLayout();
  }await Task.Delay(600);Sample("ui",(batch+1)*30);}
 }catch(Exception ex){File.AppendAllText(path,"ERROR "+ex+Environment.NewLine);Environment.ExitCode=1;}finally{if(host!=null)host.Close();if(view!=null)Call("Shutdown");else Application.Current.Shutdown();}}
 static void Workers(){for(int batch=0;batch<11;batch++){for(int i=0;i<10;i++){var a=new PhoneWorkerClient("fixture","diagnostic-pcm",(b,n)=>{},null);weak.Add(new WeakReference(a));a.Start();var t=Stopwatch.StartNew();while(a.Frames<3&&t.ElapsedMilliseconds<3000)Thread.Sleep(5);if(a.Frames<3)throw new Exception("No synthetic frames");a.Dispose();}Thread.Sleep(100);Sample("workers",(batch+1)*10);}}
 [STAThread]static int Main(string[] args){path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"memory-"+args[0]+".csv");File.WriteAllText(path,"UTC,Phase,Iteration,ManagedMB,PrivateMB,Handles,Threads,GDI,USER,AliveWeakReferences\n");if(args[0]=="ui"){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Dispatcher.BeginInvoke(new Action(async()=>await Ui()));app.Run();return Environment.ExitCode;}try{Workers();return 0;}catch(Exception ex){Console.WriteLine(ex);return 1;}}
}
