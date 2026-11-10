using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace AirBridge.Platform {
 [ComImport,Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IPhoneSessionManager {
  [PreserveSig]int GetAudioSessionControl(ref Guid g,uint flags,out IntPtr c);
  [PreserveSig]int GetSimpleAudioVolume(ref Guid g,uint flags,out IntPtr v);
  [PreserveSig]int GetSessionEnumerator(out IPhoneSessionEnumerator e);
 }
 [ComImport,Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IPhoneSessionEnumerator {
  [PreserveSig]int GetCount(out int count);
  [PreserveSig]int GetSession(int i,[MarshalAs(UnmanagedType.IUnknown)]out object control);
 }
 [ComImport,Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IPhoneSessionControl {
  [PreserveSig]int GetState(out int state);
  [PreserveSig]int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)]out string name);
  [PreserveSig]int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)]string name,ref Guid context);
  [PreserveSig]int GetIconPath([MarshalAs(UnmanagedType.LPWStr)]out string name);
  [PreserveSig]int SetIconPath([MarshalAs(UnmanagedType.LPWStr)]string name,ref Guid context);
  [PreserveSig]int GetGroupingParam(out Guid group);
  [PreserveSig]int SetGroupingParam(ref Guid group,ref Guid context);
  [PreserveSig]int RegisterAudioSessionNotification(IntPtr events);
  [PreserveSig]int UnregisterAudioSessionNotification(IntPtr events);
  [PreserveSig]int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)]out string id);
  [PreserveSig]int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)]out string id);
  [PreserveSig]int GetProcessId(out uint process);
 }
 [ComImport,Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IPhoneSessionVolume {
  [PreserveSig]int SetMasterVolume(float volume,ref Guid context);
  [PreserveSig]int GetMasterVolume(out float volume);
  [PreserveSig]int SetMute([MarshalAs(UnmanagedType.Bool)]bool mute,ref Guid context);
  [PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool mute);
 }
 public sealed class PhoneAudioSession {public string Endpoint,Name,Identifier,Instance;public uint ProcessId;public int State;public bool Muted;public float Volume;}
 public static class PhoneSessionVolume {
  static void Free(object value){if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
  static void Visit(Action<PhoneAudioSession,IPhoneSessionVolume> action){
   IDevices enumerator=null;
   try{enumerator=(IDevices)CoreAudioFactory.Create();foreach(var ep in Audio.List()){
    if(!ep.Active)continue;IDevice device=null;object manager=null;IPhoneSessionEnumerator sessions=null;
    try{Marshal.ThrowExceptionForHR(enumerator.GetDevice(ep.Id,out device));Guid iid=typeof(IPhoneSessionManager).GUID;Marshal.ThrowExceptionForHR(device.Activate(ref iid,23,IntPtr.Zero,out manager));Marshal.ThrowExceptionForHR(((IPhoneSessionManager)manager).GetSessionEnumerator(out sessions));int count;Marshal.ThrowExceptionForHR(sessions.GetCount(out count));
     for(int i=0;i<count;i++){object obj=null;try{Marshal.ThrowExceptionForHR(sessions.GetSession(i,out obj));var c=(IPhoneSessionControl)obj;var v=(IPhoneSessionVolume)obj;var info=new PhoneAudioSession{Endpoint=ep.Id};c.GetDisplayName(out info.Name);c.GetSessionIdentifier(out info.Identifier);c.GetSessionInstanceIdentifier(out info.Instance);c.GetProcessId(out info.ProcessId);c.GetState(out info.State);Marshal.ThrowExceptionForHR(v.GetMute(out info.Muted));Marshal.ThrowExceptionForHR(v.GetMasterVolume(out info.Volume));action(info,v);}finally{Free(obj);}}
    }finally{Free(sessions);Free(manager);Free(device);}
   }}finally{Free(enumerator);}
  }
  public static PhoneAudioSession[] List(){var list=new List<PhoneAudioSession>();Visit((info,volume)=>list.Add(info));return list.ToArray();}
  public static void SetMute(string endpoint,string instance,string name,uint processId,bool muted){
   Change(endpoint,instance,name,processId,volume=>{Guid context=Guid.Empty;Marshal.ThrowExceptionForHR(volume.SetMute(muted,ref context));bool actual;Marshal.ThrowExceptionForHR(volume.GetMute(out actual));if(actual!=muted)throw new InvalidOperationException("Phone session mute was not applied");});
  }
  public static void SetVolume(string endpoint,string instance,string name,uint processId,float value){
   if(Single.IsNaN(value)||Single.IsInfinity(value)||value<0||value>1)throw new ArgumentOutOfRangeException("value");
   Change(endpoint,instance,name,processId,volume=>{Guid context=Guid.Empty;Marshal.ThrowExceptionForHR(volume.SetMasterVolume(value,ref context));float actual;Marshal.ThrowExceptionForHR(volume.GetMasterVolume(out actual));if(Math.Abs(actual-value)>0.005f)throw new InvalidOperationException("Phone session volume was not applied");});
  }
  static void Change(string endpoint,string instance,string name,uint processId,Action<IPhoneSessionVolume> change){
   if(String.IsNullOrEmpty(endpoint)||String.IsNullOrEmpty(instance)||String.IsNullOrEmpty(name)||processId==0)throw new ArgumentException("Missing phone session identity");
   int matches=0;Visit((info,volume)=>{if(info.Endpoint==endpoint&&info.Instance==instance&&info.Name==name&&info.ProcessId==processId&&info.State!=2)matches++;});
   if(matches!=1)throw new InvalidOperationException("Phone audio session changed; refresh before retrying");
   bool applied=false;Visit((info,volume)=>{
    if(info.Endpoint!=endpoint||info.Instance!=instance||info.Name!=name||info.ProcessId!=processId||info.State==2)return;
    change(volume);applied=true;
   });if(!applied)throw new InvalidOperationException("Phone audio session disappeared");
  }
 }
}
