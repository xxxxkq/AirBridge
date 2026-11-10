using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Control;
namespace AirBridge.Desktop {
 // Explicit pause/play on the same session; never broadcast a toggle media key.
 internal sealed class WearPlayback {
  readonly WearState state=new WearState();
  readonly IWearMedia media;
  IWearSession paused;
  public WearPlayback():this(new WindowsWearMedia()){}
  public WearPlayback(IWearMedia media){this.media=media;}
  DateTime pausedAt,nextAttempt,nextOwnershipCheck; string pausedTrack="",identity=""; int generation; bool busy;
  public string Status="等待可靠的佩戴广播 / Waiting for wear advertisements";
  volatile bool stopped;
  public void CancelPending(){System.Threading.Interlocked.Increment(ref generation);}
  public void Stop(){stopped=true;CancelPending();}
  public void Reset(){CancelPending();state.Reset();paused=null;identity="";}
  public async Task Tick(string device,PodReading reading,DateTime now){
   if(stopped)return;
   if(reading==null||(now-reading.Seen).TotalSeconds>12||reading.Seen>now){Reset();Status="佩戴状态未知 / Wear state unknown";return;}
   string key=device+"|"+reading.Model;
   if(key!=identity){Reset();identity=key;}
   if(busy)return;
   int action=state.Observe(device,reading,now);
   Status=state.Count<0?"正在确认佩戴状态 / Confirming wear state":"佩戴耳机数 / Worn earbuds: "+state.Count;
   if(now<nextAttempt)return;
   if(action==0&&(paused==null||now<nextOwnershipCheck))return;
   nextOwnershipCheck=now.AddSeconds(1);
   busy=true;int token=generation;
   try{
    var current=await media.Current();
    if(token!=generation)return;
    if(paused!=null&&(now-pausedAt>TimeSpan.FromMinutes(5)||!Object.Equals(current,paused)||!paused.IsPaused||paused.Track!=pausedTrack))paused=null;
    if(action<0){
     if(current==null){Status="未暂停：没有可控制的媒体会话 / No controllable session";return;}if(paused!=null)return;
     if(!current.IsPlaying||!current.CanPause){Status="未暂停：媒体状态 / Media state playing="+current.IsPlaying+", pauseEnabled="+current.CanPause;return;}
     string track=current.Track;
     if(stopped||token!=generation)return;
     if(await current.Pause()&&token==generation){paused=current;pausedAt=now;pausedTrack=track;Status="佩戴检测：已自动暂停 / Wear: paused";}
    }else if(action>0&&paused!=null){
     var target=paused;paused=null;
     if(Object.Equals(current,target)&&target.CanPlay&&token==generation&&await target.Play())Status="佩戴检测：已恢复播放 / Wear: resumed";
    }
   }catch(Exception ex){paused=null;media.Reset();nextAttempt=now.AddSeconds(30);Status="媒体控制不可用 / Media control unavailable: "+ex.GetBaseException().Message;}
   finally{busy=false;}
  }
 }

 internal interface IWearSession {
  bool IsPlaying {get;} bool IsPaused {get;} bool CanPlay {get;} bool CanPause {get;}
  string Track {get;} Task<bool> Pause(); Task<bool> Play();
 }
 internal interface IWearMedia {Task<IWearSession> Current();void Reset();}
 internal sealed class WindowsWearMedia : IWearMedia {
  static async Task<T> Convert<T>(object operation){
   using(var cancellation=new System.Threading.CancellationTokenSource(4000)){
    var method=typeof(System.WindowsRuntimeSystemExtensions).GetMethods().First(m=>m.Name=="AsTask"&&m.IsGenericMethod&&m.GetGenericArguments().Length==1&&m.GetParameters().Length==2&&m.GetParameters()[1].ParameterType==typeof(System.Threading.CancellationToken));
    return await (Task<T>)method.MakeGenericMethod(typeof(T)).Invoke(null,new object[]{operation,cancellation.Token});
   }
  }

  GlobalSystemMediaTransportControlsSessionManager manager;
  WindowsSession current;
  public void Reset(){manager=null;current=null;}
  public async Task<IWearSession> Current(){
   if(manager==null)manager=await Convert<GlobalSystemMediaTransportControlsSessionManager>(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
   var session=manager.GetCurrentSession();
   if(session==null||session.GetPlaybackInfo().PlaybackStatus!=GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing){
    var playing=manager.GetSessions().Where(s=>s.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing).ToArray();
    if(playing.Length==1)session=playing[0];
   }
   if(session==null){current=null;return null;}
   if(current==null||!Object.Equals(current.Session,session))current=new WindowsSession(session);
   var properties=await Convert<GlobalSystemMediaTransportControlsSessionMediaProperties>(session.TryGetMediaPropertiesAsync());
   current.Track=properties.Title+"|"+properties.Artist+"|"+properties.AlbumTitle;
   return current;
  }
  sealed class WindowsSession : IWearSession {
   public readonly GlobalSystemMediaTransportControlsSession Session;
   public WindowsSession(GlobalSystemMediaTransportControlsSession session){Session=session;}
   public string Track {get;set;}
   public bool IsPlaying {get{return Session.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;}}
   public bool IsPaused {get{return Session.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;}}
   public bool CanPlay {get{return Session.GetPlaybackInfo().Controls.IsPlayEnabled;}}
   public bool CanPause {get{return Session.GetPlaybackInfo().Controls.IsPauseEnabled;}}
   public Task<bool> Pause(){return Convert<bool>(Session.TryPauseAsync());}
   public Task<bool> Play(){return Convert<bool>(Session.TryPlayAsync());}
  }
 }
}


