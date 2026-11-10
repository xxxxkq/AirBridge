#if AIRBRIDGE_DEVELOPMENT
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using AirBridge.Routing;

namespace AirBridge.Desktop {
 internal static class OutputComparisonCommand {
  internal static OutputComparisonConfig Config;internal static string DirectoryPath;internal static bool Pending,Finished;static DateTime deadline;
  internal static void Prepare(string path){
   var config=SettingsStore.Json.Deserialize<OutputComparisonConfig>(File.ReadAllText(path));config.Validate();
   var json=SettingsStore.Json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(path));object output;
   if(!json.TryGetValue("ResultDirectory",out output)||!Path.IsPathRooted(Convert.ToString(output)))throw new ArgumentException("ResultDirectory must be an absolute path");
   DirectoryPath=Path.GetFullPath(Convert.ToString(output));
   if(Directory.Exists(DirectoryPath)&&Directory.EnumerateFileSystemEntries(DirectoryPath).Any())throw new InvalidOperationException("对照目录必须为空，避免混入旧记录。");
   Directory.CreateDirectory(DirectoryPath);File.WriteAllText(Path.Combine(DirectoryPath,"config.json"),File.ReadAllText(path));
   File.WriteAllText(Path.Combine(DirectoryPath,"build-info.json"),File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"build-info.json")));
   Environment.SetEnvironmentVariable("AIRBRIDGE_TIMING_DIRECTORY",DirectoryPath);Config=config;Pending=true;deadline=DateTime.UtcNow.AddSeconds(120);
  }
  internal static void Report(string kind,object value){File.AppendAllText(Path.Combine(DirectoryPath,"comparison.jsonl"),SettingsStore.Json.Serialize(new{Kind=kind,Utc=DateTime.UtcNow.ToString("o"),Qpc=Stopwatch.GetTimestamp(),Value=value})+Environment.NewLine);}
  internal static void Finish(string status,string detail){Finished=true;File.WriteAllText(Path.Combine(DirectoryPath,"trace-stop"),"stop");SettingsStore.AtomicWrite(Path.Combine(DirectoryPath,"completed.json"),SettingsStore.Json.Serialize(new{Status=status,Detail=detail,Utc=DateTime.UtcNow.ToString("o")}));}
  internal static bool Expired{get{return DateTime.UtcNow>=deadline;}}
 }
 internal sealed partial class MainView {
  void TickOutputComparison(){
   if(OutputComparisonCommand.Config!=null&&!OutputComparisonCommand.Finished&&File.Exists(Path.Combine(OutputComparisonCommand.DirectoryPath,"comparison-cancel"))){
    routes.CancelComparison();if(OutputComparisonCommand.Pending){OutputComparisonCommand.Pending=false;OutputComparisonCommand.Finish("cancelled","在切换输出前取消。");}return;
   }
   if(!OutputComparisonCommand.Pending)return;
   if(OutputComparisonCommand.Expired){OutputComparisonCommand.Pending=false;OutputComparisonCommand.Finish("not-started","两台手机通道未在准备期内就绪；未切换输出。");Notify("自动对照未开始：手机通道未就绪。");return;}
   if(routes.Busy||routes.ResumePending)return;
   if(settings.Bool("PhoneAntiStutter")){OutputComparisonCommand.Pending=false;OutputComparisonCommand.Finish("not-started","缓冲未关闭；未修改设置。");Notify("自动对照要求缓冲关闭。");return;}
   var phonesReady=routes.Engine.Snapshot().Count(s=>s.Target=="graph"&&s.Source!=null&&s.Source.IndexOf("BTHENUM",StringComparison.OrdinalIgnoreCase)>=0&&s.Status=="running");
   if(phonesReady!=2)return;
   OutputComparisonCommand.Pending=false;Run(()=>routes.CompareOutputs(OutputComparisonCommand.Config));
  }
 }
 internal sealed partial class RouteCoordinator {
  CancellationTokenSource comparisonCancel;
  internal void CancelComparison(){if(comparisonCancel!=null)comparisonCancel.Cancel();}
  internal async Task CompareOutputs(OutputComparisonConfig config){
   RequireIdle();Busy=true;LastError="";comparisonCancel=new CancellationTokenSource();Status="正在自动对照：临时双手机连线，结束后恢复。";
   try{
    if(!AudioTimingTrace.Enabled)throw new InvalidOperationException("时间诊断未启动，未切换输出。");
    // Include preflight in the tracked operation so exit cannot dispose the engine underneath it.
    active=Task.Run(async()=>{
     var session=Engine.BeginOutputComparison(config);OutputComparisonCommand.Report("preflight",session.Evidence(config));
     await OutputComparisonRunner.Run(config,session.Change,session.Snapshot,session.Restore,(kind,value)=>{if(kind=="switch")Status="自动对照 "+value+" · 缓冲关闭";OutputComparisonCommand.Report(kind,value);},comparisonCancel.Token);
    });await active;Status="自动对照结束，已恢复原连线。";OutputComparisonCommand.Finish("completed",Status);
   }catch(Exception ex){Status=LastError=ex.GetBaseException().Message;OutputComparisonCommand.Report("failed",ex.ToString());OutputComparisonCommand.Finish("failed",ex.ToString());}
   finally{comparisonCancel.Dispose();comparisonCancel=null;active=null;Busy=false;}
   if(stopped&&!disposed)await Stop();
  }
 }
}

#endif
