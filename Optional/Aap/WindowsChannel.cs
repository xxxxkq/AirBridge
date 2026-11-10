using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace AirBridge.Aap {
 // Candidate NTPods ABI, pinned source documented in README.txt. No installer.
 public sealed class WindowsChannel : IChannel {
  public static readonly Guid InterfaceId=new Guid("C0FFEE00-1337-4A5B-9E6F-A1B2C3D4E5F6");
  [StructLayout(LayoutKind.Sequential)]struct DeviceInterface {public int Size;public Guid Class;public int Flags;public IntPtr Reserved;}
  [StructLayout(LayoutKind.Sequential)]struct Overlapped {public IntPtr Internal,InternalHigh;public uint Offset,OffsetHigh;public IntPtr Event;}
  [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr SetupDiGetClassDevs(ref Guid guid,IntPtr enumerator,IntPtr parent,uint flags);
  [DllImport("setupapi.dll",SetLastError=true)]static extern bool SetupDiEnumDeviceInterfaces(IntPtr info,IntPtr device,ref Guid guid,uint index,ref DeviceInterface item);
  [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr info,ref DeviceInterface item,IntPtr detail,int length,out int required,IntPtr device);
  [DllImport("setupapi.dll")]static extern bool SetupDiDestroyDeviceInfoList(IntPtr info);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool DeviceIoControl(SafeFileHandle file,uint code,IntPtr input,int inputSize,IntPtr output,int outputSize,out int returned,IntPtr overlapped);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetOverlappedResult(SafeFileHandle file,IntPtr overlapped,out int returned,bool wait);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool CancelIoEx(SafeFileHandle file,IntPtr overlapped);
  [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr CreateEvent(IntPtr security,bool manual,bool initial,string name);
  [DllImport("kernel32.dll")]static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
  // Enumeration only: no device handles, connection IOCTLs, pairing or settings.
  public static string[] Enumerate(){var result=new List<string>();Guid guid=InterfaceId;var info=SetupDiGetClassDevs(ref guid,IntPtr.Zero,IntPtr.Zero,18);if(info==new IntPtr(-1))throw new Win32Exception();try{for(uint i=0;;i++){var item=new DeviceInterface{Size=Marshal.SizeOf(typeof(DeviceInterface))};if(!SetupDiEnumDeviceInterfaces(info,IntPtr.Zero,ref guid,i,ref item)){int error=Marshal.GetLastWin32Error();if(error==259)break;throw new Win32Exception(error);}int size;SetupDiGetDeviceInterfaceDetail(info,ref item,IntPtr.Zero,0,out size,IntPtr.Zero);if(size<8||size>65536)throw new IOException("Invalid interface detail size");IntPtr detail=Marshal.AllocHGlobal(size);try{Marshal.WriteInt32(detail,IntPtr.Size==8?8:6);if(!SetupDiGetDeviceInterfaceDetail(info,ref item,detail,size,out size,IntPtr.Zero))throw new Win32Exception();result.Add(Marshal.PtrToStringUni(IntPtr.Add(detail,4)));}finally{Marshal.FreeHGlobal(detail);}}}finally{SetupDiDestroyDeviceInfoList(info);}return result.ToArray();}
  readonly string path;SafeFileHandle file;bool connected;
  public WindowsChannel(string path,bool explicitHardwareConsent){if(!explicitHardwareConsent)throw new InvalidOperationException("Hardware testing must be explicitly enabled");if(Array.IndexOf(Enumerate(),path)<0)throw new ArgumentException("Select an enumerated AAP interface");this.path=path;}
  public static byte[] ConnectPayload(ulong address){if(address==0||address>0xFFFFFFFFFFFF)throw new ArgumentOutOfRangeException("address");var data=new byte[10];Buffer.BlockCopy(BitConverter.GetBytes(address),0,data,0,8);data[8]=1;data[9]=16;return data;}
  byte[] Call(uint function,byte[] input,int capacity,int timeout){if(file==null||file.IsInvalid)throw new InvalidOperationException("Bridge not open");IntPtr a=IntPtr.Zero,b=IntPtr.Zero,ov=IntPtr.Zero,ev=IntPtr.Zero;bool pending=false;try{if(input!=null&&input.Length>0){a=Marshal.AllocHGlobal(input.Length);Marshal.Copy(input,0,a,input.Length);}if(capacity>0)b=Marshal.AllocHGlobal(capacity);ev=CreateEvent(IntPtr.Zero,true,false,null);if(ev==IntPtr.Zero)throw new Win32Exception();ov=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Overlapped)));Marshal.StructureToPtr(new Overlapped{Event=ev},ov,false);int count;bool ok=DeviceIoControl(file,0x80000000u|(function<<2),a,input==null?0:input.Length,b,capacity,out count,ov);if(!ok){int error=Marshal.GetLastWin32Error();if(error!=997)throw new Win32Exception(error);pending=true;uint wait=WaitForSingleObject(ev,(uint)timeout);if(wait!=0){CancelIoEx(file,ov);GetOverlappedResult(file,ov,out count,true);pending=false;throw new TimeoutException("Bridge I/O timeout");}ok=GetOverlappedResult(file,ov,out count,false);pending=false;if(!ok)throw new Win32Exception();}if(count<0||count>capacity)throw new IOException("Invalid bridge response length");var result=new byte[count];if(count>0)Marshal.Copy(b,result,0,count);return result;}finally{if(pending){CancelIoEx(file,ov);int ignored;GetOverlappedResult(file,ov,out ignored,true);}if(ov!=IntPtr.Zero)Marshal.FreeHGlobal(ov);if(ev!=IntPtr.Zero)CloseHandle(ev);if(a!=IntPtr.Zero)Marshal.FreeHGlobal(a);if(b!=IntPtr.Zero)Marshal.FreeHGlobal(b);}}
  public void Connect(ulong address){var input=ConnectPayload(address);if(file!=null)throw new InvalidOperationException("Already opened");file=CreateFile(path,0xC0000000,0,IntPtr.Zero,3,0x40000000,IntPtr.Zero);try{if(file.IsInvalid)throw new Win32Exception();var status=Call(0x804,null,32,1000);if(status.Length!=32||BitConverter.ToUInt32(status,0)!=0)throw new IOException("Unexpected ABI or occupied channel");var reply=Call(0x800,input,8,8000);if(reply.Length!=8||BitConverter.ToUInt32(reply,0)==0)throw new IOException("L2CAP channel open rejected");connected=true;}catch{file.Dispose();file=null;throw;}}
  public void Send(byte[] packet){if(!connected)throw new InvalidOperationException("Disconnected");if(packet==null||packet.Length==0||packet.Length>4096)throw new ArgumentException("Packet size");Call(0x802,packet,0,2000);}
  public byte[] Receive(int timeoutMs){if(!connected)throw new InvalidOperationException("Disconnected");if(timeoutMs<1||timeoutMs>1000)throw new ArgumentOutOfRangeException("timeoutMs");try{return Call(0x803,BitConverter.GetBytes((uint)timeoutMs),4096,timeoutMs+1000);}catch(Win32Exception ex){if(ex.NativeErrorCode==121||ex.NativeErrorCode==1460||ex.NativeErrorCode==258)return null;throw;}}
  public void Disconnect(){try{if(connected)Call(0x801,null,0,3000);}finally{connected=false;if(file!=null){file.Dispose();file=null;}}}
  public void Dispose(){Disconnect();}
 }
}
