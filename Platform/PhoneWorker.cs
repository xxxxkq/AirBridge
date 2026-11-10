using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Audio;

namespace AirBridge.Routing {
 // One Windows Bluetooth audio owner per process. PCM never leaves this local pipe.
 public sealed class PhoneWorkerClient : IDisposable {
  // Serialize control operations only. Pipe readers and PCM callbacks never take this gate.
  static readonly object transportGate=new object();static long nextOperation;
  readonly long generation=Interlocked.Increment(ref nextOperation);int closing;
  static readonly System.Collections.Concurrent.ConcurrentDictionary<PhoneWorkerClient,byte> clients=new System.Collections.Concurrent.ConcurrentDictionary<PhoneWorkerClient,byte>();static int shuttingDown;
  // Process-wide terminal shutdown: reject late starts and close independent owners together.
  public static Task ShutdownAllAsync(){Interlocked.Exchange(ref shuttingDown,1);return Task.WhenAll(clients.Keys.Select(c=>Task.Run(()=>c.Dispose())).ToArray());}
  public static string DiagnosticPath;
  public volatile bool DiagnosticEnabled=true;
  public void SetDiagnostics(bool enabled){lock(lifetime){DiagnosticEnabled=enabled;if(disposed!=0||pipe==null||!Connected)return;var command=new BinaryWriter(pipe);command.Write(1);command.Write(enabled);command.Flush();}}
  static readonly AsyncAudioLog diagnosticLog=new AsyncAudioLog(256);
  int loggedPid;
  public static long DroppedDiagnosticMessages{get{return diagnosticLog.Dropped;}}
  public static long DiagnosticWriteErrors{get{return diagnosticLog.Errors;}}
  public static bool FlushDiagnostics(int milliseconds){return diagnosticLog.Flush(milliseconds);}
  void Log(string text){if(!DiagnosticEnabled||String.IsNullOrEmpty(DiagnosticPath))return;string path=DiagnosticPath;string line=DateTime.UtcNow.ToString("o")+" phone-worker "+mode+" address="+System.Text.RegularExpressions.Regex.Match(id??"","&([0-9a-f]{12})_",System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value+" pid="+Volatile.Read(ref loggedPid)+" generation="+generation+" "+text+Environment.NewLine;diagnosticLog.Post(()=>File.AppendAllText(path,line));}

  public PlaybackTuning Tuning=new PlaybackTuning();readonly string id,mode;readonly Action<byte[],int> receive;readonly Action<Exception> fail;
  readonly object lifetime=new object();readonly TaskCompletionSource<bool> ready=new TaskCompletionSource<bool>();
  NamedPipeServerStream pipe;Process child;WorkerJob job;Task reader;int disposed,failed;
  public long Frames,NonZeroSamples,Peak,ShortReportedFrames,TailSignalFrames,ClippedSamples;
  public volatile bool Connected;public string Error="";
  public int ProcessId {get{lock(lifetime)return child==null?0:child.Id;}}
  public PhoneWorkerClient(string id,Action<byte[],int> receive,Action<Exception> fail):this(id,"capture",receive,fail){}
  public PhoneWorkerClient(string id,string mode,Action<byte[],int> receive,Action<Exception> fail){this.id=id;this.mode=mode;this.receive=receive;this.fail=fail;}
  public string DiagnosticIdentity{get{return Volatile.Read(ref loggedPid)+":"+generation;}}
  public void Start(){
   var queued=Stopwatch.StartNew();lock(transportGate){
    if(Volatile.Read(ref closing)!=0||Volatile.Read(ref shuttingDown)!=0)throw new ObjectDisposedException("PhoneWorkerClient");
    Log("control-open begin queuedMs="+queued.ElapsedMilliseconds);
    try{StartCore();}finally{Log("control-open end");}
   }
  }
  void StartCore(){
   try{
    lock(lifetime){
     if(disposed!=0||Volatile.Read(ref closing)!=0)throw new ObjectDisposedException("PhoneWorkerClient");clients.TryAdd(this,0);if(Volatile.Read(ref shuttingDown)!=0)throw new ObjectDisposedException("PhoneWorkerClient");
     if(child!=null)throw new InvalidOperationException("Phone worker already started");
     string name="AirBridge.Phone."+Guid.NewGuid().ToString("N");
     var security=new PipeSecurity();var user=System.Security.Principal.WindowsIdentity.GetCurrent().User;
     security.SetAccessRuleProtection(true,false);security.AddAccessRule(new PipeAccessRule(user,PipeAccessRights.FullControl,System.Security.AccessControl.AccessControlType.Allow));
     pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,65536,65536,security);
     job=new WorkerJob();
     child=Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"AirBridge.exe"),"--phone-worker "+name){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
     // Worker cannot open audio until it receives the handshake below.
     job.Assign(child);Volatile.Write(ref loggedPid,child.Id);
     Log("started");
     reader=Task.Factory.StartNew(Read,CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
    }
    if(Task.WhenAny(ready.Task,Task.Delay(TimeSpan.FromSeconds(75))).GetAwaiter().GetResult()!=ready.Task)throw new TimeoutException("手机后台通道启动超时。");
    ready.Task.GetAwaiter().GetResult();
   }catch{Dispose();throw;}
  }
  void Read(){try{
   var connect=Task.Factory.FromAsync(pipe.BeginWaitForConnection,pipe.EndWaitForConnection,null);
   if(!connect.Wait(10000))throw new TimeoutException("手机后台进程未响应。");connect.GetAwaiter().GetResult();
   var writer=new BinaryWriter(pipe);writer.Write(2);writer.Write(mode);writer.Write(id);writer.Write(Tuning.CaptureRequests);writer.Write(Tuning.CompletedFrames);writer.Write(Tuning.CaptureWaitMs);writer.Flush();
   var input=new BinaryReader(pipe);
   while(disposed==0&&Volatile.Read(ref closing)==0){int kind=input.ReadInt32();if(kind==1){lock(lifetime){if(disposed!=0||Volatile.Read(ref closing)!=0)return;Connected=true;ready.TrySetResult(true);}}
    else if(kind==2){int n=input.ReadInt32();if(n!=1764)throw new InvalidDataException("Invalid phone PCM frame");
     Frames=input.ReadInt64();NonZeroSamples=input.ReadInt64();Peak=input.ReadInt64();ShortReportedFrames=input.ReadInt64();TailSignalFrames=input.ReadInt64();ClippedSamples=input.ReadInt64();
     byte[] data=input.ReadBytes(n);if(data.Length!=n)throw new EndOfStreamException();long received=AudioTimingTrace.Now();AudioTimingTrace.Record("ipc-received",id,received,Frames);if(receive!=null&&Volatile.Read(ref closing)==0)receive(data,n);AudioTimingTrace.Record("source-delivery",id,received,Frames);
    }else if(kind==3||kind==4){int n=input.ReadInt32();if(n<0||n>8192)throw new InvalidDataException("Invalid phone message frame");byte[] text=input.ReadBytes(n);if(text.Length!=n)throw new EndOfStreamException();string message=System.Text.Encoding.UTF8.GetString(text);if(kind==3)throw new IOException(message);Log(message);}
    else throw new InvalidDataException("Invalid phone worker message");
   }
  }catch(Exception ex){if(disposed==0){string detail=ex.GetBaseException().Message;try{if(child!=null&&child.WaitForExit(150))detail+=" (worker 0x"+child.ExitCode.ToString("X8")+")";}catch{}Report(new IOException("手机后台通道已停止："+detail,ex));}}
   finally{Connected=false;}
  }
  void Report(Exception ex){if(Volatile.Read(ref closing)!=0||Interlocked.Exchange(ref failed,1)!=0)return;Error=ex.Message;Connected=false;Log(Error);ready.TrySetException(ex);if(fail!=null&&Volatile.Read(ref closing)==0)try{fail(ex);}catch{}Task.Run(()=>{try{Dispose();}catch{}});}
  public void Dispose(){
   // Cancel an opening owner before waiting for the gate, so shutdown cannot wait 75s.
   Interlocked.Exchange(ref closing,1);Connected=false;ready.TrySetCanceled();
   lock(transportGate){if(disposed!=0)return;Log("control-close begin");try{DisposeCore();}finally{Log("control-close end");}}
  }
  void DisposeCore(){
   lock(lifetime){
    if(Interlocked.Exchange(ref disposed,1)!=0)return;Connected=false;ready.TrySetCanceled();
    // EOF asks the child to close its own KS/APC handles before we enforce a deadline.
    if(pipe!=null)try{pipe.Dispose();}catch{}
    try{if(child!=null)try{if(!child.WaitForExit(2500)){Log("close timed out; terminating owned worker");child.Kill();if(!child.WaitForExit(2500))throw new IOException("手机后台进程未能退出，未释放所有权。");}Log("released; frames="+Frames+" signal="+NonZeroSamples);}finally{child.Dispose();child=null;Volatile.Write(ref loggedPid,0);}}
    finally{if(job!=null){job.Dispose();job=null;}byte ignored;clients.TryRemove(this,out ignored);}
   }
  }
 }

