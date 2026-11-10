using System;
using System.Runtime.InteropServices;
namespace AirBridge {
 [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IDevices {
  int EnumAudioEndpoints(int f,int s,out IntPtr p);
  [PreserveSig] int GetDefaultAudioEndpoint(int flow,int role,out IDevice device);
 }
 [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IDevice {
  [PreserveSig] int Activate(ref Guid iid,int context,IntPtr param,[MarshalAs(UnmanagedType.IUnknown)]out object result);
 }
 [ComImport,Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IVolume {
  [PreserveSig] int RegisterControlChangeNotify(IntPtr p);
  [PreserveSig] int UnregisterControlChangeNotify(IntPtr p);
  [PreserveSig] int GetChannelCount(out uint count);
  [PreserveSig] int SetMasterVolumeLevel(float v,ref Guid context);
  [PreserveSig] int SetMasterVolumeLevelScalar(float v,ref Guid context);
  [PreserveSig] int GetMasterVolumeLevel(out float v);
  [PreserveSig] int GetMasterVolumeLevelScalar(out float v);
  [PreserveSig] int SetChannelVolumeLevel(uint c,float v,ref Guid context);
  [PreserveSig] int SetChannelVolumeLevelScalar(uint c,float v,ref Guid context);
  [PreserveSig] int GetChannelVolumeLevel(uint c,out float v);
  [PreserveSig] int GetChannelVolumeLevelScalar(uint c,out float v);
  [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)]bool mute,ref Guid context);
  [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)]out bool mute);
 }
 public class VolumeState { public int Level; public bool Muted; }
 public static class AudioControl {
  static void WithVolume(Action<IVolume> action) {
   IDevices e=null; IDevice d=null; object v=null;
   try {
    e=(IDevices)AirBridge.Platform.CoreAudioFactory.Create(); Marshal.ThrowExceptionForHR(e.GetDefaultAudioEndpoint(0,1,out d));
    Guid id=typeof(IVolume).GUID;Marshal.ThrowExceptionForHR(d.Activate(ref id,23,IntPtr.Zero,out v));
    action((IVolume)v);
   } finally {if(v!=null)Marshal.ReleaseComObject(v);if(d!=null)Marshal.ReleaseComObject(d);if(e!=null)Marshal.ReleaseComObject(e);}
  }
  public static VolumeState Read() {var s=new VolumeState();WithVolume(delegate(IVolume v){float n;bool mute;Marshal.ThrowExceptionForHR(v.GetMasterVolumeLevelScalar(out n));Marshal.ThrowExceptionForHR(v.GetMute(out mute));s.Level=(int)Math.Round(n*100);s.Muted=mute;});return s;}
  public static void Set(int level) {WithVolume(delegate(IVolume v){Guid c=Guid.Empty;Marshal.ThrowExceptionForHR(v.SetMasterVolumeLevelScalar(Math.Max(0,Math.Min(100,level))/100f,ref c));});}
  public static void ToggleMute() {WithVolume(delegate(IVolume v){bool m;Guid c=Guid.Empty;Marshal.ThrowExceptionForHR(v.GetMute(out m));Marshal.ThrowExceptionForHR(v.SetMute(!m,ref c));});}
  public static bool VerifySameVolumeWrite() {bool ok=false;WithVolume(delegate(IVolume v){float before,after;Guid c=Guid.Empty;Marshal.ThrowExceptionForHR(v.GetMasterVolumeLevelScalar(out before));Marshal.ThrowExceptionForHR(v.SetMasterVolumeLevelScalar(before,ref c));Marshal.ThrowExceptionForHR(v.GetMasterVolumeLevelScalar(out after));ok=Math.Abs(after-before)<0.0001f;});return ok;}
 }
 public static class PodMatcher {
  public static PodReading Pick(PodReading[] items,string preferred,string output,DateTime now) {
   if(items==null)return null;
   var fresh=System.Linq.Enumerable.ToArray(System.Linq.Enumerable.OrderByDescending(System.Linq.Enumerable.Where(items,x=>(now-x.Seen).TotalSeconds>=0&&(now-x.Seen).TotalSeconds<=12&&x.Rssi>=-68),x=>x.Rssi));
   foreach(var r in fresh)if(!String.IsNullOrEmpty(preferred)&&r.Address==preferred)return r;
   if(String.IsNullOrEmpty(output)||output.IndexOf("AirPods",StringComparison.OrdinalIgnoreCase)<0)return null;
   if(fresh.Length==1)return fresh[0];
   if(fresh.Length>1&&fresh[0].Rssi-fresh[1].Rssi>=12)return fresh[0];
   return null;
  }
 }
}
