using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using AirBridge.Platform;
namespace AirBridge.Desktop {
 internal sealed class AudioObservation {
  public string DefaultId="";
  public Dictionary<string,Levels> Levels=new Dictionary<string,Levels>();
  public PhoneAudioSession[] Phones=new PhoneAudioSession[0];
  public static AudioObservation Read(string[] endpoints){
   var result=new AudioObservation();
   try{result.DefaultId=Audio.DefaultId(1);}catch{}
   foreach(string id in endpoints.Where(x=>!String.IsNullOrEmpty(x)).Distinct())try{result.Levels[id]=Audio.Read(id);}catch{}
   try{result.Phones=PhoneSessionVolume.List();}catch{}
   return result;
  }
 }
 internal sealed partial class MainView {
  AudioObservation audioObservation=new AudioObservation();
  readonly BackgroundQuery<AudioObservation> audioQuery=new BackgroundQuery<AudioObservation>();
  int audioWriteRevision;
  Levels ObservedLevel(string id){Levels level;if(!audioObservation.Levels.TryGetValue(id??audioObservation.DefaultId,out level))throw new InvalidOperationException("Audio status pending");return level;}
  void ObservedMute(string id,bool muted){Audio.Mute(id,muted);audioWriteRevision++;Levels level;if(audioObservation.Levels.TryGetValue(id,out level))level.Muted=muted;}
  void PollAudioObservation(){
   if(settings.Test||inventory==null)return;
   var source=inventory;int revision=audioWriteRevision;
   var ids=source.Endpoints.Concat(source.Captures).Where(e=>e.Active).Select(e=>e.Id).ToArray();
   var pending=audioQuery.TryRun(()=>Task.FromResult(AudioObservation.Read(ids)));
   if(pending!=null)Run(async()=>{var result=await pending;if(exiting||source!=inventory||revision!=audioWriteRevision)return;audioObservation=result;PollDevices();});
  }
 }
}
