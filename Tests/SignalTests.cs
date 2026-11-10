using System;
using System.Threading;
class SignalTests {
 static int Main(string[] args){
  if(AirBridge.Monitor.IsUsableAdvertisementSignal(-127)||AirBridge.Monitor.IsUsableAdvertisementSignal(127)||!AirBridge.Monitor.IsUsableAdvertisementSignal(-63)||!AirBridge.Monitor.IsUsableAdvertisementSignal(0))throw new Exception("RSSI validity regression");
  Console.WriteLine("PASS: out-of-range sentinel rejected; real advertisement range retained");
  if(args.Length>0)using(var monitor=new AirBridge.Monitor()){monitor.EnsureRunning();Thread.Sleep(2500);foreach(string address in args){var s=monitor.SystemSignal(address,null);Console.WriteLine(address+": "+(s==null?"unavailable":s.Value+" system-cache connected="+s.Connected));}}
  return 0;
 }
}