 // KILL_ON_JOB_CLOSE guarantees cleanup even when the UI process crashes or is terminated.
 internal sealed class WorkerJob:IDisposable {
  IntPtr handle;
  [StructLayout(LayoutKind.Sequential)]struct Basic {public long ProcessTime,JobTime;public uint Flags;public UIntPtr Min,Max;public uint Active;public UIntPtr Affinity;public uint Priority,Scheduling;}
  [StructLayout(LayoutKind.Sequential)]struct Counters {public ulong A,B,C,D,E,F;}
  [StructLayout(LayoutKind.Sequential)]struct Extended {public Basic Basic;public Counters Io;public UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob;}
  [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr CreateJobObject(IntPtr security,string name);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetInformationJobObject(IntPtr job,int info,ref Extended value,int size);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
  public WorkerJob(){handle=CreateJobObject(IntPtr.Zero,null);if(handle==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();var value=new Extended();value.Basic.Flags=0x2000;if(!SetInformationJobObject(handle,9,ref value,Marshal.SizeOf(typeof(Extended)))){int error=Marshal.GetLastWin32Error();Dispose();throw new System.ComponentModel.Win32Exception(error);}}
  public void Assign(Process process){if(!AssignProcessToJobObject(handle,process.Handle))throw new System.ComponentModel.Win32Exception();}
  public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
 }

 public static class PhoneWorkerHost {
  static Task Convert(object operation,Type result){var m=typeof(System.WindowsRuntimeSystemExtensions).GetMethods().First(x=>x.Name=="AsTask"&&x.GetParameters().Length==1&&(result==null?!x.IsGenericMethod&&x.GetParameters()[0].ParameterType.Name=="IAsyncAction":x.IsGenericMethod&&x.GetGenericArguments().Length==1&&x.GetParameters()[0].ParameterType.Name=="IAsyncOperation`1"));return (Task)(result==null?m:m.MakeGenericMethod(result)).Invoke(null,new[]{operation});}
  public static int Run(string name){
   if(name==null||!System.Text.RegularExpressions.Regex.IsMatch(name,@"^AirBridge\.Phone\.[a-f0-9]{32}$"))return 2;
   using(var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous))using(var stop=new ManualResetEvent(false)){
    PhoneCapture capture=null;AudioPlaybackConnection receiver=null;object outputGate=new object();Mutex owner=null;bool owns=false;
    try{
     pipe.Connect(10000);var input=new BinaryReader(pipe);int protocol=input.ReadInt32();if(protocol!=1&&protocol!=2)return 2;
     string mode=input.ReadString(),id=input.ReadString();if(id.Length>4096)return 2;var tuning=new PlaybackTuning();if(protocol==2){tuning.CaptureRequests=input.ReadInt32();tuning.CompletedFrames=input.ReadInt32();tuning.CaptureWaitMs=input.ReadInt32();tuning.Validate();}
     if(mode=="capture"||mode=="receiver"){
      string identity=id.ToUpperInvariant();var address=System.Text.RegularExpressions.Regex.Match(identity,"&([0-9A-F]{12})_");if(address.Success)identity=address.Groups[1].Value;
      using(var hash=System.Security.Cryptography.SHA256.Create())identity=BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(identity))).Replace("-","");
      owner=new Mutex(false,@"Local\AirBridge.PhoneOwner."+identity);try{owns=owner.WaitOne(0);}catch(AbandonedMutexException){owns=true;}
      if(!owns)throw new IOException("此手机已有 AirBridge 音频通道，等待旧通道释放后再连接。");
     }
     var output=new BinaryWriter(pipe);var pcmPacket=new PhonePcmPacket();
     PhoneCapture.Diagnostic=text=>{long begin=AudioTimingTrace.Now();lock(outputGate){byte[] bytes=System.Text.Encoding.UTF8.GetBytes(text);if(bytes.Length>8192)Array.Resize(ref bytes,8192);output.Write(4);output.Write(bytes.Length);output.Write(bytes);output.Flush();}AudioTimingTrace.Record("diagnostic-send",id,begin);};
     Action<Exception> fail=ex=>{try{lock(outputGate){byte[] text=System.Text.Encoding.UTF8.GetBytes(ex.GetBaseException().Message);if(text.Length>8192)Array.Resize(ref text,8192);output.Write(3);output.Write(text.Length);output.Write(text);output.Flush();}}catch{}stop.Set();};
     // Dedicated reader: no UI dispatcher, no shared audio owner; EOF cancels this phone only.
     Task.Factory.StartNew(()=>{try{while(true){if(input.ReadInt32()!=1)break;PhoneCapture.DiagnosticEnabled=input.ReadBoolean();}}catch{}try{stop.Set();}catch(ObjectDisposedException){}},CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
     if(mode=="capture"){
      capture=new PhoneCapture(id,(data,count)=>{lock(outputGate){pcmPacket.Write(pipe,data,count,capture.Frames,capture.NonZeroSamples,capture.Peak,capture.ShortReportedFrames,capture.TailSignalFrames,capture.ClippedSamples);}},fail);
      capture.Tuning=tuning;capture.Start();
     }else if(mode=="receiver"){
      receiver=AudioPlaybackConnection.TryCreateFromId(id);if(receiver==null)throw new IOException("手机音频接口不可用。");
      var starting=Convert(receiver.StartAsync(),null);if(!starting.Wait(30000))throw new TimeoutException("手机连接启动超时。");starting.GetAwaiter().GetResult();
      var opening=(Task<AudioPlaybackConnectionOpenResult>)Convert(receiver.OpenAsync(),typeof(AudioPlaybackConnectionOpenResult));if(!opening.Wait(30000))throw new TimeoutException("手机连接打开超时。");
      if(opening.Result.Status!=AudioPlaybackConnectionOpenResultStatus.Success&&receiver.State!=AudioPlaybackConnectionState.Opened)throw new IOException("Bluetooth: "+opening.Result.Status+" "+opening.Result.ExtendedError);
     }else if(mode!="diagnostic-pcm"&&mode!="diagnostic-exit"&&mode!="diagnostic-hang"&&mode!="diagnostic-slow")throw new ArgumentException("Invalid worker mode");
     if(mode=="diagnostic-slow")Thread.Sleep(800);
     lock(outputGate){output.Write(1);output.Flush();}
     long frames=0;
     while(!stop.WaitOne(10)){
      if(mode=="diagnostic-exit")Environment.Exit(17);
      if(mode=="diagnostic-pcm"){byte[] data=new byte[1764];for(int i=0;i<data.Length;i+=2){data[i]=0x34;data[i+1]=0x12;}lock(outputGate){frames++;pcmPacket.Write(pipe,data,1764,frames,frames*882,0x1234,0,0,0);}}
      if(receiver!=null&&receiver.State==AudioPlaybackConnectionState.Closed)throw new IOException("手机蓝牙音频连接已关闭。");
     }
     if(mode=="diagnostic-hang")Thread.Sleep(Timeout.Infinite);
     return 0;
    }catch(Exception ex){try{lock(outputGate){var output=new BinaryWriter(pipe);byte[] text=System.Text.Encoding.UTF8.GetBytes(ex.GetBaseException().Message);if(text.Length>8192)Array.Resize(ref text,8192);output.Write(3);output.Write(text.Length);output.Write(text);output.Flush();}}catch{}return 1;}
    finally{try{if(capture!=null)capture.Dispose();if(receiver!=null)receiver.Dispose();}finally{if(owner!=null){if(owns)owner.ReleaseMutex();owner.Dispose();}}}
   }
  }
 }
}
