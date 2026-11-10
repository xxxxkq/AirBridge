using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Threading;
using System.Security.Cryptography;
namespace AirBridge.Storage {
 internal sealed class DataResolution {internal string Root,Notice="";}
 internal static class DataLocation {
  internal const string EnvironmentKey="AIRBRIDGE_DATA_ROOT";
  internal static string LegacyRoot {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AirBridge");}}
  internal static string Launcher {get{string path=Environment.GetEnvironmentVariable("AIRBRIDGE_LAUNCH_PATH");return !String.IsNullOrEmpty(path)&&Path.IsPathRooted(path)&&File.Exists(path)?path:System.Reflection.Assembly.GetEntryAssembly().Location;}}
  internal static string Pointer(string launch){return Path.ChangeExtension(Path.GetFullPath(launch),".storage.xml");}
  internal static string Current {get{string inherited=Environment.GetEnvironmentVariable(EnvironmentKey);return !String.IsNullOrEmpty(inherited)&&Path.IsPathRooted(inherited)?Path.GetFullPath(inherited):Resolve(Launcher,LegacyRoot,false,null).Root;}}
  static string Full(string path){if(String.IsNullOrWhiteSpace(path)||!Path.IsPathRooted(path)||path.StartsWith(@"\\",StringComparison.Ordinal))throw new IOException("请选择本地磁盘中的专用数据目录。 / Select a dedicated local data folder.");string full=Path.GetFullPath(path);if(full.TrimEnd('\\','/')==Path.GetPathRoot(full).TrimEnd('\\','/'))throw new IOException("不能使用磁盘根目录作为数据目录。 / The drive root cannot be a data folder.");return full.TrimEnd('\\','/');}
  static bool Equal(string a,string b){return String.Equals(Full(a),Full(b),StringComparison.OrdinalIgnoreCase);}
  static string Active(XmlElement e,string launch,string legacy,string prefix){string mode=e.GetAttribute(prefix+"mode"),path=e.GetAttribute(prefix+"path");return mode=="portable"?Full(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(launch)),"Data")):mode=="default"?Full(legacy):Full(path);}
  static XmlDocument Load(string path){var doc=new XmlDocument{XmlResolver=null};doc.Load(path);if(doc.DocumentElement==null||doc.DocumentElement.Name!="storage"||doc.DocumentElement.GetAttribute("schema")!="1")throw new InvalidDataException("Invalid data location configuration");return doc;}
  static XmlDocument Read(string path){if(!File.Exists(path))return null;try{return Load(path);}catch{if(File.Exists(path+".bak"))return Load(path+".bak");throw;}}
  static void Save(XmlDocument doc,string path){string temp=path+".tmp-"+Guid.NewGuid().ToString("N");try{using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){doc.Save(stream);stream.Flush(true);}if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
  internal static bool HasPending(string launch){var doc=Read(Pointer(launch));return doc!=null&&doc.DocumentElement.HasAttribute("pending-mode");}
  internal static string PendingTarget(string launch,string legacy){var doc=Read(Pointer(launch));return doc==null||!doc.DocumentElement.HasAttribute("pending-mode")?null:Active(doc.DocumentElement,launch,legacy,"pending-");}
  internal static bool PendingCopies(string launch){var doc=Read(Pointer(launch));return doc==null||!doc.DocumentElement.HasAttribute("pending-mode")||doc.DocumentElement.GetAttribute("copy")=="true";}
  internal static void Request(string launch,string current,string legacy,string mode,string destination,bool copy){
   if(mode!="portable"&&mode!="custom"&&mode!="default")throw new ArgumentException("Invalid data location mode");current=Full(current);
   string target=mode=="portable"?Full(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(launch)),"Data")):mode=="default"?Full(legacy):Full(destination);
   if(!Equal(current,target)&&(target.StartsWith(current+"\\",StringComparison.OrdinalIgnoreCase)||current.StartsWith(target+"\\",StringComparison.OrdinalIgnoreCase)))throw new IOException("新旧数据目录不能互相包含。 / Data folders cannot contain one another.");
   if(!Equal(current,target)&&copy&&Directory.Exists(target)&&Directory.EnumerateFileSystemEntries(target).Any())throw new IOException("迁移目标必须为空；使用已有配置时请取消勾选迁移。 / Migration requires an empty destination; disable migration to use existing data.");
   Probe(target);
   var doc=new XmlDocument();var e=doc.CreateElement("storage");doc.AppendChild(e);e.SetAttribute("schema","1");
   string portable=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(launch)),"Data"),activeMode=Equal(current,portable)?"portable":Equal(current,legacy)?"default":"custom";e.SetAttribute("mode",activeMode);e.SetAttribute("path",activeMode=="custom"?current:"");
   if(!Equal(current,target)){e.SetAttribute("pending-mode",mode);e.SetAttribute("pending-path",mode=="custom"?target:"");e.SetAttribute("copy",copy?"true":"false");}
   Save(doc,Pointer(launch));
  }
  static void Probe(string root){Directory.CreateDirectory(root);if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("数据目录不能是链接。 / Linked data folders are not supported.");string probe=Path.Combine(root,".airbridge-write-"+Guid.NewGuid().ToString("N"));try{using(var file=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.WriteByte(1);file.Flush(true);}}finally{if(File.Exists(probe))File.Delete(probe);}}
  internal static void Cancel(string launch){string file=Pointer(launch);var doc=Read(file);if(doc==null)return;foreach(string attr in new[]{"pending-mode","pending-path","copy"})doc.DocumentElement.RemoveAttribute(attr);Save(doc,file);}
  internal static bool RecoveryIsQuiet(){using(var gate=new Mutex(false,@"Local\AirBridge.DefaultOutputRecovery.v1")){bool held=false;try{try{held=gate.WaitOne(10000);}catch(AbandonedMutexException){held=true;}return held;}finally{if(held)gate.ReleaseMutex();}}}
  internal static bool JournalsEmpty(string root){foreach(var name in new[]{"AirBridge-default-output-recovery.xml","AirBridge-app-routing-recovery.xml"}){string file=Path.Combine(root,name);if(!File.Exists(file))continue;var doc=new XmlDocument{XmlResolver=null};doc.Load(file);if(doc.DocumentElement!=null&&doc.DocumentElement.ChildNodes.OfType<XmlElement>().Any())return false;}return true;}
  internal static DataResolution Resolve(string launch,string legacy,bool migrate,Func<bool> recoveryReady){
   string pointer=Pointer(launch);var result=new DataResolution{Root=Full(legacy)};
   // A storage lock also serializes two simultaneous cold starts of the same portable copy.
   string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(pointer.ToUpperInvariant()))).Replace("-","");
   using(var gate=new Mutex(false,@"Local\AirBridge.Storage."+hash)){bool held=false;try{try{held=gate.WaitOne(30000);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("Data location is busy");
    var doc=Read(pointer);if(doc==null)return result;var e=doc.DocumentElement;result.Root=Active(e,launch,legacy,"");
    if(!migrate||!e.HasAttribute("pending-mode"))return result;
    try{
     if(recoveryReady!=null&&!recoveryReady()||!JournalsEmpty(result.Root)){result.Notice="输出恢复尚未完成，暂用原数据目录；下次启动重试迁移。 / Output recovery pending; using the previous data folder.";return result;}
     string target=Active(e,launch,legacy,"pending-");bool copy=e.GetAttribute("copy")=="true";
     if(!Equal(target,result.Root)&&(target.StartsWith(result.Root+"\\",StringComparison.OrdinalIgnoreCase)||result.Root.StartsWith(target+"\\",StringComparison.OrdinalIgnoreCase)))throw new IOException("Data folders overlap");
     if(copy&&!Equal(target,result.Root))CopyData(result.Root,target);else Probe(target);
     e.SetAttribute("mode",e.GetAttribute("pending-mode"));e.SetAttribute("path",e.GetAttribute("mode")=="custom"?target:"");foreach(string name in new[]{"pending-mode","pending-path","copy"})e.RemoveAttribute(name);Save(doc,pointer);
     result.Root=target;result.Notice=copy?"数据位置已切换，原目录保留作备份。 / Data location changed; the old folder is retained.":"已使用所选目录的数据。 / Using the selected data folder.";
    }catch(Exception ex){result.Notice="数据位置未更改，继续使用原目录："+ex.Message;}
    return result;
   }finally{if(held)gate.ReleaseMutex();}}
  }
  static void CopyData(string source,string target){
   if(!Directory.Exists(source))throw new IOException("原数据目录不可访问，迁移未执行。 / The previous data folder is unavailable.");
   target=Full(target);if(Directory.Exists(target)&&Directory.EnumerateFileSystemEntries(target).Any())throw new IOException("Migration destination is no longer empty");
   Probe(target);string parent=Path.GetDirectoryName(target),stage=Path.Combine(parent,".AirBridge-migrate-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
   try{foreach(var file in Directory.GetFiles(source).Where(f=>new[]{".json",".json.bak",".xml",".xml.bak",".log",".log.1",".txt",".csv"}.Any(ext=>f.EndsWith(ext,StringComparison.OrdinalIgnoreCase)))){if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked source file");string name=Path.GetFileName(file);File.Copy(file,Path.Combine(stage,name),false);using(var a=File.OpenRead(file))using(var b=File.OpenRead(Path.Combine(stage,name)))using(var sha=SHA256.Create()){string x=BitConverter.ToString(sha.ComputeHash(a)),y=BitConverter.ToString(sha.ComputeHash(b));if(x!=y)throw new IOException("Migration checksum mismatch");}}
    if(Directory.EnumerateFileSystemEntries(target).Any())throw new IOException("Migration destination changed");Directory.Delete(target,false);Directory.Move(stage,target);
   }finally{if(Directory.Exists(stage)&&Path.GetDirectoryName(Path.GetFullPath(stage))==parent)Directory.Delete(stage,true);}
  }
  internal static bool SignalExisting(){try{using(var gate=Mutex.OpenExisting(@"Local\AirBridge.Desktop.v1")){try{using(var signal=EventWaitHandle.OpenExisting(@"Local\AirBridge.Show.v1"))signal.Set();}catch(WaitHandleCannotBeOpenedException){}return true;}}catch(WaitHandleCannotBeOpenedException){return false;}}
 }
}
