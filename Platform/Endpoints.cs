using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace AirBridge.Platform {
 internal static class CoreAudioFactory {
  [DllImport("ole32.dll",ExactSpelling=true)]static extern int CoCreateInstance(ref Guid clsid,IntPtr outer,uint context,ref Guid iid,out IntPtr result);
  public static object Create(){Guid clsid=new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"),iid=new Guid("00000000-0000-0000-C000-000000000046");IntPtr pointer;
   Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid,IntPtr.Zero,1,ref iid,out pointer));
   try{return Marshal.GetUniqueObjectForIUnknown(pointer);}finally{Marshal.Release(pointer);}
  }
 }
 [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IDevices {
  [PreserveSig]int EnumAudioEndpoints(int flow,int state,out ICollection items);
  [PreserveSig]int GetDefaultAudioEndpoint(int flow,int role,out IDevice device);
  [PreserveSig]int GetDevice([MarshalAs(UnmanagedType.LPWStr)]string id,out IDevice device);
 }
 [ComImport,Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ICollection {
  [PreserveSig]int GetCount(out uint count); [PreserveSig]int Item(uint index,out IDevice device);
 }
 [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IDevice {
  [PreserveSig]int Activate(ref Guid id,int context,IntPtr p,[MarshalAs(UnmanagedType.IUnknown)]out object result);
  [PreserveSig]int OpenPropertyStore(int access,out IProperties properties);
  [PreserveSig]int GetId([MarshalAs(UnmanagedType.LPWStr)]out string id);
  [PreserveSig]int GetState(out int state);
 }
 [StructLayout(LayoutKind.Sequential)]struct Key {public Guid fmt;public int pid; public Key(string g,int p){fmt=new Guid(g);pid=p;}}
 [StructLayout(LayoutKind.Explicit,Size=24)]struct Value {[FieldOffset(0)]public ushort type;[FieldOffset(8)]public IntPtr pointer;[FieldOffset(8)]public uint number;}
 [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IProperties {
  [PreserveSig]int GetCount(out uint count);[PreserveSig]int GetAt(uint i,out Key key);[PreserveSig]int GetValue(ref Key key,out Value value);
 }
 [ComImport,Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IVolume {
  [PreserveSig]int RegisterControlChangeNotify(IntPtr p);[PreserveSig]int UnregisterControlChangeNotify(IntPtr p);[PreserveSig]int GetChannelCount(out uint c);
  [PreserveSig]int SetMasterVolumeLevel(float v,ref Guid c);[PreserveSig]int SetMasterVolumeLevelScalar(float v,ref Guid c);
  [PreserveSig]int GetMasterVolumeLevel(out float v);[PreserveSig]int GetMasterVolumeLevelScalar(out float v);
  [PreserveSig]int SetChannelVolumeLevel(uint c,float v,ref Guid x);[PreserveSig]int SetChannelVolumeLevelScalar(uint c,float v,ref Guid x);
  [PreserveSig]int GetChannelVolumeLevel(uint c,out float v);[PreserveSig]int GetChannelVolumeLevelScalar(uint c,out float v);
  [PreserveSig]int SetMute([MarshalAs(UnmanagedType.Bool)]bool m,ref Guid c);[PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool m);
 }
 public sealed class Endpoint {public string Id,Name,ContainerId;public bool Active,IsDefault;public int Channels,FormFactor,State;public bool IsCapture;}
 public sealed class Levels {public int Master,Left,Right;public bool Muted;}
 public static class Audio {
  [DllImport("ole32.dll")]static extern int PropVariantClear(ref Value value);
  static void Free(object o){if(o!=null&&Marshal.IsComObject(o))Marshal.ReleaseComObject(o);}
  static string Property(IProperties p,Key key){Value v;Marshal.ThrowExceptionForHR(p.GetValue(ref key,out v));try{if(v.type==31)return Marshal.PtrToStringUni(v.pointer);if(v.type==72)return ((Guid)Marshal.PtrToStructure(v.pointer,typeof(Guid))).ToString();return "";}finally{PropVariantClear(ref v);}}
  static int Number(IProperties p,Key key){Value v;if(p.GetValue(ref key,out v)<0)return -1;try{return v.type==19?(int)v.number:-1;}finally{PropVariantClear(ref v);}}
  public static string DefaultId(int role){IDevices e=null;IDevice d=null;try{e=(IDevices)CoreAudioFactory.Create();Marshal.ThrowExceptionForHR(e.GetDefaultAudioEndpoint(0,role,out d));string id;Marshal.ThrowExceptionForHR(d.GetId(out id));return id;}finally{Free(d);Free(e);}}
  public static Endpoint[] List(){return ListFlow(0);}
  public static Endpoint[] CaptureList(){return ListFlow(1);}
  static Endpoint[] ListFlow(int flow){var list=new List<Endpoint>();IDevices e=null;ICollection collection=null;IDevice def=null;
   try{e=(IDevices)CoreAudioFactory.Create();string defaultId="";if(e.GetDefaultAudioEndpoint(flow,1,out def)>=0)def.GetId(out defaultId);
    Marshal.ThrowExceptionForHR(e.EnumAudioEndpoints(flow,15,out collection));uint count;Marshal.ThrowExceptionForHR(collection.GetCount(out count));
    for(uint i=0;i<count;i++){IDevice d=null;IProperties p=null;object volume=null;try{Marshal.ThrowExceptionForHR(collection.Item(i,out d));string id;int state;d.GetId(out id);d.GetState(out state);d.OpenPropertyStore(0,out p);
     var item=new Endpoint{Id=id,State=state,IsCapture=flow==1,FormFactor=Number(p,new Key("1da5d803-d492-4edd-8c23-e0c0ffee7f0e",0)),Name=Property(p,new Key("a45c254e-df1c-4efd-8020-67d146a850e0",14)),ContainerId=Property(p,new Key("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c",2)),Active=(state&1)!=0,IsDefault=id==defaultId};
     if(item.Active){Guid iid=typeof(IVolume).GUID;if(d.Activate(ref iid,23,IntPtr.Zero,out volume)>=0){uint channels;((IVolume)volume).GetChannelCount(out channels);item.Channels=(int)channels;}}list.Add(item);
    }finally{Free(volume);Free(p);Free(d);}}return list.ToArray();
   }finally{Free(def);Free(collection);Free(e);}}
  static void With(string id,Action<IVolume> action){IDevices e=null;IDevice d=null;object v=null;try{e=(IDevices)CoreAudioFactory.Create();Marshal.ThrowExceptionForHR(String.IsNullOrEmpty(id)?e.GetDefaultAudioEndpoint(0,1,out d):e.GetDevice(id,out d));Guid iid=typeof(IVolume).GUID;Marshal.ThrowExceptionForHR(d.Activate(ref iid,23,IntPtr.Zero,out v));action((IVolume)v);}finally{Free(v);Free(d);Free(e);}}
  public static Levels Read(string id){var r=new Levels();With(id,delegate(IVolume v){float f;uint c;Marshal.ThrowExceptionForHR(v.GetMasterVolumeLevelScalar(out f));r.Master=(int)Math.Round(f*100);Marshal.ThrowExceptionForHR(v.GetMute(out r.Muted));v.GetChannelCount(out c);if(c>1){v.GetChannelVolumeLevelScalar(0,out f);r.Left=(int)Math.Round(f*100);v.GetChannelVolumeLevelScalar(1,out f);r.Right=(int)Math.Round(f*100);}});return r;}
  public static void Set(string id,int value){With(id,delegate(IVolume v){Guid c=Guid.Empty;Marshal.ThrowExceptionForHR(v.SetMasterVolumeLevelScalar(Math.Max(0,Math.Min(100,value))/100f,ref c));});}
  public static void Mute(string id,bool mute){With(id,delegate(IVolume v){Guid c=Guid.Empty;Marshal.ThrowExceptionForHR(v.SetMute(mute,ref c));});}
  public static void Channel(string id,int channel,int value){With(id,delegate(IVolume v){Guid c=Guid.Empty;uint n;Marshal.ThrowExceptionForHR(v.GetChannelCount(out n));if(channel<0||channel>=n)throw new ArgumentOutOfRangeException("channel");Marshal.ThrowExceptionForHR(v.SetChannelVolumeLevelScalar((uint)channel,Math.Max(0,Math.Min(100,value))/100f,ref c));});}
 }
}
