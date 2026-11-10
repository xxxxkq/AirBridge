using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;
using AirBridge.Platform;
namespace AirBridge.Desktop {
 internal sealed class PhoneSession {
  public string Id,Name,Status="idle",Detail="";public bool Connected,Paused,Managed;public int Failures,Retries;
  public AirBridge.Routing.PhoneWorkerClient Connection;public Task Operation,Release;public DateTime RetryAt;
  public bool WasConnected,WasPaused,GraphSuspended;
 }
 internal sealed class PhoneService : IDisposable {
  public bool GraphOwnsPlayback;
  readonly SettingsStore settings;public readonly Dictionary<string,PhoneSession> Sessions=new Dictionary<string,PhoneSession>(StringComparer.OrdinalIgnoreCase);
  public DeviceInformation[] Sources=new DeviceInformation[0];public string Error="";public int Revision;bool discovering,disposed;DateTime nextScan;
  readonly BackgroundQuery<Dictionary<string,string>> capabilityQuery=new BackgroundQuery<Dictionary<string,string>>();
  Dictionary<string,string> captureErrors=new Dictionary<string,string>();DateTime nextCapability;
  public bool CanCapture(string id){string error;return captureErrors.TryGetValue(id,out error)&&error=="";}
  public string CaptureError(string id){string error;return captureErrors.TryGetValue(id,out error)?error:"正在检查独立音频接口 / Checking capture interface";}
  internal void AcceptCaptureErrors(Dictionary<string,string> result){bool changed=result.Count!=captureErrors.Count||result.Any(p=>!captureErrors.ContainsKey(p.Key)||captureErrors[p.Key]!=p.Value);captureErrors=result;if(changed)Revision++;}
  async void PollCaptureAvailability(){
   if(disposed||settings.Test||DateTime.UtcNow<nextCapability)return;
   var ids=Sources.Select(s=>s.Id).ToArray();
   var pending=capabilityQuery.TryRun(()=>Task.FromResult(ids.ToDictionary(id=>id,id=>AirBridge.Routing.RouteEngine.PhoneAvailabilityError(id))));
   if(pending==null)return;nextCapability=DateTime.UtcNow.AddSeconds(5);
   try{var result=await pending;if(!disposed&&ids.SequenceEqual(Sources.Select(s=>s.Id)))AcceptCaptureErrors(result);}catch(Exception ex){if(!disposed)Error=ex.GetBaseException().Message;}
  }
  public PhoneService(SettingsStore settings){this.settings=settings;}
  public bool IsManualPaused(string id){return settings.Items<string>("PausedPhoneConnections").Any(x=>String.Equals(x,id,StringComparison.OrdinalIgnoreCase));}
  public void SetManualPause(string id,bool paused){var values=settings.Items<string>("PausedPhoneConnections").Where(x=>!String.Equals(x,id,StringComparison.OrdinalIgnoreCase));settings.Set("PausedPhoneConnections",paused?values.Concat(new[]{id}).ToArray():values.ToArray());settings.Save();if(Sessions.ContainsKey(id))Sessions[id].Paused=paused;}
  public static string Address(string id){var m=System.Text.RegularExpressions.Regex.Match(id??"","&([0-9a-f]{12})_",System.Text.RegularExpressions.RegexOptions.IgnoreCase);return m.Success?m.Groups[1].Value.ToUpperInvariant():"";}
  public PhoneSession Get(string id){PhoneSession session;if(!Sessions.TryGetValue(id,out session)){var source=Sources.FirstOrDefault(s=>s.Id==id);Sessions[id]=session=new PhoneSession{Id=id,Paused=IsManualPaused(id),Name=source==null?id:source.Name};}return session;}
  static Task Convert(object operation,Type result=null){var m=typeof(System.WindowsRuntimeSystemExtensions).GetMethods().First(x=>x.Name=="AsTask"&&x.GetParameters().Length==1&&(result==null?!x.IsGenericMethod&&x.GetParameters()[0].ParameterType.Name=="IAsyncAction":x.IsGenericMethod&&x.GetGenericArguments().Length==1&&x.GetParameters()[0].ParameterType.Name=="IAsyncOperation`1"));return (Task)(result==null?m:m.MakeGenericMethod(result)).Invoke(null,new[]{operation});}
  public Task Discover(){return Discover(false);}
  public async Task Discover(bool inspect){if(discovering||disposed||settings.Test&&!inspect)return;discovering=true;try{var task=(Task<DeviceInformationCollection>)Convert(DeviceInformation.FindAllAsync(AudioPlaybackConnection.GetDeviceSelector()),typeof(DeviceInformationCollection));await Timed(task);if(disposed)return;Sources=task.Result.ToArray();Error="";Revision++;nextCapability=DateTime.MinValue;PollCaptureAvailability();}catch(Exception ex){Error=ex.GetBaseException().Message;}finally{discovering=false;nextScan=DateTime.UtcNow.AddSeconds(30);}}
  public void Remember(string id){settings.Set("RememberedSources",settings.Items<string>("RememberedSources").Where(x=>x!=id&&(Address(id)==""||Address(x)!=Address(id))).Concat(new[]{id}).ToArray());settings.Set("LastSourceId",id);var source=Sources.FirstOrDefault(s=>s.Id==id);if(source!=null)settings.Set("LastSourceName",source.Name);settings.Save();}
  public Task Release(PhoneSession s){var connection=s.Connection;var operation=s.Operation;s.Connection=null;s.Operation=null;s.Connected=false;if(connection!=null||operation!=null)s.Release=Task.Run(async ()=>{await PhoneRelease.Begin(connection,operation).ConfigureAwait(false);});return s.Release??Task.FromResult(true);}
  public async Task Connect(string id,bool manual){var s=Get(id);if(GraphOwnsPlayback){if(manual){s.Detail="音频由节点图管理，请添加该手机的输入连线并应用。 / Add and apply this phone in the audio graph.";Revision++;}return;}if(disposed||s.Managed||s.Connection!=null||s.Operation!=null||IsIgnored(id))return;if(s.Release!=null&&!s.Release.IsCompleted)return;
   AirBridge.Routing.PhoneWorkerClient owned=null;try{if(s.Release!=null){await s.Release;s.Release=null;}if(manual){SetManualPause(id,false);s.Paused=false;s.Failures=s.Retries=0;Remember(id);}if(s.Paused)return;
    s.Status="start";s.Detail="";Revision++;var connection=new AirBridge.Routing.PhoneWorkerClient(id,"receiver",null,null);s.Connection=connection;owned=connection;
    var opening=Task.Run(()=>connection.Start());s.Operation=opening;await opening;
    if(disposed||s.Managed||s.Connection!=connection)return;
    if(!connection.Connected)throw new InvalidOperationException(connection.Error);
    s.Operation=null;s.Connected=true;s.Status="connected";s.Failures=s.Retries=0;Remember(id);Revision++;
   }catch(Exception ex){if(s.Managed||disposed||owned!=null&&s.Connection!=owned)return;var release=Release(s);s.Failures++;s.Status="error";s.Detail=ex.GetBaseException().Message;s.RetryAt=DateTime.UtcNow.AddSeconds(Math.Min(120,10*Math.Pow(2,Math.Min(4,s.Failures-1))));Revision++;}
  }
  static async Task Timed(Task t){if(await Task.WhenAny(t,Task.Delay(30000))!=t)throw new TimeoutException("蓝牙操作超时 / Bluetooth operation timed out");await t;}
  public async Task Disconnect(string id){var s=Get(id);SetManualPause(id,true);s.Paused=true;await Release(s);s.Status="disconnected";s.Detail="";Revision++;}
  public Task[] Take(string[] ids){return ids.Select(id=>{var s=Get(id);if(!s.Managed){if(!s.GraphSuspended){s.WasConnected=s.Connected;s.WasPaused=s.Paused;}s.GraphSuspended=false;s.Managed=true;s.Paused=true;s.Status="start";Revision++;return Release(s);}return s.Release??Task.FromResult(true);}).ToArray();}
  // A running graph owns ALL phone playback, not only the phones present in its edges.
  public Task[] TakeGraph(string[] ids){GraphOwnsPlayback=true;var releases=Take(ids).ToList();foreach(var s in Sessions.Values.Where(s=>!s.Managed).ToArray()){if(!s.GraphSuspended){s.WasConnected=s.Connected;s.WasPaused=s.Paused;s.GraphSuspended=true;}releases.Add(Release(s));s.Status="idle";s.Detail="未连入音频图，不播放 / Not routed in the audio graph";Revision++;}return releases.ToArray();}
  public async Task RestoreGraph(){GraphOwnsPlayback=false;foreach(var s in Sessions.Values.ToArray()){if(s.Managed)await Restore(s.Id,true);else if(s.GraphSuspended){s.GraphSuspended=false;s.Status="disconnected";s.Detail="";Revision++;if(s.WasConnected&&!s.Paused)await Connect(s.Id,false);}}}
  public async Task Restore(string id,bool resume){var s=Get(id);if(!s.Managed)return;s.Managed=false;s.Paused=!resume||s.WasPaused;s.Connected=false;s.Status="disconnected";Revision++;if(resume&&s.WasConnected)await Connect(id,false);}
  public void SetAutoConnect(bool enabled){settings.Set("AutoConnect",enabled);if(enabled)foreach(var s in Sessions.Values.Where(x=>!x.Managed)){s.Paused=IsManualPaused(s.Id);s.RetryAt=DateTime.MinValue;}settings.Save();Revision++;}
  public bool IsIgnored(string id){return settings.IsIgnored(id);}
  public void Tick(HashSet<string> reserved){if(disposed||settings.Test)return;PollCaptureAvailability();if(DateTime.UtcNow>=nextScan&&!discovering){var ignored=Discover();}
   foreach(var s in Sessions.Values.ToArray()){if(s.Managed)continue;try{if(s.Operation==null&&s.Connection!=null&&!s.Connection.Connected){s.Detail=s.Connection.Error;Release(s);s.Status="disconnected";s.RetryAt=DateTime.UtcNow.AddSeconds(5);Revision++;}}catch{Release(s);}}
   foreach(var session in Sessions.Values.ToArray())if(session.Status=="retry"&&!session.Managed&&!session.Paused&&!reserved.Contains(session.Id)&&DateTime.UtcNow>=session.RetryAt){var retry=Connect(session.Id,false);}
   if(GraphOwnsPlayback||!settings.Bool("AutoConnect"))return;
   foreach(string old in settings.Items<string>("RememberedSources").Concat(new[]{settings.Text("LastSourceId")}).Where(x=>!String.IsNullOrEmpty(x)).Distinct()){
    var candidates=Sources.Where(s=>s.Id==old||Address(old)!=""&&Address(s.Id)==Address(old)).ToArray();if(candidates.Length!=1)continue;string id=candidates[0].Id;if(reserved.Contains(id))continue;var session=Get(id);if(!session.Paused&&DateTime.UtcNow>=session.RetryAt){var ignored=Connect(id,false);}
   }
  }
  public void Dispose(){disposed=true;var releases=Sessions.Values.Select(Release).ToArray();if(!Task.WhenAll(releases).Wait(6500))throw new TimeoutException("手机后台通道仍在退出；主进程退出时将由进程作业统一清理。");}
 }
}
