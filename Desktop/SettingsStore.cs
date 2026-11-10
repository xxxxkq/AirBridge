using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Web.Script.Serialization;
namespace AirBridge.Desktop {
 internal sealed class SettingsStore {
  internal static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=8388608,RecursionLimit=64};
  internal readonly string Root,Path;internal readonly bool Test;
  Dictionary<string,object> values;
  public SettingsStore(string root,bool test){Root=root;Test=test;Path=System.IO.Path.Combine(root,"settings.json");Directory.CreateDirectory(root);values=Defaults();
   foreach(string file in new[]{Path,Path+".bak"}){try{if(!File.Exists(file))continue;var saved=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file));if(saved==null||!saved.ContainsKey("AutoConnect"))continue;foreach(var pair in saved)values[pair.Key]=pair.Value;break;}catch{}}
  }
  public static Dictionary<string,object> Defaults(){return new Dictionary<string,object>{
   {"ResumeGraph",false},{"LastRunningGraph",null},{"NodeLibraryCollapsed",false},{"EditorWorkflow",2},{"AudioAppNodes",new object[0]},{"AudioModules",new object[0]},
   {"AudioCanvasNodes",new[]{"pc:system","o:default"}},{"AudioLinks",new[]{new UI.AudioLink{Source="pc:system",Target="o:default",SourceName="电脑媒体声音",TargetName="系统默认播放器"}}},{"AudioNodePositions",new object[0]},
   {"PlaybackTuning",new Routing.PlaybackTuning()},{"VirtualCableEnabled",false},{"PhoneAntiStutter",true},{"TakeOverPcAudio",true},{"IgnoredDevices",new object[0]},{"SidebarCollapsed",false},{"Language","zh-CN"},{"AutoConnect",true},{"PopupEnabled",true},{"LastSourceId",""},{"LastSourceName",""},{"RememberedSources",new string[0]},
   {"WearPlayback",true},{"PodAddress",""},{"Theme","system"},{"HideTray",false},{"TrayBattery",true},{"TrayOutput",true},{"CasePopup",true},{"OutputPopup",false},{"PopupSeconds",5},{"PopupPosition","right"},{"LowBattery",false},{"LogLevel","Info"},
   {"HotkeyShow",""},{"HotkeyMute",""},{"HotkeyPlay",""},{"HotkeyOutput",""},{"HotkeyBluetooth",""},{"HotkeyDisconnect",""}};}
  public T Get<T>(string key,T fallback=default(T)){object value;if(!values.TryGetValue(key,out value)||value==null)return fallback;try{return Json.Deserialize<T>(Json.Serialize(value));}catch{return fallback;}}
  public Routing.PlaybackTuning AudioTuning(){var value=Get<Routing.PlaybackTuning>("PlaybackTuning",new Routing.PlaybackTuning());try{value.Validate();return value;}catch{return new Routing.PlaybackTuning();}}
  public bool IsIgnored(string id){string address=DeviceAddress(id);return Items<IgnoredDevice>("IgnoredDevices").Any(d=>String.Equals(d.Id,id,StringComparison.OrdinalIgnoreCase)||address!=""&&String.Equals(d.Address,address,StringComparison.OrdinalIgnoreCase));}
  public static string DeviceAddress(string id){var m=System.Text.RegularExpressions.Regex.Match(id??"","DEV_([0-9A-F]{12})",System.Text.RegularExpressions.RegexOptions.IgnoreCase);return m.Success?m.Groups[1].Value.ToUpperInvariant():PhoneService.Address(id);}
  static string WearKey(Core.DeviceIdentity device){string address=DeviceAddress(device.Id);return address!=""?"address:"+address:!String.IsNullOrWhiteSpace(device.ContainerId)?"container:"+device.ContainerId.ToUpperInvariant():"id:"+device.Id;}
  public bool WearEnabled(Core.DeviceIdentity device){if(!Core.AppleProvider.SupportsBroadcastWear(device))return false;bool value;return Get<Dictionary<string,bool>>("DeviceWearPlayback",new Dictionary<string,bool>()).TryGetValue(WearKey(device),out value)?value:Bool("WearPlayback");}
  public void SetWearEnabled(Core.DeviceIdentity device,bool enabled){var map=Get<Dictionary<string,bool>>("DeviceWearPlayback",new Dictionary<string,bool>());map[WearKey(device)]=enabled;Set("DeviceWearPlayback",map);Save();}
  public bool Bool(string key){return Get<bool>(key);}public string Text(string key){return Get<string>(key,"");}
  public void Set(string key,object value){values[key]=value;}
  public T[] Items<T>(string key){return Get<T[]>(key,new T[0]);}
  public void Save(){if(Test)return;AtomicWrite(Path,Json.Serialize(values));}
  public static void AtomicWrite(string path,string text){Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));string temp=path+".tmp";byte[] bytes=Encoding.UTF8.GetBytes(text);using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}if(File.Exists(path)){string backup=null;try{Json.DeserializeObject(File.ReadAllText(path));backup=path+".bak";}catch{}File.Replace(temp,path,backup);}else File.Move(temp,path);}
  public static T Clone<T>(T value){return Json.Deserialize<T>(Json.Serialize(value));}
 }
 internal sealed class AppBinding{public string Id,Source,Name,Path;}
 internal sealed class RememberedApp{public string Id,Path;}
 internal sealed class RunningGraph{public int Schema=1;public Routing.GraphEdge[] Edges;public Routing.ModuleSpec[] Modules;public RememberedApp[] Applications;}
 internal sealed class IgnoredDevice{public string Id,Name,Address;}
}

