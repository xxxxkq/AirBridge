using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Radios;
namespace AirBridge.Platform {
 public sealed class Hotkeys : NativeWindow, IDisposable {
  [DllImport("user32.dll",SetLastError=true)]static extern bool RegisterHotKey(IntPtr h,int id,uint mods,uint key);
  [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr h,int id);
  [DllImport("user32.dll")]static extern bool PostMessage(IntPtr h,int m,IntPtr w,IntPtr l);
  readonly Dictionary<int,string> bindings=new Dictionary<int,string>();readonly Queue<int> pending=new Queue<int>();
  public Hotkeys(){CreateHandle(new CreateParams{Caption="AirBridge hotkeys",Parent=new IntPtr(-3)});}
  public static uint[] Parse(string text){uint mods=0;Keys key=Keys.None;foreach(string raw in text.Split('+')){string p=raw.Trim();switch(p.ToUpperInvariant()){case "CTRL":mods|=2;break;case "ALT":mods|=1;break;case "SHIFT":mods|=4;break;default:if(key!=Keys.None||!Enum.TryParse<Keys>(p,true,out key))throw new ArgumentException("Use Ctrl/Alt + a letter, number or F-key");break;}}if((mods&3)==0||key==Keys.None||key==Keys.F12||key==Keys.ControlKey||key==Keys.Menu||key==Keys.ShiftKey)throw new ArgumentException("Use Ctrl or Alt with a key; F12 is reserved");return new[]{mods,(uint)key};}
  bool Register(int id,string text){var p=Parse(text);return RegisterHotKey(Handle,id,p[0]|0x4000,p[1]);}
  public void Set(int id,string text){string old;bindings.TryGetValue(id,out old);if(!String.IsNullOrWhiteSpace(text))Parse(text);if(old!=null)UnregisterHotKey(Handle,id);if(String.IsNullOrWhiteSpace(text)){bindings.Remove(id);return;}if(!Register(id,text)){int error=Marshal.GetLastWin32Error();if(old!=null)Register(id,old);throw new Win32Exception(error,"Shortcut unavailable or used by another application");}bindings[id]=text;}
  protected override void WndProc(ref Message m){if(m.Msg==0x312)pending.Enqueue(m.WParam.ToInt32());base.WndProc(ref m);}
  public int[] Drain(){var items=pending.ToArray();pending.Clear();return items;}
  public void TestDispatch(int id){PostMessage(Handle,0x312,new IntPtr(id),IntPtr.Zero);}
  public void Dispose(){foreach(int id in bindings.Keys)UnregisterHotKey(Handle,id);bindings.Clear();DestroyHandle();}
 }
 public static class TrayArtwork {
  [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr h);
  public static Icon Create(int battery,bool subdued,bool dark){using(var b=new Bitmap(32,32))using(var g=Graphics.FromImage(b)){
   g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.Transparent);
   Color color=subdued?Color.FromArgb(150,145,155,170):(dark?Color.White:Color.FromArgb(40,104,235));
   if(battery>=0){using(var font=new Font("Segoe UI",battery==100?11:14,FontStyle.Bold,GraphicsUnit.Pixel))using(var brush=new SolidBrush(color)){var sf=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};g.DrawString(battery.ToString(),font,brush,new RectangleF(0,0,32,32),sf);}}
   else using(var pen=new Pen(color,3)){g.DrawArc(pen,6,5,20,22,180,180);g.DrawLine(pen,6,16,6,25);g.DrawLine(pen,26,16,26,25);g.DrawLine(pen,9,18,9,27);g.DrawLine(pen,23,18,23,27);}
   IntPtr handle=b.GetHicon();try{return (Icon)Icon.FromHandle(handle).Clone();}finally{DestroyIcon(handle);}
  }}
 }
 [ComImport,Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]class PolicyClient{}
 [ComImport,Guid("F8679F50-850A-41CF-9C72-430F290290C8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IPolicy {
  // Reserved slots are intentionally never called. Windows 10/11 private ABI.
  void Slot0();void Slot1();void Slot2();void Slot3();void Slot4();void Slot5();void Slot6();void Slot7();void Slot8();void Slot9();
  [PreserveSig]int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)]string id,int role);
 }
 public static class OutputSwitch {
  public static void SelectRole(string id,int role){if(role<0||role>1)throw new ArgumentOutOfRangeException("role");if(!Audio.List().Any(e=>e.Id==id&&e.Active))throw new InvalidOperationException("Output is no longer available");IPolicy p=null;try{p=(IPolicy)new PolicyClient();Marshal.ThrowExceptionForHR(p.SetDefaultEndpoint(id,role));}finally{if(p!=null)Marshal.ReleaseComObject(p);}if(Audio.DefaultId(role)!=id)throw new InvalidOperationException("Windows did not change the output");}

  public static void Select(string id){if(!Audio.List().Any(e=>e.Id==id&&e.Active))throw new InvalidOperationException("Output is no longer available");string console=Audio.DefaultId(0),media=Audio.DefaultId(1);IPolicy p=null;try{p=(IPolicy)new PolicyClient();Marshal.ThrowExceptionForHR(p.SetDefaultEndpoint(id,0));Marshal.ThrowExceptionForHR(p.SetDefaultEndpoint(id,1));}catch{if(p!=null){p.SetDefaultEndpoint(console,0);p.SetDefaultEndpoint(media,1);}throw;}finally{if(p!=null)Marshal.ReleaseComObject(p);}if(!Audio.List().Any(e=>e.Id==id&&e.IsDefault))throw new InvalidOperationException("Windows did not change the output");}
  public static void RestoreRoles(string console,string media){IPolicy p=null;try{p=(IPolicy)new PolicyClient();Marshal.ThrowExceptionForHR(p.SetDefaultEndpoint(console,0));Marshal.ThrowExceptionForHR(p.SetDefaultEndpoint(media,1));}finally{if(p!=null)Marshal.ReleaseComObject(p);}}
  public static string Next(){var list=Audio.List().Where(e=>e.Active).OrderBy(e=>e.Id).ToArray();if(list.Length<2)throw new InvalidOperationException("Connect at least two playback devices");int current=Array.FindIndex(list,e=>e.IsDefault);var next=list[(current+1)%list.Length];Select(next.Id);return next.Name;}
 }
 public static class BluetoothRadio {
  static Task<T> Convert<T>(object operation){var method=typeof(System.WindowsRuntimeSystemExtensions).GetMethods().First(m=>m.Name=="AsTask"&&m.IsGenericMethod&&m.GetGenericArguments().Length==1&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType.Name=="IAsyncOperation`1");return (Task<T>)method.MakeGenericMethod(typeof(T)).Invoke(null,new[]{operation});}
  public static async Task<string> State(){var radios=await Convert<IReadOnlyList<Radio>>(Radio.GetRadiosAsync());return String.Join(", ",radios.Where(r=>r.Kind==RadioKind.Bluetooth).Select(r=>r.State.ToString()).ToArray());}
  public static async Task<string> Toggle(){var access=await Convert<RadioAccessStatus>(Radio.RequestAccessAsync());if(access!=RadioAccessStatus.Allowed)throw new InvalidOperationException("Bluetooth permission: "+access);var radios=await Convert<IReadOnlyList<Radio>>(Radio.GetRadiosAsync());var bluetooth=radios.Where(r=>r.Kind==RadioKind.Bluetooth).ToArray();if(bluetooth.Length==0)throw new InvalidOperationException("No Bluetooth radio found");var target=bluetooth.Any(r=>r.State==RadioState.On)?RadioState.Off:RadioState.On;foreach(var radio in bluetooth){var result=await Convert<RadioAccessStatus>(radio.SetStateAsync(target));if(result!=RadioAccessStatus.Allowed)throw new InvalidOperationException("Bluetooth change: "+result);}for(int i=0;i<20;i++){if(bluetooth.All(r=>r.State==target))return target.ToString();await Task.Delay(100);}throw new InvalidOperationException("Bluetooth did not reach the requested state");}
 }
}
