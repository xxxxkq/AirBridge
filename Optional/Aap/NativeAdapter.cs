using System;
using AirBridge.Core;
namespace AirBridge.Aap {
 // Explicitly constructed by a future opt-in host. Nothing registers this
 // transport with the production provider registry or starts a background task.
 public sealed class NativeAdapter : INativeHeadsetTransport {
  readonly string id;readonly Session session;readonly bool anc,adaptive;
  public NativeAdapter(string deviceId,int appleProductId,Session session){if(String.IsNullOrWhiteSpace(deviceId)||session==null)throw new ArgumentException("Device identity and session required");id=deviceId;this.session=session;anc=appleProductId==0x201b||appleProductId==0x200e||appleProductId==0x2014||appleProductId==0x2024||appleProductId==0x200a;adaptive=appleProductId==0x201b||appleProductId==0x2014||appleProductId==0x2024;}
  public bool IsReady(string deviceId){return deviceId==id&&session.Ready;}
  public string Read(string deviceId,string capability){if(!IsReady(deviceId))throw new InvalidOperationException("No confirmed AAP session");if(capability=="noise-control")return session.Noise.HasValue?session.Noise.ToString():"unknown";if(capability=="wear-detection")return session.Primary+"/"+session.Secondary;throw new NotSupportedException(capability);}
  public void Write(string deviceId,string capability,string value){if(!IsReady(deviceId))throw new InvalidOperationException("No confirmed AAP session");if(capability!="noise-control")throw new NotSupportedException("Only noise control writes are implemented");NoiseMode mode;if(!Enum.TryParse(value,true,out mode)||!Enum.IsDefined(typeof(NoiseMode),mode))throw new ArgumentException("Invalid noise mode");session.ChangeNoise(mode,anc&&(mode!=NoiseMode.Adaptive||adaptive),3000);}
  public void Dispose(){session.Dispose();}
 }
}
