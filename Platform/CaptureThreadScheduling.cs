using System;
using System.Runtime.InteropServices;
using System.Threading;
namespace AirBridge.Routing {
 // Only affects the calling capture thread. No process priority, registry or driver changes.
 internal sealed class CaptureThreadScheduling:IDisposable {
  IntPtr handle;readonly Action<string> log;readonly int thread;
  public CaptureThreadScheduling(Action<string> log){this.log=log;thread=Thread.CurrentThread.ManagedThreadId;
   if(Environment.GetEnvironmentVariable("AIRBRIDGE_CAPTURE_MMCSS")=="0"){log("capture-scheduling disabled");return;}
   try{uint task=0;handle=AvSetMmThreadCharacteristics("Audio",ref task);log(handle==IntPtr.Zero?"capture-scheduling unavailable error="+Marshal.GetLastWin32Error():"capture-scheduling Audio enabled task="+task);}
   catch(DllNotFoundException ex){log("capture-scheduling unavailable "+ex.GetType().Name);}
   catch(EntryPointNotFoundException ex){log("capture-scheduling unavailable "+ex.GetType().Name);}
  }
  public void Dispose(){if(handle==IntPtr.Zero)return;if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("Capture scheduling must be reverted on its owning thread");
   if(!AvRevertMmThreadCharacteristics(handle))log("capture-scheduling revert failed error="+Marshal.GetLastWin32Error());else log("capture-scheduling reverted");handle=IntPtr.Zero;
  }
  [DllImport("avrt.dll",EntryPoint="AvSetMmThreadCharacteristicsW",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr AvSetMmThreadCharacteristics(string task,ref uint index);
  [DllImport("avrt.dll",SetLastError=true)]static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);
 }
 // Five chronological 1s bins make pause/resume transitions visible without more log writes.
 public sealed class CaptureFrameBins {
  readonly long[] frames=new long[5];double start;
  public void Frame(double now){int bin=(int)Math.Max(0,Math.Min(4,(now-start)/1000));frames[bin]++;}
  public string Finish(double now){string result=String.Join(",",frames);Array.Clear(frames,0,frames.Length);start=now;return result;}
 }
}
