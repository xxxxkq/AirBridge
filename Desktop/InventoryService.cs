using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AirBridge.Platform;
using AirBridge.Core;
namespace AirBridge.Desktop {
 internal sealed class BluetoothIdentity{public string Id,Name,Address,ContainerId,Brand;public int CachedBattery=-1;public ulong LeAddress;}
 internal sealed class DeviceInventory {
  public DateTime ReadAt=DateTime.Now;
  public AudioObservation AudioState=new AudioObservation();
  public Endpoint[] Endpoints=new Endpoint[0],Captures=new Endpoint[0];
  public DeviceIdentity[] Devices=new DeviceIdentity[0];
  public BluetoothIdentity[] BluetoothDevices=new BluetoothIdentity[0];
 }
 internal static class InventoryService {
  [StructLayout(LayoutKind.Sequential)]struct Info{public uint Size;public Guid Class;public uint DevInst;public IntPtr Reserved;}
  [StructLayout(LayoutKind.Sequential)]struct Key{public Guid Format;public uint Id;public Key(string format,uint id){Format=new Guid(format);Id=id;}}
  [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr SetupDiGetClassDevs(ref Guid guid,string enumerator,IntPtr parent,uint flags);
  [DllImport("setupapi.dll",SetLastError=true)]static extern bool SetupDiEnumDeviceInfo(IntPtr set,uint index,ref Info info);
  [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetupDiGetDeviceInstanceId(IntPtr set,ref Info info,StringBuilder id,int size,out uint required);
  [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetupDiGetDeviceProperty(IntPtr set,ref Info info,ref Key key,out uint type,byte[] value,uint size,out uint required,uint flags);
  [DllImport("setupapi.dll")]static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
  sealed class Node{public string Id,Name,Container;public int Battery=-1;}
  static byte[] Property(IntPtr set,ref Info info,Key key,out uint type){uint required;var b=new byte[4096];if(!SetupDiGetDeviceProperty(set,ref info,ref key,out type,b,(uint)b.Length,out required,0))return null;Array.Resize(ref b,(int)required);return b;}
  static List<Node> Bluetooth(){var result=new List<Node>();Guid bluetooth=new Guid("e0cbf06c-cd8b-4647-bb8a-263b43f0f974");IntPtr set=SetupDiGetClassDevs(ref bluetooth,null,IntPtr.Zero,2);if(set==new IntPtr(-1))throw new System.ComponentModel.Win32Exception();try{
   for(uint i=0;;i++){var info=new Info{Size=(uint)Marshal.SizeOf(typeof(Info))};if(!SetupDiEnumDeviceInfo(set,i,ref info)){if(Marshal.GetLastWin32Error()!=259)throw new System.ComponentModel.Win32Exception();break;}var id=new StringBuilder(2048);uint need,type;if(!SetupDiGetDeviceInstanceId(set,ref info,id,id.Capacity,out need))continue;
    var name=Property(set,ref info,new Key("a45c254e-df1c-4efd-8020-67d146a850e0",14),out type)??Property(set,ref info,new Key("a45c254e-df1c-4efd-8020-67d146a850e0",2),out type);
    var container=Property(set,ref info,new Key("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c",2),out type);
    var node=new Node{Id=id.ToString(),Name=name==null?id.ToString():Encoding.Unicode.GetString(name).TrimEnd('\0'),Container=container!=null&&container.Length==16?new Guid(container).ToString():""};
    var battery=Property(set,ref info,new Key("104ea319-6ee2-4701-bd47-8ddbf425bbe5",2),out type);if(battery!=null){int level=battery.Length==1?battery[0]:battery.Length==4?BitConverter.ToInt32(battery,0):-1;if(level>=0&&level<=100)node.Battery=level;}result.Add(node);
   }
  }finally{SetupDiDestroyDeviceInfoList(set);}return result;}
  static bool Match(string value,string pattern){return System.Text.RegularExpressions.Regex.IsMatch(value??"",pattern,System.Text.RegularExpressions.RegexOptions.IgnoreCase);}
  internal static bool HasPlaybackCapability(string hardware,bool endpoint){return endpoint||Match(hardware,"0000110B");}
  public static DeviceInventory Read(){var inventory=new DeviceInventory{Endpoints=Audio.List(),Captures=Audio.CaptureList()};var nodes=Bluetooth();var devices=new List<DeviceIdentity>();var records=new List<BluetoothIdentity>();
   foreach(var root in nodes.Where(n=>n.Id.StartsWith(@"BTHENUM\DEV_",StringComparison.OrdinalIgnoreCase))){string address=root.Id.Split('\\')[1].Substring(4);string hardware=String.Join(";",nodes.Where(n=>n.Id.IndexOf(address,StringComparison.OrdinalIgnoreCase)>=0).Select(n=>n.Id));var le=nodes.FirstOrDefault(n=>n.Id.StartsWith(@"BTHLE\DEV_",StringComparison.OrdinalIgnoreCase)&&n.Container==root.Container);ulong leAddress=0;if(le!=null)UInt64.TryParse(le.Id.Split('\\')[1].Substring(4),System.Globalization.NumberStyles.HexNumber,null,out leAddress);
    var record=new BluetoothIdentity{Id=root.Id,Name=root.Name,Address=address,ContainerId=root.Container,CachedBattery=root.Battery>=0?root.Battery:le==null?-1:le.Battery,LeAddress=leAddress,Brand=Match(root.Name,"iPhone|Apple")?"Apple":Match(root.Name,"Redmi|Xiaomi|POCO")?"Xiaomi / Redmi":""};records.Add(record);
    if(Match(hardware,"0000110A")||Match(root.Name,"iPhone|Redmi|Galaxy|Pixel|POCO"))continue;
    var ep=inventory.Endpoints.Where(e=>e.ContainerId==root.Container&&!String.IsNullOrEmpty(root.Container)).OrderByDescending(e=>e.IsDefault).ThenByDescending(e=>e.Active).FirstOrDefault();if(!HasPlaybackCapability(hardware,ep!=null))continue;
    var mic=inventory.Captures.Where(e=>e.ContainerId==root.Container&&!String.IsNullOrEmpty(root.Container)).OrderByDescending(e=>e.Active).FirstOrDefault();var d=new DeviceIdentity{Id=root.Id,ContainerId=root.Container,DisplayName=root.Name,HardwareIds=hardware,Battery=record.CachedBattery};if(ep!=null){d.EndpointId=ep.Id;d.IsDefault=ep.IsDefault;d.IsActive=ep.Active;d.Channels=ep.Channels;}if(mic!=null){d.CaptureId=mic.Id;d.CaptureActive=mic.Active;}devices.Add(d);
   }
   foreach(var ep in inventory.Endpoints.Concat(inventory.Captures)){if(devices.Any(d=>d.EndpointId==ep.Id||d.CaptureId==ep.Id)||records.Any(r=>!String.IsNullOrEmpty(ep.ContainerId)&&r.ContainerId==ep.ContainerId))continue;
    string kind=Match(ep.Name,"virtual|vb-audio|voicemeeter|虚拟|cable")?"virtual":ep.FormFactor==9?"display":ep.FormFactor==7||ep.FormFactor==8?"digital":ep.IsCapture?(ep.FormFactor==2?"linein":"microphone"):ep.FormFactor==3||ep.FormFactor==5?"headset":ep.FormFactor==1?"speaker":"audio";
    devices.Add(new DeviceIdentity{Id="endpoint:"+ep.Id,IsLocal=true,DisplayName=ep.Name,ContainerId=ep.ContainerId,Battery=-1,Kind=kind,IsActive=ep.Active,IsDefault=ep.IsDefault,Channels=ep.Channels,CaptureId=ep.IsCapture?ep.Id:null,CaptureActive=ep.IsCapture&&ep.Active,EndpointId=ep.IsCapture?null:ep.Id});
   }inventory.Devices=devices.ToArray();inventory.BluetoothDevices=records.ToArray();inventory.AudioState=AudioObservation.Read(inventory.Endpoints.Concat(inventory.Captures).Where(e=>e.Active).Select(e=>e.Id).ToArray());return inventory;
  }
 }
}
