using System;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using Windows.Devices.Enumeration;
namespace AirBridge.Platform {
 public sealed class PhoneBatteryReading { public int Level=-1; public string Status="unavailable"; public string Stage="start"; public string Connection=""; public string AddressType=""; public DateTime ReadAt=DateTime.MinValue; }
 public static class PhoneBattery {
  static readonly object sync=new object();static bool closed;
  static readonly System.Collections.Generic.Dictionary<string,PhoneBatteryClient> clients=new System.Collections.Generic.Dictionary<string,PhoneBatteryClient>(StringComparer.OrdinalIgnoreCase);
  static PhoneBatteryClient Get(string key){lock(sync){if(closed)return null;PhoneBatteryClient client;if(!clients.TryGetValue(key,out client)){client=new PhoneBatteryClient();clients[key]=client;}return client;}}
  public static Task<PhoneBatteryReading> ReadAsync(ulong address){var c=Get(address.ToString());return c==null?Task.FromResult(new PhoneBatteryReading{Status="closed"}):c.ReadAsync(address);}
  public static Task<PhoneBatteryReading> ReadPairedAsync(string container){var c=Get(container);return c==null?Task.FromResult(new PhoneBatteryReading{Status="closed"}):c.ReadPairedAsync(container);}
  public static void Close(){lock(sync){closed=true;foreach(var client in clients.Values)client.Close();clients.Clear();}}
 }
 sealed class PhoneBatteryClient {
  static Task<T> Convert<T>(object operation,CancellationToken token){var method=typeof(System.WindowsRuntimeSystemExtensions).GetMethods().First(m=>m.Name=="AsTask"&&m.IsGenericMethod&&m.GetGenericArguments().Length==1&&m.GetParameters().Length==2&&m.GetParameters()[1].ParameterType==typeof(CancellationToken));return (Task<T>)method.MakeGenericMethod(typeof(T)).Invoke(null,new object[]{operation,token});}

  BluetoothLEDevice retained; GattSession session; string retainedKey="";
  readonly SemaphoreSlim gate=new SemaphoreSlim(1,1); bool stopping;
  public Task<PhoneBatteryReading> ReadAsync(ulong address) {return Task.Run(()=>ReadCoreAsync(address,null));}
  public Task<PhoneBatteryReading> ReadPairedAsync(string container) {return Task.Run(()=>ReadCoreAsync(0,container));}
  void Release(){if(session!=null){session.MaintainConnection=false;session.Dispose();session=null;}if(retained!=null){retained.Dispose();retained=null;}retainedKey="";}
  public void Close(){stopping=true;if(gate.Wait(0)){try{Release();}finally{gate.Release();}}}
  async Task<PhoneBatteryReading> ReadCoreAsync(ulong address,string container) {
   var result=new PhoneBatteryReading();
   using(var cancel=new CancellationTokenSource(20000)) {
    bool entered=false;
    try {
     await gate.WaitAsync(cancel.Token);entered=true;if(stopping){result.Status="closed";return result;}
     string key=container??address.ToString();
     if(retained==null || retainedKey!=key){
      Release();result.Stage="paired-device";
      if(container!=null){
       var devices=await Convert<DeviceInformationCollection>(DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),new[]{"System.Devices.ContainerId","System.Devices.Aep.ContainerId"}),cancel.Token);
       var matches=devices.Where(d=>d.Properties.Any(p=>(p.Key=="System.Devices.ContainerId" || p.Key=="System.Devices.Aep.ContainerId") && p.Value!=null && String.Equals(p.Value.ToString().Trim('{','}'),container.Trim('{','}'),StringComparison.OrdinalIgnoreCase))).ToArray();
       if(matches.Length!=1){result.Status=matches.Length==0?"paired-device-not-found":"ambiguous-device";return result;}
       retained=await Convert<BluetoothLEDevice>(BluetoothLEDevice.FromIdAsync(matches[0].Id),cancel.Token);
      }else{retained=await Convert<BluetoothLEDevice>(BluetoothLEDevice.FromBluetoothAddressAsync(address),cancel.Token);}
      if(retained==null){result.Status="unavailable";return result;}
      if(!retained.DeviceInformation.Pairing.IsPaired){Release();result.Status="not-paired";return result;}
      retainedKey=key;result.Stage="session";
      session=await Convert<GattSession>(GattSession.FromDeviceIdAsync(retained.BluetoothDeviceId),cancel.Token);
      if(session!=null && session.CanMaintainConnection)session.MaintainConnection=true;
     }
     var device=retained;result.Connection=device.ConnectionStatus.ToString();result.AddressType=device.BluetoothAddressType.ToString();
     result.Stage="service-discovery";
      var services=await Convert<GattDeviceServicesResult>(device.GetGattServicesForUuidAsync(GattServiceUuids.Battery,BluetoothCacheMode.Cached),cancel.Token);
      if(services.Status!=GattCommunicationStatus.Success){result.Status=services.Status.ToString();return result;}
      try {
       foreach(var service in services.Services) {
        result.Stage="characteristic-discovery";var chars=await Convert<GattCharacteristicsResult>(service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel,BluetoothCacheMode.Cached),cancel.Token);
        if(chars.Status!=GattCommunicationStatus.Success){result.Status=chars.Status.ToString();continue;}
        foreach(var characteristic in chars.Characteristics) {
         result.Stage="battery-read";var reading=await Convert<GattReadResult>(characteristic.ReadValueAsync(BluetoothCacheMode.Uncached),cancel.Token);
         result.Status=reading.Status.ToString()+(reading.ProtocolError.HasValue?" protocol="+reading.ProtocolError.Value.ToString("X2"):"");result.Connection=device.ConnectionStatus.ToString()+" / session="+(session==null?"none":session.SessionStatus.ToString());
         if(reading.Status!=GattCommunicationStatus.Success || reading.Value==null || reading.Value.Length!=1)continue;
         using(var reader=DataReader.FromBuffer(reading.Value)){int level=reader.ReadByte();if(level<=100){result.Connection=device.ConnectionStatus.ToString();result.Level=level;result.ReadAt=DateTime.UtcNow;result.Status="live";return result;}}
        }
       }
       if(result.Stage!="battery-read")result.Status="no-battery-service";
      } finally {foreach(var service in services.Services)service.Dispose();}
    } catch(OperationCanceledException){result.Status="timeout";} catch(Exception e){result.Status="error: "+e.GetType().Name+" 0x"+e.HResult.ToString("X8");}
    finally {if(entered){if(stopping)Release();gate.Release();}}
   }
   return result;
  }
 }
}
