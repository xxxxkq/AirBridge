using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using Windows.Media.Devices;

namespace AirBridge {
 public class PodReading {
  public string Address, Model;
  public int Left, Right, Case, Rssi;
  public bool LeftCharging, RightCharging, CaseCharging;
  public bool LeftInEar, RightInEar, InCase, LidOpen, LeftBroadcast;
  public DateTime LeftAt, RightAt, CaseAt;
  public PodReading Copy() { return (PodReading)MemberwiseClone(); }
  public static PodReading Merge(PodReading previous, PodReading next) {
   var result=next.Copy();
   if(previous==null||previous.Model!=next.Model||previous.Address!=next.Address)return result;
   if(next.Left<0){result.Left=previous.Left;result.LeftAt=previous.LeftAt;}
   if(next.Right<0){result.Right=previous.Right;result.RightAt=previous.RightAt;}
   if(next.Case<0){result.Case=previous.Case;result.CaseAt=previous.CaseAt;}
   return result;
  }
  public DateTime Seen;
  public override string ToString() { return Model + " · " + Address + " · " + Rssi + " dBm"; }
 }
 public class DeviceReading {
  public string Name, Id; public bool Connected; public int Battery = -1;
 }
 public class Inventory {
  public string Output = ""; public List<DeviceReading> Devices = new List<DeviceReading>();
 }
 public sealed class Monitor : IDisposable {
  public sealed class SignalReading { public int Rssi,Samples; public DateTime Seen; }
  public sealed class SignalAverage {
   readonly Queue<SignalReading> samples=new Queue<SignalReading>();public DateTime Seen{get;private set;}
   public void Add(int rssi,DateTime seen){if(!IsUsableAdvertisementSignal(rssi)||seen<=Seen)return;Prune(seen);Seen=seen;samples.Enqueue(new SignalReading{Rssi=rssi,Seen=seen});while(samples.Count>5)samples.Dequeue();}
   void Prune(DateTime now){while(samples.Count>0&&(now-samples.Peek().Seen).TotalSeconds>12)samples.Dequeue();}
   public SignalReading Read(DateTime now){Prune(now);if(samples.Count==0||Seen>now)return null;return new SignalReading{Rssi=(int)Math.Round(samples.Average(x=>x.Rssi),MidpointRounding.AwayFromZero),Samples=samples.Count,Seen=Seen};}
  }
  readonly Dictionary<string,SignalAverage> signals=new Dictionary<string,SignalAverage>(StringComparer.OrdinalIgnoreCase);
  public SignalReading Signal(string address) { lock(gate){SignalAverage value;return address!=null&&signals.TryGetValue(address,out value)?value.Read(DateTime.UtcNow):null;} }
  public sealed class SystemSignalReading { public string Address,Container;public int Value;public bool LowEnergy,Connected;public DateTime Queried; }
  readonly Dictionary<string,SystemSignalReading> systemSignals=new Dictionary<string,SystemSignalReading>();
  bool systemQuery,disposed;DateTime nextSystemQuery;
  static Task<T> PropertyTask<T>(object operation,System.Threading.CancellationToken token){var method=typeof(System.WindowsRuntimeSystemExtensions).GetMethods().First(m=>m.Name=="AsTask"&&m.IsGenericMethod&&m.GetGenericArguments().Length==1&&m.GetParameters().Length==2&&m.GetParameters()[1].ParameterType==typeof(System.Threading.CancellationToken));return (Task<T>)method.MakeGenericMethod(typeof(T)).Invoke(null,new object[]{operation,token});}
  public SystemSignalReading SystemSignal(string address,string container){lock(gate){return systemSignals.Values.Where(x=>x.Address==address||!String.IsNullOrEmpty(container)&&String.Equals(x.Container,container,StringComparison.OrdinalIgnoreCase)).OrderByDescending(x=>x.Connected).ThenByDescending(x=>x.LowEnergy).ThenByDescending(x=>x.Queried).FirstOrDefault();}}
  async Task PollSystemSignals(){
   if(disposed||systemQuery||DateTime.UtcNow<nextSystemQuery)return;systemQuery=true;nextSystemQuery=DateTime.UtcNow.AddSeconds(30);
   try{foreach(bool le in new[]{false,true}){
    string selector=le?Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelectorFromPairingState(true):Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromPairingState(true);
    using(var cancel=new System.Threading.CancellationTokenSource(10000)){
     var found=await PropertyTask<DeviceInformationCollection>(DeviceInformation.FindAllAsync(selector,new[]{"System.Devices.Aep.DeviceAddress","System.Devices.Aep.ContainerId","System.Devices.Aep.SignalStrength","System.Devices.Aep.IsConnected"},DeviceInformationKind.AssociationEndpoint),cancel.Token).ConfigureAwait(false);
     if(disposed)return;
     lock(gate)foreach(var d in found){object raw,address,container,connected;
      if(!d.Properties.TryGetValue("System.Devices.Aep.SignalStrength",out raw)||!(raw is int)||!d.Properties.TryGetValue("System.Devices.Aep.DeviceAddress",out address)||address==null)continue;
      d.Properties.TryGetValue("System.Devices.Aep.ContainerId",out container);d.Properties.TryGetValue("System.Devices.Aep.IsConnected",out connected);
      int value=(int)raw;if(value<=-127||value>127)continue;
      if(systemSignals.Count>=256&&!systemSignals.ContainsKey(d.Id))continue;
      systemSignals[d.Id]=new SystemSignalReading{Address=address.ToString().Replace(":","").ToUpperInvariant(),Container=container==null?"":container.ToString(),Value=value,LowEnergy=le,Connected=connected is bool&&(bool)connected,Queried=DateTime.UtcNow};
     }
    }
   }}catch{ /* Optional cached properties must never interrupt audio or broadcast monitoring. */ }
   finally{systemQuery=false;}
  }
  BluetoothLEAdvertisementWatcher watcher;
  readonly object gate = new object();
  readonly Dictionary<string,PodReading> readings = new Dictionary<string,PodReading>();
  DateTime restartAt;
  public string Error = "";
  public string State { get { return watcher == null ? "Stopped" : watcher.Status.ToString(); } }
  public void Start() {
   if (watcher != null) return;
   watcher = new BluetoothLEAdvertisementWatcher();
   watcher.ScanningMode = BluetoothLEScanningMode.Passive;
   watcher.Received += Received;
   watcher.Stopped += (s,e) => { Error = e.Error.ToString(); };
   try { watcher.Start(); } catch(Exception e) { Error = e.Message; }
  }
  public static PodReading Decode(byte[] b, string address, int rssi) {
   if(b == null || b.Length != 27 || b[0] != 7 || b[1] != 25 || b[2] != 1) return null;
   int model = b[3] | b[4] << 8;
   string name;
   switch(model) {
    case 0x2002: name="AirPods 1"; break; case 0x200f: name="AirPods 2"; break;
    case 0x2013: name="AirPods 3"; break; case 0x2019: name="AirPods 4"; break;
    case 0x201b: name="AirPods 4 (ANC)"; break;
    case 0x200e: name="AirPods Pro"; break; case 0x2014: name="AirPods Pro 2 (Lightning)"; break;
    case 0x2024: name="AirPods Pro 2 (USB-C)"; break; default: return null;
   }
   // Reverse-engineered proximity format; battery nibbles are approximate 10% steps.
   bool flip=(b[5]&0x20)!=0;
   int hi=(b[6]>>4)&15, lo=b[6]&15, flags=b[7]>>4;
   var now=DateTime.UtcNow;
   var reading=new PodReading { Address=address, Model=name, Rssi=rssi, Seen=now,
    Left=Level(flip?lo:hi), Right=Level(flip?hi:lo), Case=Level(b[7]&15),
    LeftCharging=(flags&(flip?1:2))!=0, RightCharging=(flags&(flip?2:1))!=0, CaseCharging=(flags&4)!=0 };
   reading.LeftBroadcast=flip;reading.InCase=(b[5]&4)!=0;reading.LidOpen=(b[8]&8)==0;
   reading.LeftInEar=!reading.InCase&&!reading.LeftCharging&&(b[5]&(flip?2:8))!=0;
   reading.RightInEar=!reading.InCase&&!reading.RightCharging&&(b[5]&(flip?8:2))!=0;
   reading.LeftAt=reading.Left>=0?now:DateTime.MinValue;reading.RightAt=reading.Right>=0?now:DateTime.MinValue;reading.CaseAt=reading.Case>=0?now:DateTime.MinValue;
   return reading;
  }
  static int Level(int nibble) { return nibble <= 10 ? nibble*10 : -1; }
  public static bool IsUsableAdvertisementSignal(int value){return value>-127&&value<=20;}
  void Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs e) {
   // -127 is Windows' out-of-range notification, carrying old advertisement data.
   // Do not refresh RSSI, battery or wear timestamps from this synthetic event.
   if(!IsUsableAdvertisementSignal(e.RawSignalStrengthInDBm))return;
   if(e.RawSignalStrengthInDBm>-127&&e.RawSignalStrengthInDBm<=20)lock(gate){
    var now=DateTime.UtcNow;
    if(signals.Count>=256)foreach(var key in signals.OrderBy(x=>x.Value.Seen).Take(64).Select(x=>x.Key).ToArray())signals.Remove(key);
    string address=e.BluetoothAddress.ToString("X12");SignalAverage average;if(!signals.TryGetValue(address,out average)){average=new SignalAverage();signals[address]=average;}average.Add(e.RawSignalStrengthInDBm,now);
   }
   foreach(var m in e.Advertisement.ManufacturerData) {
    if(m.CompanyId != 0x004c) continue;
    byte[] bytes=new byte[m.Data.Length];
    using(var reader=DataReader.FromBuffer(m.Data)) { reader.ReadBytes(bytes); }
    var item=Decode(bytes,e.BluetoothAddress.ToString("X12"),e.RawSignalStrengthInDBm);
    if(item!=null) lock(gate) { PodReading previous;readings.TryGetValue(item.Address,out previous);readings[item.Address]=PodReading.Merge(previous,item); }
   }
  }
  public void EnsureRunning() {
   var query=PollSystemSignals();
   var active=watcher;
   if(active!=null&&(active.Status==BluetoothLEAdvertisementWatcherStatus.Stopped||active.Status==BluetoothLEAdvertisementWatcherStatus.Aborted)&&DateTime.UtcNow>=restartAt){restartAt=DateTime.UtcNow.AddSeconds(15);try{active.Start();Error="";}catch(Exception ex){Error=ex.Message;}}
  }
  public PodReading[] Snapshot() {
   lock(gate) {
    foreach(var key in readings.Where(x=>(DateTime.UtcNow-x.Value.Seen).TotalMinutes>5).Select(x=>x.Key).ToArray()) readings.Remove(key);
    return readings.Values.OrderByDescending(x=>x.Seen).Select(x=>x.Copy()).ToArray();
   }
  }
  public void Dispose() { disposed=true;if(watcher!=null) { watcher.Received-=Received; watcher.Stop(); watcher=null; } }
 }
 public static class MediaKeys {
  [DllImport("user32.dll")] static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
  public static void Send(byte key) { keybd_event(key,0,0,UIntPtr.Zero); keybd_event(key,0,2,UIntPtr.Zero); }
 }
}

