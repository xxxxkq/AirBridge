using System;
using System.Runtime.InteropServices;
namespace AirBridge.Routing {
 // ABI reference: EarTrumpet IAudioPolicyConfigFactoryVariantFor21H2 / Downlevel.
 // Only render policy Get/Set are exposed; never clear all rules or change the system default.
 public sealed class AppOutputPolicy:IDisposable {
  [DllImport("combase.dll")]static extern int RoInitialize(uint kind);
  [DllImport("combase.dll")]static extern void RoUninitialize();
  [DllImport("combase.dll",CharSet=CharSet.Unicode)]static extern int WindowsCreateString(string value,uint length,out IntPtr text);
  [DllImport("combase.dll")]static extern int WindowsDeleteString(IntPtr text);
  [DllImport("combase.dll")]static extern IntPtr WindowsGetStringRawBuffer(IntPtr text,out uint length);
  [DllImport("combase.dll")]static extern int RoGetActivationFactory(IntPtr name,ref Guid iid,out IntPtr factory);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int GetPolicy(IntPtr self,uint pid,int flow,int role,out IntPtr value);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int SetPolicy(IntPtr self,uint pid,int flow,int role,IntPtr value);
  IntPtr factory;bool initialized;GetPolicy get;SetPolicy set;
  public AppOutputPolicy(){int hr=RoInitialize(1);if(hr<0&&hr!=unchecked((int)0x80010106))Marshal.ThrowExceptionForHR(hr);initialized=hr>=0;
   IntPtr name=IntPtr.Zero;try{string cls="Windows.Media.Internal.AudioPolicyConfig";Marshal.ThrowExceptionForHR(WindowsCreateString(cls,(uint)cls.Length,out name));
    Guid iid=new Guid("ab3d4648-e242-459f-b02f-541c70306324");hr=RoGetActivationFactory(name,ref iid,out factory);
    if(hr==unchecked((int)0x80004002)){iid=new Guid("2a59116d-6c4f-45e0-a74f-707e3fef9258");hr=RoGetActivationFactory(name,ref iid,out factory);}Marshal.ThrowExceptionForHR(hr);
    IntPtr table=Marshal.ReadIntPtr(factory);set=(SetPolicy)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table,25*IntPtr.Size),typeof(SetPolicy));get=(GetPolicy)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table,26*IntPtr.Size),typeof(GetPolicy));
   }catch{Dispose();throw;}finally{if(name!=IntPtr.Zero)WindowsDeleteString(name);}
  }
  public string Read(uint pid,int role){IntPtr result=IntPtr.Zero;try{int hr=get(factory,pid,0,role,out result);if(hr==unchecked((int)0x80070490))return "";Marshal.ThrowExceptionForHR(hr);if(result==IntPtr.Zero)return "";uint length;var p=WindowsGetStringRawBuffer(result,out length);return Marshal.PtrToStringUni(p,(int)length)??"";}finally{if(result!=IntPtr.Zero)WindowsDeleteString(result);}}
  public void Write(uint pid,int role,string value){IntPtr text=IntPtr.Zero;try{if(!String.IsNullOrEmpty(value))Marshal.ThrowExceptionForHR(WindowsCreateString(value,(uint)value.Length,out text));Marshal.ThrowExceptionForHR(set(factory,pid,0,role,text));}finally{if(text!=IntPtr.Zero)WindowsDeleteString(text);}}
  public static string RenderId(string endpoint){return endpoint=="default"?"":@"\\?\SWD#MMDEVAPI#"+endpoint+"#{e6327cad-dcec-4949-ae8a-991e976a79d2}";}
  public void Dispose(){if(factory!=IntPtr.Zero){Marshal.Release(factory);factory=IntPtr.Zero;}if(initialized){RoUninitialize();initialized=false;}}
 }
}
