using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using NAudio.CoreAudioApi;
namespace AirBridge.Routing {
 public sealed class AppRuleRecord {public string Source,Exe,Original,Written,Previous;public bool Pending;public int Role;public uint Pid;public long Start;public AppRuleRecord Copy(){return (AppRuleRecord)MemberwiseClone();}}
 public sealed class NativeAppRoutes {
  readonly string journal;readonly List<AppRuleRecord> records=new List<AppRuleRecord>();readonly object gate=new object();
  public NativeAppRoutes(string path){if(String.IsNullOrWhiteSpace(path))throw new ArgumentException("应用输出管理需要可写的恢复日志路径。");journal=Path.GetFullPath(path);Load();}
  public int PendingCount {get{lock(gate)return records.Count;}}
  public string[] ActiveSources {get{lock(gate)return records.Select(r=>r.Source).Distinct().ToArray();}}
  static bool Equal(string a,string b){return String.Equals(a??"",b??"",StringComparison.OrdinalIgnoreCase);}
  void Load(){if(!File.Exists(journal))return;var doc=new XmlDocument{XmlResolver=null};doc.Load(journal);foreach(XmlElement e in doc.SelectNodes("/rules/rule"))records.Add(new AppRuleRecord{Source=e.GetAttribute("source"),Exe=e.GetAttribute("exe"),Original=e.GetAttribute("original"),Written=e.GetAttribute("written"),Previous=e.GetAttribute("previous"),Pending=e.GetAttribute("pending")=="true",Role=Int32.Parse(e.GetAttribute("role")),Pid=UInt32.Parse(e.GetAttribute("pid")),Start=Int64.Parse(e.GetAttribute("start"))});}
  void Save(){var doc=new XmlDocument();var root=doc.CreateElement("rules");doc.AppendChild(root);foreach(var r in records){var e=doc.CreateElement("rule");e.SetAttribute("source",r.Source);e.SetAttribute("exe",r.Exe);e.SetAttribute("original",r.Original??"");e.SetAttribute("written",r.Written??"");e.SetAttribute("previous",r.Previous??"");e.SetAttribute("pending",r.Pending?"true":"false");e.SetAttribute("role",r.Role.ToString());e.SetAttribute("pid",r.Pid.ToString());e.SetAttribute("start",r.Start.ToString());root.AppendChild(e);}Directory.CreateDirectory(Path.GetDirectoryName(journal));string temp=journal+".tmp";using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){doc.Save(stream);stream.Flush(true);}if(File.Exists(journal))File.Replace(temp,journal,null);else File.Move(temp,journal);}
  static AppRuleRecord Identity(string source){var parts=source.Split(':');if(parts.Length!=3||parts[0]!="a")throw new ArgumentException("无效应用 ID");using(var p=Process.GetProcessById(Int32.Parse(parts[1]))){long start=p.StartTime.ToUniversalTime().Ticks;if(start!=Int64.Parse(parts[2]))throw new InvalidOperationException("应用已重启，请重新选择应用。");return new AppRuleRecord{Source=source,Pid=(uint)p.Id,Start=start,Exe=p.MainModule.FileName};}}
  static uint Resolve(AppRuleRecord record){try{using(var p=Process.GetProcessById((int)record.Pid)){if(p.StartTime.ToUniversalTime().Ticks==record.Start&&Equal(p.MainModule.FileName,record.Exe))return record.Pid;}}catch{}
   var match=ApplicationIdentity.Resolve(record.Source,record.Exe,GraphRuntime.Applications());if(match!=null)try{return Identity(match.Id).Pid;}catch{}return 0;
  }
  sealed class Operation {public AppRuleRecord Record;public uint Pid;public string Before,After;public bool Remove;}
  public static void RequireExclusiveCable(string source,string endpoint){
   uint expected=UInt32.Parse(source.Split(':')[1]);
   using(var enumerator=new MMDeviceEnumerator())using(var device=enumerator.GetDevice(endpoint)){
    var sessions=device.AudioSessionManager.Sessions;
    for(int i=0;i<sessions.Count;i++)using(var session=sessions[i]){
     if(session.State==NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive&&session.GetProcessID!=expected)
      throw new InvalidOperationException("VB-CABLE 正被其他音频会话使用。请先将其他应用移出该通道，避免混入声音。");
    }
   }
  }
  public void Apply(GraphEdge[] edges){lock(gate){if(edges.Length==0&&records.Count==0)return;
   var wanted=new List<AppRuleRecord>();var executableTargets=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   using(var enumerator=new MMDeviceEnumerator())foreach(var edge in edges){var identity=Identity(edge.Source);string endpoint=edge.Target.Substring(2);if(endpoint!="default")using(var device=enumerator.GetDevice(endpoint)){if(device.State!=DeviceState.Active||device.DataFlow!=DataFlow.Render)throw new InvalidOperationException("应用目标播放设备不可用。");}
    string target=AppOutputPolicy.RenderId(endpoint),prior;if(executableTargets.TryGetValue(identity.Exe,out prior)&&!Equal(prior,target))throw new InvalidOperationException("同一软件的多个进程不能指定不同输出设备。");if(executableTargets.ContainsKey(identity.Exe))continue;executableTargets.Add(identity.Exe,target);
    foreach(int role in new[]{0,1}){var item=identity.Copy();item.Role=role;item.Written=target;wanted.Add(item);}
   }
   using(var policy=new AppOutputPolicy()){
    var baseline=records.Select(r=>r.Copy()).ToList();var operations=new List<Operation>();
    foreach(var desired in wanted){var old=records.FirstOrDefault(r=>Equal(r.Exe,desired.Exe)&&r.Role==desired.Role);string current=policy.Read(desired.Pid,desired.Role);
     if(old!=null&&!Equal(current,old.Written)&&!(old.Pending&&Equal(current,old.Previous)))throw new InvalidOperationException("应用输出已被其他操作更改。请先停止管理，再重新应用，避免覆盖新的设置。");
     desired.Original=old==null?current:old.Original;operations.Add(new Operation{Record=desired,Pid=desired.Pid,Before=current,After=desired.Written});
    }
    foreach(var old in records.Where(r=>!wanted.Any(w=>Equal(w.Exe,r.Exe)&&w.Role==r.Role))){uint pid=Resolve(old);if(pid==0)continue;string current;try{current=policy.Read(pid,old.Role);}catch{continue;}operations.Add(new Operation{Record=old,Pid=pid,Before=current,After=(Equal(current,old.Written)||(old.Pending&&Equal(current,old.Previous)))?old.Original:current,Remove=true});}
    var completed=new List<Operation>();try{foreach(var op in operations){
      if(!op.Remove){op.Record.Pending=true;op.Record.Previous=op.Before;records.RemoveAll(r=>Equal(r.Exe,op.Record.Exe)&&r.Role==op.Record.Role);records.Add(op.Record);Save();}
      completed.Add(op);if(!Equal(op.Before,op.After)){policy.Write(op.Pid,op.Record.Role,op.After);if(!Equal(policy.Read(op.Pid,op.Record.Role),op.After))throw new InvalidOperationException("Windows 未保留指定应用输出，已尝试回滚。");}
      if(op.Remove){records.Remove(op.Record);Save();}else{op.Record.Pending=false;op.Record.Previous="";Save();}
     }
    }catch(Exception original){bool rollbackOk=true;foreach(var op in completed.AsEnumerable().Reverse()){try{if(Equal(policy.Read(op.Pid,op.Record.Role),op.After))policy.Write(op.Pid,op.Record.Role,op.Before);}catch{rollbackOk=false;}}
     if(rollbackOk){records.Clear();records.AddRange(baseline);Save();}throw new InvalidOperationException(rollbackOk?"应用输出修改失败，已回滚："+original.Message:"应用输出恢复尚未完成，恢复记录已保留，请点击恢复应用输出。",original);
    }
   }
  }}
  public void Restore(){Apply(new GraphEdge[0]);}
 }
}
