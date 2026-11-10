using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
namespace AirBridge.Routing {
 public sealed class ProcessCapture:IDisposable {
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void Samples(IntPtr bytes,int count);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void Ready(int hr);
  [DllImport("AirBridge.Capture.dll",CallingConvention=CallingConvention.StdCall)]static extern int CaptureProcess(uint pid,bool exclude,IntPtr stop,Samples samples,Ready ready);
  readonly IntPtr stop=Marshal.AllocHGlobal(4);readonly Action<byte[],int> receive;readonly Action<Exception> fail;Task worker;bool disposed;
  public ProcessCapture(Action<byte[],int> receive,Action<Exception> fail){this.receive=receive;this.fail=fail;Marshal.WriteInt32(stop,0);}
  public void Start(uint pid,bool exclude){var ready=new TaskCompletionSource<bool>();worker=Task.Run(()=>{
   Samples samples=(pointer,count)=>{try{var data=new byte[count];Marshal.Copy(pointer,data,0,count);receive(data,count);}catch(Exception ex){Marshal.WriteInt32(stop,1);fail(ex);}};
   Ready started=hr=>{if(hr<0)ready.TrySetException(Marshal.GetExceptionForHR(hr));else ready.TrySetResult(true);};
   try{int hr=CaptureProcess(pid,exclude,stop,samples,started);if(hr<0){var error=Marshal.GetExceptionForHR(hr);ready.TrySetException(error);if(Marshal.ReadInt32(stop)==0)fail(error);}}catch(Exception ex){ready.TrySetException(ex);fail(ex);}finally{GC.KeepAlive(samples);GC.KeepAlive(started);}
  });ready.Task.GetAwaiter().GetResult();}
  public void Dispose(){if(disposed)return;disposed=true;Marshal.WriteInt32(stop,1);if(worker!=null)worker.GetAwaiter().GetResult();Marshal.FreeHGlobal(stop);}
 }
}
