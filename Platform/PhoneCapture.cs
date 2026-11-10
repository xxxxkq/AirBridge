using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Windows.Media.Audio;
namespace AirBridge.Routing {
 // Existing Microsoft BthA2dp capture path, x64, explicitly negotiated 44.1 kHz stereo PCM16.
 // No driver installation, registry writes, default-device changes or process loopback.
 internal sealed class PhoneCapture : IDisposable {
  internal static Action<string> Diagnostic;internal static volatile bool DiagnosticEnabled=true;
  static void Log(string text){try{if(DiagnosticEnabled&&Diagnostic!=null)Diagnostic(text);}catch{}}
  public PlaybackTuning Tuning=new PlaybackTuning();double maxSubmitMs;
  public long NonZeroSamples,Peak;public long Frames,ShortReportedFrames,TailSignalFrames,ClippedSamples;
  readonly string id;readonly Action<byte[],int> receive;readonly Action<Exception> fail;
  readonly CancellationTokenSource stop=new CancellationTokenSource();readonly TaskCompletionSource<bool> ready=new TaskCompletionSource<bool>();Task worker;
  public PhoneCapture(string id,Action<byte[],int> receive,Action<Exception> fail){this.id=id;this.receive=receive;this.fail=fail;}
  public static string FindPath(string id){
   var match=Regex.Match(id??"","&([0-9a-f]{12})_",RegexOptions.IgnoreCase);if(!match.Success)throw new ArgumentException("手机缺少蓝牙硬件地址，请重新发现设备。");
   var paths=new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
   using(var root=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture")){
    if(root!=null)foreach(var key in root.GetSubKeyNames())using(var p=root.OpenSubKey(key+@"\Properties")){if(p==null)continue;var value=p.GetValue("{233164c8-1b2c-4c7d-bc68-b671687a2567},1") as string;if(value!=null&&value.StartsWith("{2}.")&&value.IndexOf(match.Groups[1].Value+"_",StringComparison.OrdinalIgnoreCase)>=0&&value.IndexOf("{0000110a-",StringComparison.OrdinalIgnoreCase)>=0&&value.EndsWith(@"\snk",StringComparison.OrdinalIgnoreCase))paths.Add(value.Substring(4));}
   }
   if(paths.Count!=1)throw new InvalidOperationException("未找到唯一的手机独立音频接口。请确认手机已配对并启用媒体音频；不会回退为系统混音。");return paths.Single();
  }
  public void Start(){worker=Task.Factory.StartNew(Run,CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);ready.Task.GetAwaiter().GetResult();}
  static Task ConvertTask(object operation,Type type){var method=typeof(System.WindowsRuntimeSystemExtensions).GetMethods().First(m=>m.Name=="AsTask"&&m.GetParameters().Length==1&&(type==null?!m.IsGenericMethod&&m.GetParameters()[0].ParameterType.Name=="IAsyncAction":m.IsGenericMethod&&m.GetGenericArguments().Length==1&&m.GetParameters()[0].ParameterType.Name=="IAsyncOperation`1"));return (Task)(type==null?method:method.MakeGenericMethod(type)).Invoke(null,new[]{operation});}
  static void Await(Task task,CancellationToken token){task.Wait(token);task.GetAwaiter().GetResult();}
  AudioPlaybackConnection StartTransport(){var c=AudioPlaybackConnection.TryCreateFromId(id);if(c==null)throw new InvalidOperationException("无法创建手机音频连接。");try{using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token)){timeout.CancelAfter(30000);Await(ConvertTask(c.StartAsync(),null),timeout.Token);}Log("Start completed; state="+c.State);return c;}catch{c.Dispose();throw;}}
  void OpenTransport(AudioPlaybackConnection c){using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token)){timeout.CancelAfter(30000);var opening=(Task<AudioPlaybackConnectionOpenResult>)ConvertTask(c.OpenAsync(),typeof(AudioPlaybackConnectionOpenResult));Await(opening,timeout.Token);if(opening.Result.Status!=AudioPlaybackConnectionOpenResultStatus.Success&&c.State!=AudioPlaybackConnectionState.Opened)throw new InvalidOperationException("手机连接失败："+opening.Result.Status+" "+opening.Result.ExtendedError);}Log("Open completed; state="+c.State);}
  void Run(){try{
   if(IntPtr.Size!=8)throw new NotSupportedException("独立手机取流需要 64 位进程。");string path=FindPath(id);Log("Selected capture interface="+path);
   for(int generation=0;;generation++){
    bool repair=false;
    using(var connection=StartTransport()){
     try{using(var filter=CreateFile(path,0xC0000000,3,IntPtr.Zero,3,0,IntPtr.Zero)){
      if(filter.IsInvalid)throw new Win32Exception();
      using(var pin=OpenPinWithRetry(filter)){OpenTransport(connection);Capture(pin,connection);return;}
     }}catch(Win32Exception ex){if(ex.NativeErrorCode!=1450||generation!=0||stop.IsCancellationRequested)throw;repair=true;}
     if(repair){Log("1450 recovery: open own transport once before releasing it");OpenTransport(connection);}
    }
    // Complete one actual open/close lifecycle, then recreate the capture owner.
    // Never reset the adapter, close another process, or leave ordinary playback running.
    Log("1450 recovery: own transport disposed");if(stop.Token.WaitHandle.WaitOne(500))stop.Token.ThrowIfCancellationRequested();
   }
  }catch(Exception ex){Log("Failure="+ex.Message);ready.TrySetException(ex);if(!stop.IsCancellationRequested)fail(ex);}finally{ready.TrySetCanceled();}}
  void Capture(SafeFileHandle pin,AudioPlaybackConnection connection){using(var scheduling=new CaptureThreadScheduling(Log))CaptureCore(pin,connection);}
  void CaptureCore(SafeFileHandle pin,AudioPlaybackConnection connection){var slots=new Slot[Tuning.CaptureRequests];int state=0;try{
   for(int s=1;s<=2;s++){SetState(pin,s);state=s;}for(int i=0;i<slots.Length;i++){slots[i]=new Slot();slots[i].Sequence=i+1;slots[i].Submit(pin);}SetState(pin,3);state=3;ready.TrySetResult(true);
   long submitted=slots.Length;var completed=new CaptureCompletionOrder<CapturedFrame>(Tuning.CompletedFrames);var clock=System.Diagnostics.Stopwatch.StartNew();double stateAt=0,reportAt=5000;long reportFrames=0;double stateMs=0,sendMs=0,waitMs=0,lastFrameAt=-1,maxGapMs=0,maxSendMs=0;int gapsOver20=0,gapsOver50=0,waitTimeouts=0,readyBehind=0;double maxWaitMs=0;var frameBins=new CaptureFrameBins();var health=new PhoneTransportHealth();
   while(!stop.IsCancellationRequested){long waitBegin=AudioTimingTrace.Now();double waiting=clock.Elapsed.TotalMilliseconds;var frame=NextFrame(pin,slots,completed,ref submitted);bool complete=frame!=null;AudioTimingTrace.Record("capture-wait",id,waitBegin,complete?1:0,completed.Count,Frames);double waited=clock.Elapsed.TotalMilliseconds-waiting;waitMs+=waited;maxWaitMs=Math.Max(maxWaitMs,waited);if(!complete){waitTimeouts++;if(completed.Count>0)readyBehind++;}if(complete){
    var buffer=frame.Data;int used=frame.Used,flags=frame.Flags;long duration=frame.Duration;AudioTimingTrace.Record("capture-complete",id,AudioTimingTrace.Now(),Frames+1,used,flags);AudioTimingTrace.Record("capture-header",id,AudioTimingTrace.Now(),frame.Time,duration,flags);AudioTimingTrace.Record("capture-timebase",id,AudioTimingTrace.Now(),frame.Numerator,frame.Denominator,Frames+1);
    if(used<0||used>1764||used%4!=0||(flags&256)==0||duration!=100000)throw new InvalidOperationException("手机驱动返回了未支持的音频帧格式；已停止独立转发。");
    // For this explicitly negotiated BthA2dp 10-ms path, preserve the entire driver frame.
    // Zero before submitting, never erase samples after completion based on DataUsed.
    // Frame was copied before its native slot was resubmitted; delivery stays ordered.
    double frameAt=clock.Elapsed.TotalMilliseconds;if(lastFrameAt>=0){double gap=frameAt-lastFrameAt;maxGapMs=Math.Max(maxGapMs,gap);if(gap>20)gapsOver20++;if(gap>50)gapsOver50++;}lastFrameAt=frameAt;frameBins.Frame(frameAt);
    Interlocked.Increment(ref Frames);health.Frame(frameAt);if(used<buffer.Length)Interlocked.Increment(ref ShortReportedFrames);
    for(int k=used;k<buffer.Length;k++)if(buffer[k]!=0){Interlocked.Increment(ref TailSignalFrames);break;}
    long nonZero=0,clipped=0,peak=0;for(int k=0;k<buffer.Length;k+=2){int value=Math.Abs((int)BitConverter.ToInt16(buffer,k));if(value>0)nonZero++;if(value>peak)peak=value;if(value>=32760)clipped++;}
    Interlocked.Add(ref NonZeroSamples,nonZero);Interlocked.Add(ref ClippedSamples,clipped);if(peak>Peak)Interlocked.Exchange(ref Peak,peak);
    // Completed native slots were returned before ordered delivery or IPC.
    double sending=clock.Elapsed.TotalMilliseconds;long sendBegin=AudioTimingTrace.Now();receive(buffer,buffer.Length);AudioTimingTrace.Record("ipc-send",id,sendBegin,Frames);double sent=clock.Elapsed.TotalMilliseconds-sending;sendMs+=sent;maxSendMs=Math.Max(maxSendMs,sent);
   }
    // State is a COM/WinRT control-plane query, not part of each PCM frame.
    if(clock.Elapsed.TotalMilliseconds>=stateAt){double checking=clock.Elapsed.TotalMilliseconds;long stateBegin=AudioTimingTrace.Now();bool closed=connection.State==AudioPlaybackConnectionState.Closed;AudioTimingTrace.Record("transport-query",id,stateBegin,closed?1:0);if(closed!=health.Closed)Log("Transport state="+(closed?"Closed":"Opened")+"; frames="+Frames+"; verifying capture progress");if(health.Check(closed,checking))throw new InvalidOperationException("手机蓝牙音频连接已关闭，且连续 3 秒未收到音频帧。");stateMs+=clock.Elapsed.TotalMilliseconds-checking;stateAt=clock.Elapsed.TotalMilliseconds+500;}
    if(clock.Elapsed.TotalMilliseconds>=reportAt){Log("capture-window frames="+(Frames-reportFrames)+" waitMs="+(long)waitMs+" sendMs="+(long)sendMs+" stateMs="+(long)stateMs+" maxGapMs="+(long)maxGapMs+" gaps20="+gapsOver20+" gaps50="+gapsOver50+" maxSendMs="+(long)maxSendMs+" maxWaitMs="+(long)maxWaitMs+" waitTimeouts="+waitTimeouts+" readyBehind="+readyBehind+" maxSubmitMs="+maxSubmitMs.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" frames1s="+frameBins.Finish(clock.Elapsed.TotalMilliseconds));reportFrames=Frames;reportAt=clock.Elapsed.TotalMilliseconds+5000;stateMs=sendMs=waitMs=maxGapMs=maxSendMs=0;gapsOver20=gapsOver50=waitTimeouts=readyBehind=0;maxWaitMs=maxSubmitMs=0;}
   }
  }finally{for(int s=Math.Min(state,2);s>=0;s--){try{SetState(pin,s);}catch{}}foreach(var slot in slots)if(slot!=null){try{slot.Cancel(pin);}finally{slot.Dispose();}}}}
  sealed class CapturedFrame {public byte[] Data=new byte[1764];public int Used,Flags,Numerator,Denominator;public long Duration,Time;}
  CapturedFrame NextFrame(SafeFileHandle pin,Slot[] slots,CaptureCompletionOrder<CapturedFrame> completed,ref long submitted){
   var clock=System.Diagnostics.Stopwatch.StartNew();CapturedFrame next;
   while(!stop.IsCancellationRequested){
    if(completed.TryTake(out next))return next;
    foreach(var slot in slots){if(!completed.CanStore(slot.Sequence)||!slot.IsCompleted)continue;
     slot.Complete(pin,0);var frame=new CapturedFrame{Used=Marshal.ReadInt32(slot.Header,36),Flags=Marshal.ReadInt32(slot.Header,48),Duration=Marshal.ReadInt64(slot.Header,24),Time=Marshal.ReadInt64(slot.Header,8),Numerator=Marshal.ReadInt32(slot.Header,16),Denominator=Marshal.ReadInt32(slot.Header,20)};
     Marshal.Copy(slot.Data,frame.Data,0,frame.Data.Length);long sequence=slot.Sequence;completed.Store(sequence,frame);
     AudioTimingTrace.Record("capture-harvest",id,AudioTimingTrace.Now(),sequence,completed.Next,completed.Count);
     slot.Sequence=++submitted;long begin=System.Diagnostics.Stopwatch.GetTimestamp();slot.Submit(pin);maxSubmitMs=Math.Max(maxSubmitMs,(System.Diagnostics.Stopwatch.GetTimestamp()-begin)*1000.0/System.Diagnostics.Stopwatch.Frequency);AudioTimingTrace.Record("capture-submit",id,begin,submitted,completed.Count);
    }
    if(completed.TryTake(out next))return next;
    int remaining=Tuning.CaptureWaitMs-(int)clock.ElapsedMilliseconds;if(remaining<=0)return null;
    // Synchronous completions need not signal the event. Harvest again before waiting.
    if(slots.Any(slot=>completed.CanStore(slot.Sequence)&&slot.IsCompleted))continue;
    var handles=new System.Collections.Generic.List<IntPtr>{stop.Token.WaitHandle.SafeWaitHandle.DangerousGetHandle()};
    foreach(var slot in slots)if(completed.CanStore(slot.Sequence))handles.Add(slot.Event);
    uint result=WaitForMultipleObjects((uint)handles.Count,handles.ToArray(),false,(uint)remaining);
    if(result==0)return null;if(result==258)return null;if(result==0xffffffff)throw new Win32Exception(Marshal.GetLastWin32Error());
   }return null;
  }
  SafeFileHandle OpenPinWithRetry(SafeFileHandle filter){
   for(int attempt=0;;attempt++){stop.Token.ThrowIfCancellationRequested();try{return OpenPin(filter);}catch(Win32Exception ex){
    Log("Pin attempt="+attempt+" code="+ex.NativeErrorCode);if(!ShouldRetryPin(ex.NativeErrorCode,attempt))throw;
    // A disposed ordinary APC can take a short time to release its driver pin.
    if(stop.Token.WaitHandle.WaitOne(300))stop.Token.ThrowIfCancellationRequested();
   }}
  }
  internal static bool ShouldRetryPin(int code,int attempt){return attempt<10&&(code==32||code==33||code==170||code==1450);}
  int disposed;public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;stop.Cancel();try{if(worker!=null)worker.GetAwaiter().GetResult();}finally{stop.Dispose();}}
  [StructLayout(LayoutKind.Sequential)]struct Identifier{public Guid Set;public uint Id,Flags;}
  [StructLayout(LayoutKind.Sequential)]struct Connect{public Identifier Interface,Medium;public uint PinId;public IntPtr PinToHandle;public uint PriorityClass,PrioritySubClass;}
  static void U32(byte[] b,int at,uint value){Array.Copy(BitConverter.GetBytes(value),0,b,at,4);}static void U16(byte[] b,int at,ushort value){Array.Copy(BitConverter.GetBytes(value),0,b,at,2);}
  static SafeFileHandle OpenPin(SafeFileHandle filter){
   var conn=new Connect{Interface=new Identifier{Set=new Guid("1A8766A0-62CE-11CF-A5D6-28DB04C10000")},Medium=new Identifier{Set=new Guid("4747B320-62CE-11CF-A5D6-28DB04C10000")},PriorityClass=0x40000000,PrioritySubClass=1};
   byte[] fmt=new byte[88];U32(fmt,0,88);U32(fmt,8,4);Array.Copy(new Guid("73647561-0000-0010-8000-00AA00389B71").ToByteArray(),0,fmt,16,16);Array.Copy(new Guid("00000001-0000-0010-8000-00AA00389B71").ToByteArray(),0,fmt,32,16);Array.Copy(new Guid("05589F81-C356-11CE-BF01-00AA0055595A").ToByteArray(),0,fmt,48,16);U16(fmt,64,1);U16(fmt,66,2);U32(fmt,68,44100);U32(fmt,72,176400);U16(fmt,76,4);U16(fmt,78,16);
   int size=Marshal.SizeOf(typeof(Connect));var memory=Marshal.AllocHGlobal(size+fmt.Length);try{Marshal.StructureToPtr(conn,memory,false);Marshal.Copy(fmt,0,IntPtr.Add(memory,size),fmt.Length);SafeFileHandle pin;uint code=KsCreatePin(filter,memory,0xC0000000,out pin);if(code!=0||pin==null||pin.IsInvalid){if(pin!=null)pin.Dispose();throw new Win32Exception((int)code,"手机取流接口未能打开（Win32 "+code+"）。系统或驱动资源不足、旧通道释放延迟或其他接收程序占用均可能导致此错误；不能仅凭错误码确认原因。已保留图管理状态，不回退普通蓝牙播放。");}return pin;}finally{Marshal.FreeHGlobal(memory);}
  }
  static IntPtr Alloc(int n){var p=Marshal.AllocHGlobal(n);Marshal.Copy(new byte[n],0,p,n);return p;}
  static void SetState(SafeFileHandle pin,int state){IntPtr prop=Alloc(24),value=Alloc(4),ov=Alloc(32),ev=CreateEvent(IntPtr.Zero,true,false,null);try{Marshal.Copy(new Guid("1D58C920-AC9B-11CF-A5D6-28DB04C10000").ToByteArray(),0,prop,16);Marshal.WriteInt32(prop,20,2);Marshal.WriteInt32(value,state);Marshal.WriteIntPtr(ov,24,ev);uint bytes;bool ok=DeviceIoControl(pin,0x2F0003,prop,24,value,4,out bytes,ov);int error=ok?0:Marshal.GetLastWin32Error();if(error==997){if(WaitForSingleObject(ev,3000)!=0){CancelIoEx(pin,ov);GetOverlappedResult(pin,ov,out bytes,true);throw new TimeoutException("蓝牙音频状态切换超时。");}if(!GetOverlappedResult(pin,ov,out bytes,false))throw new Win32Exception();}else if(error!=0)throw new Win32Exception(error);}finally{Marshal.FreeHGlobal(prop);Marshal.FreeHGlobal(value);Marshal.FreeHGlobal(ov);CloseHandle(ev);}}
  sealed class Slot:IDisposable{static readonly byte[] EmptyData=new byte[1764],EmptyOv=new byte[32],EmptyHeader=new byte[56];public long Sequence;public IntPtr Header=Alloc(56),Data=Alloc(1764),Ov=Alloc(32),Event=CreateEvent(IntPtr.Zero,true,false,null);bool pending;
   public void Submit(SafeFileHandle pin){Marshal.Copy(EmptyData,0,Data,1764);ResetEvent(Event);Marshal.Copy(EmptyOv,0,Ov,32);Marshal.WriteIntPtr(Ov,24,Event);Marshal.Copy(EmptyHeader,0,Header,56);Marshal.WriteInt32(Header,56);Marshal.WriteInt32(Header,16,1);Marshal.WriteInt32(Header,20,1);Marshal.WriteInt32(Header,32,1764);Marshal.WriteIntPtr(Header,40,Data);uint n;bool ok=DeviceIoControl(pin,0x2F4017,IntPtr.Zero,0,Header,56,out n,Ov);int error=ok?0:Marshal.GetLastWin32Error();pending=!ok&&error==997;if(!ok&&!pending)throw new Win32Exception(error);}
   public bool IsCompleted{get{return !pending||WaitForSingleObject(Event,0)==0;}}
   public bool Complete(SafeFileHandle pin,uint timeout){if(pending){uint result=WaitForSingleObject(Event,timeout);if(result==258)return false;if(result!=0)throw new Win32Exception(Marshal.GetLastWin32Error(),"Capture event wait failed: "+result);uint n;if(!GetOverlappedResult(pin,Ov,out n,false))throw new Win32Exception();pending=false;}return true;}
   public void Cancel(SafeFileHandle pin){if(pending){CancelIoEx(pin,Ov);uint n;GetOverlappedResult(pin,Ov,out n,true);pending=false;}}
   public void Dispose(){Marshal.FreeHGlobal(Header);Marshal.FreeHGlobal(Data);Marshal.FreeHGlobal(Ov);CloseHandle(Event);}
  }
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
  [DllImport("ksuser.dll")]static extern uint KsCreatePin(SafeFileHandle filter,IntPtr connect,uint access,out SafeFileHandle pin);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool DeviceIoControl(SafeFileHandle h,uint code,IntPtr input,uint inputSize,IntPtr output,uint outputSize,out uint bytes,IntPtr ov);
  [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr CreateEvent(IntPtr a,bool manual,bool initial,string name);
  [DllImport("kernel32.dll")]static extern bool ResetEvent(IntPtr h);
  [DllImport("kernel32.dll",SetLastError=true)]static extern uint WaitForMultipleObjects(uint count,IntPtr[] handles,bool all,uint ms);
  [DllImport("kernel32.dll",SetLastError=true)]static extern uint WaitForSingleObject(IntPtr h,uint ms);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetOverlappedResult(SafeFileHandle h,IntPtr ov,out uint bytes,bool wait);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool CancelIoEx(SafeFileHandle h,IntPtr ov);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr h);
 }
 // APC control state can change when a peer phone closes. Do not tear down
 // a live capture solely because of that notification. Silence packets count
 // as progress; an opened, paused phone is never treated as a failed stream.
 internal sealed class PhoneTransportHealth {
  double closedAt,lastFrame;bool closedButStreaming;public bool Closed{get;private set;}
  public void Frame(double now){lastFrame=now;if(Closed)closedButStreaming=true;}
  public bool Check(bool closed,double now){if(!closed){Closed=false;closedButStreaming=false;return false;}if(!Closed){Closed=true;closedAt=now;closedButStreaming=false;}return !closedButStreaming&&now-Math.Max(closedAt,lastFrame)>=3000;}
 }
}
