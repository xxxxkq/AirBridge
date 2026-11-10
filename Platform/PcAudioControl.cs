using System;
using System.Collections.Generic;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
namespace AirBridge.Routing {
 // Controls native application playback; never loopbacks the final mixed endpoint.
 // Keep AirBridge's own render sessions and the Windows Bluetooth receiver untouched.
 public sealed class PcAudioControl:IDisposable {
  readonly Dictionary<string,AudioSessionControl> changed=new Dictionary<string,AudioSessionControl>();readonly uint own=(uint)Process.GetCurrentProcess().Id;
  public bool Enabled=true;public uint[] OnlyProcesses;public uint[] ExceptProcesses;
  public void Tick(){
   if(Enabled){Restore();return;}
   using(var e=new MMDeviceEnumerator())foreach(var d in e.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active))using(d){
    var sessions=d.AudioSessionManager.Sessions;for(int i=0;i<sessions.Count;i++){var session=sessions[i];bool retained=false;try{
     if(session.State==AudioSessionState.AudioSessionStateExpired)continue;
     if(ExceptProcesses!=null&&Array.IndexOf(ExceptProcesses,session.GetProcessID)>=0){
      string restoredKey=d.ID+"|"+session.GetSessionInstanceIdentifier;AudioSessionControl prior;
      if(changed.TryGetValue(restoredKey,out prior)){prior.SimpleAudioVolume.Mute=false;prior.Dispose();changed.Remove(restoredKey);}continue;
     }
     if(OnlyProcesses!=null&&Array.IndexOf(OnlyProcesses,session.GetProcessID)<0)continue;
     if(session.GetProcessID==own||(session.DisplayName??"").IndexOf("A2DP SNK",StringComparison.OrdinalIgnoreCase)>=0)continue;
     string key=d.ID+"|"+session.GetSessionInstanceIdentifier;var volume=session.SimpleAudioVolume;
     if(!volume.Mute){if(!changed.ContainsKey(key)){changed.Add(key,session);retained=true;}volume.Mute=true;}
    }finally{if(!retained)session.Dispose();}}
   }
  }
  void Restore(){Exception error=null;foreach(var entry in new List<KeyValuePair<string,AudioSessionControl>>(changed)){try{entry.Value.SimpleAudioVolume.Mute=false;}catch(Exception ex){error=ex;}finally{entry.Value.Dispose();changed.Remove(entry.Key);}}if(error!=null)throw new InvalidOperationException("部分已关闭应用的静音恢复失败，请检查 Windows 音量合成器。",error);}
  public void Dispose(){Enabled=true;Restore();}
 }
}
