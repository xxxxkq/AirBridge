using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
namespace AirBridge.Routing {
 // Write-ahead journal. Only media/console render defaults are owned, never communications.
 public sealed class DefaultOutputRoute {
  sealed class Entry { public int Role; public string Original,Written,Previous;public bool Pending; public Entry Copy(){return (Entry)MemberwiseClone();} }
  readonly string path;readonly Func<int,string> read;readonly Action<string,int> write;readonly Func<string,bool> available;readonly List<Entry> records=new List<Entry>();
  public DefaultOutputRoute(string path):this(path,AirBridge.Platform.Audio.DefaultId,AirBridge.Platform.OutputSwitch.SelectRole,id=>AirBridge.Platform.Audio.List().Any(e=>e.Id==id&&e.Active)){}
  public DefaultOutputRoute(string path,Func<int,string> read,Action<string,int> write,Func<string,bool> available){if(String.IsNullOrWhiteSpace(path))throw new ArgumentException("默认输出管理需要恢复日志路径。");this.path=Path.GetFullPath(path);this.read=read;this.write=write;this.available=available;if(File.Exists(this.path)){var doc=new XmlDocument{XmlResolver=null};doc.Load(this.path);foreach(XmlElement e in doc.SelectNodes("/defaults/role")){int role=Int32.Parse(e.GetAttribute("id"));if(role<0||role>1||records.Any(r=>r.Role==role))throw new InvalidDataException("默认输出恢复记录无效。");records.Add(new Entry{Role=role,Original=e.GetAttribute("original"),Written=e.GetAttribute("written"),Previous=e.GetAttribute("previous"),Pending=e.GetAttribute("pending")=="true"});}}}
  public int PendingCount{get{return records.Count;}}
  public string OriginalId(int role){var entry=records.FirstOrDefault(e=>e.Role==role);return entry==null?read(role):entry.Original;}
  static bool Equal(string a,string b){return String.Equals(a,b,StringComparison.OrdinalIgnoreCase);}
  static bool Owned(Entry e,string current){return Equal(current,e.Written)||(e.Pending&&Equal(current,e.Previous));}
  void Save(){var doc=new XmlDocument();var root=doc.CreateElement("defaults");doc.AppendChild(root);foreach(var r in records){var e=doc.CreateElement("role");e.SetAttribute("id",r.Role.ToString());e.SetAttribute("original",r.Original);e.SetAttribute("written",r.Written);e.SetAttribute("previous",r.Previous??"");e.SetAttribute("pending",r.Pending?"true":"false");root.AppendChild(e);}Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+".tmp";using(var f=new FileStream(tmp,FileMode.Create,FileAccess.Write,FileShare.None)){doc.Save(f);f.Flush(true);}if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
  public void Apply(string target){if(String.IsNullOrEmpty(target)||target=="default"){Restore();return;}if(!available(target))throw new InvalidOperationException("所选电脑播放设备不可用，默认输出未切换。");
   var before=new[]{read(0),read(1)};var baseline=records.Select(e=>e.Copy()).ToArray();foreach(var r in records)if(!Owned(r,before[r.Role]))throw new InvalidOperationException("系统默认输出已被其他操作更改；请先停止并还原，再重新应用。");
   foreach(int role in new[]{0,1}){var r=records.FirstOrDefault(e=>e.Role==role);if(r==null){r=new Entry{Role=role,Original=before[role]};records.Add(r);}r.Previous=before[role];r.Written=target;r.Pending=true;}Save();
   try{foreach(int role in new[]{0,1}){if(!Equal(read(role),target))write(target,role);if(!Equal(read(role),target))throw new InvalidOperationException("Windows 未确认默认播放设备切换。");}foreach(var r in records){r.Pending=false;r.Previous="";}Save();}
   catch(Exception failure){bool restored=true;foreach(int role in new[]{1,0})try{var current=read(role);if(Equal(current,target)&&!Equal(current,before[role]))write(before[role],role);if(!Equal(read(role),before[role]))restored=false;}catch{restored=false;}if(restored){records.Clear();records.AddRange(baseline);Save();}throw new InvalidOperationException(restored?"默认输出切换失败，已还原此前设备。":"默认输出切换失败，部分恢复待完成；恢复记录已保留。",failure);}
  }
  public void Restore(){Exception error=null;foreach(var r in records.ToArray()){try{var current=read(r.Role);if(Owned(r,current)&&!Equal(current,r.Original)){if(!available(r.Original))throw new InvalidOperationException("原默认设备暂不可用，恢复记录已保留；重新连接设备后点击恢复。");write(r.Original,r.Role);if(!Equal(read(r.Role),r.Original))throw new InvalidOperationException("Windows 未确认默认设备恢复。");}records.Remove(r);Save();}catch(Exception ex){error=ex;}}if(error!=null)throw error;}
 }
}
