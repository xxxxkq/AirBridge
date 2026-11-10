using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Diagnostics;
using System.Threading;
namespace AirBridge.Desktop {
 internal static class UpdateInstaller {
  internal static bool Packaged {get{string path=Environment.GetEnvironmentVariable("AIRBRIDGE_LAUNCH_PATH");return !String.IsNullOrEmpty(path)&&Path.IsPathRooted(path)&&File.Exists(path)&&!String.Equals(Path.GetFullPath(path),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"AirBridge.exe"),StringComparison.OrdinalIgnoreCase);}}
  internal static string Stamp(Process p){try{return p==null||p.HasExited?"":p.Id+":"+p.StartTime.ToUniversalTime().Ticks;}catch{return "";}}
  static string Folder(string root,string id){Guid value;if(!Guid.TryParseExact(id,"N",out value))throw new InvalidDataException("Invalid update transaction");return Path.Combine(root,"updates","jobs",id);}
  internal static string Prepare(UpdateRelease release,string candidate,string root){
   if(!Packaged)throw new InvalidOperationException("Automatic installation requires the single-file Release.");
   if(AirBridge.Storage.DataLocation.HasPending(ApplicationLaunchPath.Current))throw new InvalidOperationException("数据位置待切换，请先退出并重开软件，再安装更新。 / Restart to apply the data location before updating.");
   UpdateService.Verify(candidate,release.Digest,release.Size,release.Version);
   string id=Guid.NewGuid().ToString("N"),directory=Folder(root,id);Directory.CreateDirectory(directory);
   string target=ApplicationLaunchPath.Current;if(!Path.IsPathRooted(target)||!File.Exists(target))throw new InvalidDataException("Update target unavailable");
   var doc=new XmlDocument();var job=doc.CreateElement("update");doc.AppendChild(job);
   job.SetAttribute("target",target);job.SetAttribute("candidate",candidate);job.SetAttribute("sha256",release.Digest);job.SetAttribute("size",release.Size.ToString());job.SetAttribute("version",release.Version.ToString());
   using(var parent=Process.GetCurrentProcess()){string stamp=Stamp(parent);if(stamp=="")throw new IOException("Cannot identify the current AirBridge process");job.SetAttribute("parent",stamp);}job.SetAttribute("watcher",OutputRecovery.UpdateWatcherStamp());
   job.SetAttribute("workers",String.Join(",",AirBridge.Routing.PhoneWorkerClient.RunningProcessIds.Select(pid=>{try{using(var p=Process.GetProcessById(pid))return Stamp(p);}catch{return "";}}).Where(s=>s!="")));
   string ready=@"Local\AirBridge.UpdateReady."+id;job.SetAttribute("ready",ready);doc.Save(Path.Combine(directory,"install.xml"));
   using(var signal=new EventWaitHandle(false,EventResetMode.ManualReset,ready)){
    var start=new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"AirBridge.exe"),"--apply-update "+id){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
    start.EnvironmentVariables["AIRBRIDGE_LAUNCH_PATH"]=target;
    using(var installer=Process.Start(start)){if(installer==null||!signal.WaitOne(5000))throw new IOException("Update helper did not become ready; AirBridge remains open.");}
   }return directory;
  }
  static void Wait(string stamp,Stopwatch elapsed){if(String.IsNullOrEmpty(stamp))return;var pieces=stamp.Split(':');int pid;long ticks;if(pieces.Length!=2||!Int32.TryParse(pieces[0],out pid)||!Int64.TryParse(pieces[1],out ticks))throw new InvalidDataException("Invalid process stamp");
   Process process;try{process=Process.GetProcessById(pid);}catch(ArgumentException){return;}
   using(process){try{if(process.HasExited||process.StartTime.ToUniversalTime().Ticks!=ticks)return;int remaining=Math.Max(0,60000-(int)elapsed.ElapsedMilliseconds);if(!process.WaitForExit(remaining))throw new TimeoutException("Audio processes did not finish recovery; update was not installed.");}catch(InvalidOperationException){if(!process.HasExited)throw;}catch(System.ComponentModel.Win32Exception){if(!process.HasExited)throw;}}
  }
  internal static void Commit(string candidate,string target,string backup,string expectedDigest=null){
   if(!Path.IsPathRooted(target)||!File.Exists(target)||(File.GetAttributes(target)&FileAttributes.ReparsePoint)!=0)throw new IOException("Unsafe or unavailable update target");
   string stage=target+".update-"+Guid.NewGuid().ToString("N");
   try{File.Copy(candidate,stage,false);if(expectedDigest!=null&&!String.Equals(UpdateService.HashFile(stage),expectedDigest,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Staged update checksum mismatch");File.Replace(stage,target,backup,true);}finally{if(File.Exists(stage))File.Delete(stage);}
  }
  internal static void Rollback(string target,string backup){if(File.Exists(backup))File.Replace(backup,target,null,true);}
  internal static int Apply(string id){Guid parsed;if(!Guid.TryParseExact(id,"N",out parsed))return 2;using(var mutex=new Mutex(false,@"Local\AirBridge.Update."+id)){bool held=false;try{try{held=mutex.WaitOne(0);}catch(AbandonedMutexException){held=true;}return held?ApplyCore(id):3;}finally{if(held)mutex.ReleaseMutex();}}}
  static int ApplyCore(string id){string root=AirBridge.Storage.DataLocation.Current,directory="";string target="",backup="";bool committed=false,closed=false;Process child=null;
   try{directory=Folder(root,id);var doc=new XmlDocument{XmlResolver=null};doc.Load(Path.Combine(directory,"install.xml"));var job=doc.DocumentElement;if(job==null||job.Name!="update")throw new InvalidDataException("Invalid install job");
    target=job.GetAttribute("target");if(!String.Equals(target,ApplicationLaunchPath.Current,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update target changed");
    string candidate=Path.GetFullPath(job.GetAttribute("candidate")),downloadRoot=Path.GetFullPath(Path.Combine(root,"updates","downloads"))+Path.DirectorySeparatorChar;
    if(!candidate.StartsWith(downloadRoot,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Candidate outside update downloads");
    var version=UpdateService.ParseVersion(job.GetAttribute("version"));if(version==null)throw new InvalidDataException("Invalid update version");
    UpdateService.Verify(candidate,job.GetAttribute("sha256"),Int64.Parse(job.GetAttribute("size")),version);
    using(var ready=EventWaitHandle.OpenExisting(job.GetAttribute("ready")))ready.Set();
    var elapsed=Stopwatch.StartNew();Wait(job.GetAttribute("parent"),elapsed);Wait(job.GetAttribute("watcher"),elapsed);foreach(var worker in job.GetAttribute("workers").Split(new[]{','},StringSplitOptions.RemoveEmptyEntries))Wait(worker,elapsed);
    closed=true;
    // A pending journal means output restoration did not finish. Do not hide that failure with an update.
    foreach(var name in new[]{"AirBridge-default-output-recovery.xml","AirBridge-app-routing-recovery.xml"}){string path=Path.Combine(root,name);if(File.Exists(path)){var journal=new XmlDocument{XmlResolver=null};journal.Load(path);if(journal.DocumentElement!=null&&journal.DocumentElement.ChildNodes.OfType<XmlElement>().Any())throw new IOException("Output restoration is still pending; update cancelled.");}}
    backup=target+".previous";Commit(candidate,target,backup,job.GetAttribute("sha256"));committed=true;
    var start=new ProcessStartInfo(target){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(target)};start.EnvironmentVariables["AIRBRIDGE_UPDATE_JOB"]=id;child=Process.Start(start);if(child==null)throw new IOException("Cannot start updated AirBridge");
    for(int i=0;i<200;i++){if(File.Exists(Path.Combine(directory,"started"))){File.Copy(backup,Path.Combine(directory,"previous.exe"),true);File.Delete(backup);File.WriteAllText(Path.Combine(directory,"result.txt"),"installed");return 0;}if(child.HasExited)throw new IOException("Updated AirBridge exited before startup confirmation");Thread.Sleep(100);}
    File.WriteAllText(Path.Combine(directory,"result.txt"),"Installed, but startup is not confirmed. Backup retained: "+backup);return 2;
   }catch(Exception ex){if(closed&&(!committed||child==null||child.HasExited))try{if(committed)Rollback(target,backup);Process.Start(new ProcessStartInfo(target){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(target)});}catch(Exception rollback){ex=new IOException(ex.Message+"; rollback: "+rollback.Message,ex);}
    try{Directory.CreateDirectory(root);File.AppendAllText(Path.Combine(root,"update-error.log"),DateTime.UtcNow.ToString("o")+" "+ex+Environment.NewLine);if(directory!="")File.WriteAllText(Path.Combine(directory,"result.txt"),ex.Message);}catch{}return 1;
   }finally{if(child!=null)child.Dispose();}
  }
  internal static void ConfirmStartup(){string id=Environment.GetEnvironmentVariable("AIRBRIDGE_UPDATE_JOB");if(String.IsNullOrEmpty(id))return;Environment.SetEnvironmentVariable("AIRBRIDGE_UPDATE_JOB",null);try{string folder=Folder(AirBridge.Storage.DataLocation.Current,id);if(File.Exists(Path.Combine(folder,"install.xml")))File.WriteAllText(Path.Combine(folder,"started"),DateTime.UtcNow.ToString("o"));}catch{}}
 }
}
