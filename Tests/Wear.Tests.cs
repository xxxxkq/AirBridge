using System;
using System.Linq;
using Windows.Media.Control;
using System.Threading.Tasks;
using AirBridge;
using AirBridge.Desktop;
class WearTests {
 static int checks; static void Check(bool pass,string name){checks++;if(!pass)throw new Exception(name);}
 static DateTime epoch=new DateTime(2026,11,9,0,0,0,DateTimeKind.Utc);
 static PodReading Reading(int count,double seconds){return new PodReading{Address="A",Model="AirPods 4 (ANC)",LeftInEar=count>0,RightInEar=count>1,Seen=epoch.AddSeconds(seconds)};}
 static byte[] Packet(int flags,int levels,int charging){var b=new byte[27];b[0]=7;b[1]=25;b[2]=1;b[3]=0x1b;b[4]=0x20;b[5]=(byte)flags;b[6]=(byte)levels;b[7]=(byte)charging;return b;}
 sealed class Session:IWearSession {
  public bool IsPlaying{get;set;}public bool IsPaused{get;set;}public bool CanPlay{get{return true;}}public bool CanPause{get{return true;}}
  public string Track{get;set;}public int Pauses,Plays;public bool Fail;
  public Task<bool> Pause(){Pauses++;if(!Fail){IsPlaying=false;IsPaused=true;}return Task.FromResult(!Fail);}
  public Task<bool> Play(){Plays++;IsPlaying=true;IsPaused=false;return Task.FromResult(true);}
 }
 sealed class Media:IWearMedia{public Session Session=new Session{IsPlaying=true,Track="one"};public Task<IWearSession> Current(){return Task.FromResult((IWearSession)Session);}public void Reset(){}}
 sealed class DelayedMedia:IWearMedia{public TaskCompletionSource<IWearSession> Result=new TaskCompletionSource<IWearSession>();public Task<IWearSession> Current(){return Result.Task;}public void Reset(){}}
 static async Task Tick(WearPlayback c,int n,double t){await c.Tick("device",Reading(n,t),epoch.AddSeconds(t));}
 static async Task Baseline(WearPlayback c){await Tick(c,2,0);await Tick(c,2,2);}
 static async Task Remove(WearPlayback c){await Tick(c,0,3);await Tick(c,0,5);}
 static async Task Return(WearPlayback c){await Tick(c,2,6);await Tick(c,2,8);}
 static async Task Run(){
  Check(AirBridge.Monitor.Decode(new byte[26],"A",-40)==null,"short frame");
  var left=AirBridge.Monitor.Decode(Packet(0x22,0x73,0x05),"A",-40);
  Check(left.Left==30&&left.Right==70&&left.Case==50,"left side battery mapping");
  Check(left.LeftInEar&&!left.RightInEar,"left ear bit mapping");
  var right=AirBridge.Monitor.Decode(Packet(2,0x73,5),"A",-40);
  Check(!right.LeftInEar&&right.RightInEar&&right.Right==30,"right ear bit mapping");
  Check(!AirBridge.Monitor.Decode(Packet(0x2a,0x73,0x35),"A",-40).LeftInEar,"charging excludes wear");
  var cased=AirBridge.Monitor.Decode(Packet(0x2e,0x73,5),"A",-40);
  Check(!cased.LeftInEar&&!cased.RightInEar&&cased.InCase,"case excludes wear");
  var invalid=AirBridge.Monitor.Decode(Packet(0,0xff,0x0f),"A",-40);var merge=PodReading.Merge(left,invalid);
  Check(merge.Left==30&&merge.Right==70&&merge.Case==50,"unknown battery retains last values");
  Check(merge.LeftAt==left.LeftAt&&merge.CaseAt==left.CaseAt,"unknown battery does not advance timestamp");
  invalid.Address="B";Check(PodReading.Merge(left,invalid).Left==-1,"different advertiser never inherits battery");
  left.Address="left";right.Address="right";left.Seen=right.Seen=epoch;left.Rssi=right.Rssi=-40;left.Left=30;left.Right=-1;right.Left=-1;right.Right=70;
  var pair=WearAdvertisements.Select(new[]{left,right},left.Model,epoch);Check(pair!=null&&pair.LeftInEar&&pair.RightInEar,"complementary ear advertisements merge");
  right.LeftBroadcast=true;Check(WearAdvertisements.Select(new[]{left,right},left.Model,epoch)==null,"duplicate sides ambiguous");right.LeftBroadcast=false;
  right.Left=90;Check(WearAdvertisements.Select(new[]{left,right},left.Model,epoch)==null,"contradictory batteries ambiguous");right.Left=-1;
  right.Rssi=-73;Check(WearAdvertisements.Select(new[]{right},right.Model,epoch)!=null,"normal body attenuation retained");right.Rssi=-90;Check(WearAdvertisements.Select(new[]{right},right.Model,epoch)==null,"very distant signal rejected");
  var state=new WearState();Check(state.Observe("d",Reading(2,0),epoch)==0&&state.Observe("d",Reading(2,2),epoch.AddSeconds(2))==0,"startup baseline no action");
  Check(state.Observe("d",Reading(0,3),epoch.AddSeconds(3))==0,"single packet no action");
  Check(state.Observe("d",Reading(2,4),epoch.AddSeconds(4))==0,"transient removal ignored");
  Check(state.Observe("d",Reading(0,5),epoch.AddSeconds(5))==0&&state.Observe("d",Reading(0,7),epoch.AddSeconds(7))==-1,"stable removal");
  Check(state.Observe("d",Reading(0,7),epoch.AddSeconds(8))==0,"duplicate frame ignored");
  Check(state.Observe("d",Reading(2,7),epoch.AddSeconds(20))==0&&state.Count==-1,"stale becomes unknown");
  var m=new Media();var c=new WearPlayback(m);await Baseline(c);Check(m.Session.Pauses==0,"initially worn does not pause");await Remove(c);Check(m.Session.Pauses==1,"pause playing session once");await Return(c);Check(m.Session.Plays==1,"resume own pause");
  m=new Media();m.Session.IsPlaying=false;m.Session.IsPaused=true;c=new WearPlayback(m);await Baseline(c);await Remove(c);await Return(c);Check(m.Session.Plays==0,"manual pause is never resumed");
  m=new Media();c=new WearPlayback(m);await Baseline(c);await Remove(c);var original=m.Session;m.Session=new Session{IsPaused=true,Track="other"};await Return(c);Check(m.Session.Plays==0&&original.Plays==0,"session switch clears ownership");
  m=new Media();c=new WearPlayback(m);await Baseline(c);await Remove(c);m.Session.Track="different";await Return(c);Check(m.Session.Plays==0,"track change clears ownership");
  m=new Media();c=new WearPlayback(m);await Baseline(c);await Remove(c);await c.Tick("",null,epoch.AddSeconds(6));await Return(c);Check(m.Session.Plays==0,"signal loss cancels resume");
  m=new Media();c=new WearPlayback(m);await Baseline(c);m.Session.Fail=true;await Remove(c);await Return(c);Check(m.Session.Plays==0,"failed pause never owns playback");
  m=new Media();c=new WearPlayback(m);await Baseline(c);await Remove(c);c.Reset();await Return(c);Check(m.Session.Plays==0,"disable or shutdown cancels resume");
  var delayed=new DelayedMedia();c=new WearPlayback(delayed);await Baseline(c);await Tick(c,0,3);var pendingPause=Tick(c,0,5);c.Stop();var late=new Session{IsPlaying=true};delayed.Result.SetResult(late);await pendingPause;Check(late.Pauses==0,"shutdown cancels late background media result");
  var random=new Random(42);for(int i=0;i<10000;i++){byte[] bytes=new byte[random.Next(40)];random.NextBytes(bytes);AirBridge.Monitor.Decode(bytes,"fuzz",-50);}Check(true,"10000 malformed frames");
  Console.WriteLine("PASS: "+checks+" checks; 10000 malformed frames; no media writes to Windows.");
 }
 static async Task Probe(){using(var monitor=new AirBridge.Monitor()){monitor.Start();for(int i=0;i<6;i++){await Task.Delay(2000);var readings=monitor.Snapshot();Console.WriteLine("BLE "+monitor.State+"; candidates="+readings.Length+"; "+monitor.Error);foreach(var r in readings)Console.WriteLine(r.Model+" battery="+r.Left+"/"+r.Right+"/"+r.Case+" rssi="+r.Rssi+" leftBroadcast="+r.LeftBroadcast+" charging="+r.LeftCharging+"/"+r.RightCharging+" inEar="+r.LeftInEar+"/"+r.RightInEar+" age="+(DateTime.UtcNow-r.Seen).TotalSeconds.ToString("F1"));}}var media=new WindowsWearMedia();var current=await media.Current();Console.WriteLine("Windows media read-only: "+(current==null?"no current session":"session available; playing="+current.IsPlaying+"; pauseSupported="+current.CanPause));}
 static async Task MediaProbe(){var op=GlobalSystemMediaTransportControlsSessionManager.RequestAsync();var method=typeof(System.WindowsRuntimeSystemExtensions).GetMethods().First(m=>m.Name=="AsTask"&&m.IsGenericMethod&&m.GetGenericArguments().Length==1&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType.Name=="IAsyncOperation`1");var manager=await (Task<GlobalSystemMediaTransportControlsSessionManager>)method.MakeGenericMethod(typeof(GlobalSystemMediaTransportControlsSessionManager)).Invoke(null,new object[]{op});Console.WriteLine("Current: "+(manager.GetCurrentSession()==null?"none":manager.GetCurrentSession().SourceAppUserModelId));foreach(var session in manager.GetSessions()){var info=session.GetPlaybackInfo();Console.WriteLine(session.SourceAppUserModelId+" status="+info.PlaybackStatus+" pause="+info.Controls.IsPauseEnabled+" play="+info.Controls.IsPlayEnabled);}}
 static int Main(string[] args){try{if(args.Length>0&&args[0]=="--media")MediaProbe().GetAwaiter().GetResult();else if(args.Length>0&&args[0]=="--probe")Probe().GetAwaiter().GetResult();else Run().GetAwaiter().GetResult();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}

