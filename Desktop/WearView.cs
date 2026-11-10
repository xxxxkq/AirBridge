using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using AirBridge.Platform;
namespace AirBridge.Desktop {
 internal sealed partial class MainView {
  readonly WearPlayback wearPlayback=new WearPlayback();
  readonly BackgroundQuery<string> wearQuery=new BackgroundQuery<string>();
  string lastWearStatus="",wearEligibility="";int wearEpoch;
  void TickWear(PodReading[] readings,DateTime now){
   var devices=inventory.Devices.Where(d=>d.IsActive&&d.IsDefault&&!Ignored(d.Id)&&AirBridge.Core.AppleProvider.SupportsBroadcastWear(d)&&settings.WearEnabled(d)).ToArray();
   PodReading pod=null;string id="",endpoint="";
   if(!settings.Test&&devices.Length==1){
    var profile=providers.Resolve(devices[0]);
    if(inventory.Devices.Count(d=>!Ignored(d.Id)&&providers.Resolve(d).Model==profile.Model)==1){pod=WearAdvertisements.Select(readings,profile.Model,now);id=devices[0].Id;endpoint=devices[0].EndpointId;}
   }
   string eligibility=pod==null?"":id;
   if(eligibility!=wearEligibility){wearEligibility=eligibility;Interlocked.Increment(ref wearEpoch);wearPlayback.CancelPending();}
   int epoch=wearEpoch;
   var pending=wearQuery.TryRun(async()=>{
    if(epoch!=Volatile.Read(ref wearEpoch))return null;
    monitor.EnsureRunning();
    var selected=pod;
    // COM and WinRT queries, including their synchronous prefixes, stay off WPF.
    if(selected!=null&&Audio.DefaultId(1)!=endpoint)selected=null;
    if(epoch!=Volatile.Read(ref wearEpoch))return null;
    await wearPlayback.Tick(id,selected,DateTime.UtcNow).ConfigureAwait(false);
    return wearPlayback.Status;
   });
   if(pending!=null)Run(async()=>{string status=await pending;if(exiting||epoch!=wearEpoch||status==null)return;if(lastWearStatus!=status){lastWearStatus=status;Log("Wear: "+status);}});
  }
 }
}
