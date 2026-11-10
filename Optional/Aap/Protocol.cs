using System;
using System.Collections.Generic;
using System.IO;
namespace AirBridge.Aap {
 public enum NoiseMode : byte { Off=1, Cancellation=2, Transparency=3, Adaptive=4 }
 public enum EarState : byte { InEar=0, OutOfEar=1, InCase=2, Disconnected=3, Transitional=4, Unknown=255 }
 public sealed class Notification {
  public NoiseMode? Noise; public EarState? Primary,Secondary;
  public readonly Dictionary<byte,int?> Battery=new Dictionary<byte,int?>();
 }
 public static class Protocol {
  // L2CAP SDU boundaries are preserved. Bytes 2..3 are NOT a payload length.
  public static byte[] Handshake(){return new byte[]{0,0,4,0,1,0,2,0,0,0,0,0,0,0,0,0};}
  public static byte[] Subscribe(){return new byte[]{4,0,4,0,15,0,255,255,255,255};}
  public static byte[] SetNoise(NoiseMode mode){if(!Enum.IsDefined(typeof(NoiseMode),mode))throw new ArgumentOutOfRangeException("mode");return new byte[]{4,0,4,0,9,0,13,(byte)mode,0,0,0};}
  public static Notification Parse(byte[] data){
   if(data==null||data.Length<6||data.Length>4096||data[0]!=4||data[1]!=0||data[2]!=4||data[3]!=0||data[5]!=0)return null;
   var n=new Notification();
   if(data[4]==9){if(data.Length!=11||data[6]!=13||data[7]<1||data[7]>4||data[8]!=0||data[9]!=0||data[10]!=0)return null;n.Noise=(NoiseMode)data[7];return n;}
   if(data[4]==6){if(data.Length!=8)return null;n.Primary=data[6]<=4?(EarState)data[6]:EarState.Unknown;n.Secondary=data[7]<=4?(EarState)data[7]:EarState.Unknown;return n;}
   if(data[4]==4){if(data.Length<7||data[6]>8||data.Length!=7+5*data[6])return null;for(int i=7;i<data.Length;i+=5){byte part=data[i],status=data[i+3];if(data[i+1]!=1||data[i+4]!=1)return null;if(part!=2&&part!=4&&part!=8)continue;if(n.Battery.ContainsKey(part))return null;n.Battery[part]=data[i+2]<=100&&(status==1||status==2)?(int?)data[i+2]:null;}return n;}
   return null;
  }
 }
 public interface IChannel : IDisposable {
  void Connect(ulong address);void Send(byte[] packet);byte[] Receive(int timeoutMs);void Disconnect();
 }
 // Serialized operations: never reset a successful value on timeout; never infer
 // command success from SEND. Only a matching device report confirms a write.
 public sealed class Session : IDisposable {
  readonly IChannel channel;readonly object gate=new object();bool connected,disposed;
  public bool Ready {get;private set;} public NoiseMode? Noise {get;private set;}
  public EarState Primary {get;private set;} public EarState Secondary {get;private set;}
  public DateTime? NoiseUpdatedUtc {get;private set;} public DateTime? WearUpdatedUtc {get;private set;}
  public Session(IChannel channel){if(channel==null)throw new ArgumentNullException("channel");this.channel=channel;Primary=Secondary=EarState.Unknown;}
  public void Start(ulong address){lock(gate){if(disposed)throw new ObjectDisposedException("Session");if(connected)throw new InvalidOperationException("Session already open");try{channel.Connect(address);connected=true;channel.Send(Protocol.Handshake());channel.Send(Protocol.Subscribe());}catch{Stop();throw;}}}
  public Notification Poll(int timeoutMs){lock(gate){EnsureConnected();byte[] data;try{data=channel.Receive(timeoutMs);}catch{Ready=false;NoiseUpdatedUtc=null;WearUpdatedUtc=null;throw;}var n=Protocol.Parse(data);if(n==null)return null;Ready=true;var now=DateTime.UtcNow;if(n.Noise.HasValue){Noise=n.Noise;NoiseUpdatedUtc=now;}if(n.Primary.HasValue){Primary=n.Primary.Value;Secondary=n.Secondary.Value;WearUpdatedUtc=now;}return n;}}
  public void ChangeNoise(NoiseMode mode,bool modelSupportsMode,int timeoutMs){lock(gate){EnsureConnected();if(!Ready||!NoiseUpdatedUtc.HasValue||DateTime.UtcNow-NoiseUpdatedUtc.Value>TimeSpan.FromSeconds(30))throw new InvalidOperationException("Fresh noise-control report required");if(!modelSupportsMode)throw new NotSupportedException("Mode has not been enabled for this model");if(timeoutMs<1||timeoutMs>5000)throw new ArgumentOutOfRangeException("timeoutMs");channel.Send(Protocol.SetNoise(mode));var clock=System.Diagnostics.Stopwatch.StartNew();while(clock.ElapsedMilliseconds<timeoutMs){var n=Poll((int)Math.Max(1,Math.Min(200,timeoutMs-clock.ElapsedMilliseconds)));if(n!=null&&n.Noise==mode)return;}throw new TimeoutException("No matching device confirmation; state has not been assumed");}}
  void EnsureConnected(){if(disposed||!connected)throw new InvalidOperationException("AAP session is not connected");}
  public void Stop(){lock(gate){try{if(connected)channel.Disconnect();}finally{connected=false;Ready=false;Noise=null;NoiseUpdatedUtc=null;WearUpdatedUtc=null;Primary=Secondary=EarState.Unknown;}}}
  public void Dispose(){lock(gate){if(disposed)return;try{Stop();}finally{disposed=true;channel.Dispose();}}}
 }
}
