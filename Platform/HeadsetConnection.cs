using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
namespace AirBridge.Platform {
 [ComImport,Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IBtTopology {
  [PreserveSig]int GetConnectorCount(out uint count);
  [PreserveSig]int GetConnector(uint index,out IBtConnector connector);
 }
 [ComImport,Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IBtConnector {
  [PreserveSig]int GetType(out int type); [PreserveSig]int GetDataFlow(out int flow);
  [PreserveSig]int ConnectTo(IBtConnector other); [PreserveSig]int Disconnect();
  [PreserveSig]int IsConnected([MarshalAs(UnmanagedType.Bool)]out bool connected);
  [PreserveSig]int GetConnectedTo(out IBtConnector other);
  [PreserveSig]int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)]out string id);
  [PreserveSig]int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)]out string id);
 }
 [StructLayout(LayoutKind.Sequential)] struct BtProperty {public Guid Set;public uint Id,Flags;}
 [ComImport,Guid("28F54685-06FD-11D2-B27A-00A0C9223196"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IBtKsControl {
  [PreserveSig]int KsProperty(ref BtProperty property,uint length,IntPtr data,uint dataLength,out uint returned);
 }
 public sealed class HeadsetConnectionResult {public bool Success;public string Error;}
 public static class HeadsetConnection {
  static void Free(object o){if(o!=null&&Marshal.IsComObject(o))Marshal.ReleaseComObject(o);}
  static string[] Adapters(string container){
   if(String.IsNullOrWhiteSpace(container))throw new ArgumentException("Missing device container");
   var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);IDevices devices=null;
   try{devices=(IDevices)CoreAudioFactory.Create();
    foreach(var endpoint in Audio.List().Where(e=>String.Equals(e.ContainerId,container,StringComparison.OrdinalIgnoreCase))){
     IDevice device=null;object topology=null;
     try{Marshal.ThrowExceptionForHR(devices.GetDevice(endpoint.Id,out device));Guid iid=typeof(IBtTopology).GUID;
      Marshal.ThrowExceptionForHR(device.Activate(ref iid,23,IntPtr.Zero,out topology));uint count;var t=(IBtTopology)topology;Marshal.ThrowExceptionForHR(t.GetConnectorCount(out count));
      for(uint i=0;i<count;i++){IBtConnector c=null;try{Marshal.ThrowExceptionForHR(t.GetConnector(i,out c));string id;if(c.GetDeviceIdConnectedTo(out id)>=0&&!String.IsNullOrEmpty(id))ids.Add(id);}finally{Free(c);}}
     }catch(Exception){}finally{Free(topology);Free(device);}
    }
   }finally{Free(devices);}return ids.ToArray();
  }
  // Only BASIC_SUPPORT (not GET) is used for this read-only diagnostic.
  public static string Probe(string container){return Invoke(container,true,true);}
  public static string[] InspectAdapters(string container){return Adapters(container);}
  static string Invoke(string container,bool connect,bool probe){
   var adapters=Adapters(container);if(adapters.Length==0)throw new NotSupportedException("No audio driver endpoint for this device");
   IDevices devices=null;int accepted=0,last=unchecked((int)0x80004002);
   try{devices=(IDevices)CoreAudioFactory.Create();foreach(string id in adapters){IDevice adapter=null;object control=null;IntPtr data=IntPtr.Zero;
    try{Marshal.ThrowExceptionForHR(devices.GetDevice(id,out adapter));Guid iid=typeof(IBtKsControl).GUID;Marshal.ThrowExceptionForHR(adapter.Activate(ref iid,23,IntPtr.Zero,out control));
     var property=new BtProperty{Set=new Guid("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D"),Id=connect?0u:1u,Flags=probe?0x200u:1u};uint returned;
     if(probe)data=Marshal.AllocHGlobal(4);
     last=((IBtKsControl)control).KsProperty(ref property,24,data,probe?4u:0u,out returned);
     if(last>=0)accepted++;
    }catch(Exception e){last=Marshal.GetHRForException(e);}finally{if(data!=IntPtr.Zero)Marshal.FreeHGlobal(data);Free(control);Free(adapter);}
   }}finally{Free(devices);}
   if(accepted==0)throw new NotSupportedException("Bluetooth audio driver rejected the request (0x"+last.ToString("X8")+")");
   return accepted+" driver request(s) accepted";
  }
  public static Task<HeadsetConnectionResult> Request(string container,bool connect){return Task.Run(()=>{
   try{Invoke(container,connect,false);
    // A successful KS request only means an attempt. Confirm the endpoint state.
    var deadline=DateTime.UtcNow.AddSeconds(20);
    while(DateTime.UtcNow<deadline){Thread.Sleep(500);var endpoints=Audio.List().Where(e=>String.Equals(e.ContainerId,container,StringComparison.OrdinalIgnoreCase)).ToArray();
     if(endpoints.Length>0 && endpoints.Any(e=>e.Active)==connect)return new HeadsetConnectionResult{Success=true};
    }return new HeadsetConnectionResult{Error="Driver request accepted, but the audio connection did not reach the requested state within 20 seconds"};
   }catch(Exception e){return new HeadsetConnectionResult{Error=e.Message};}
  });}
 }
}
