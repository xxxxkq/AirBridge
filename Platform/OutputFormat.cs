using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
namespace AirBridge.Routing {
 public sealed class PlaybackOutputException:IOException {
  public readonly Exception OriginalException;public readonly string EnglishMessage;public readonly bool NativeFormatRejected;
  public PlaybackOutputException(string device,WaveFormat format,string stage,Exception cause,bool nativeRejected=false):base("音频输出「"+device+"」"+stage+"失败："+Reason(cause,false,nativeRejected)+"（"+OutputFormatAdapter.Describe(format)+"，0x"+cause.HResult.ToString("X8")+"）。"){OriginalException=cause;NativeFormatRejected=nativeRejected;HResult=cause.HResult;EnglishMessage="Audio output '"+device+"' failed: "+Reason(cause,true,nativeRejected)+" ("+OutputFormatAdapter.Describe(format)+", 0x"+cause.HResult.ToString("X8")+").";}
  static string Reason(Exception ex,bool en,bool nativeRejected){if(nativeRejected)return en?"device rejects its own system mix format; check its connection or audio mode":"设备拒绝自身系统混音格式，请检查连接或音频模式";switch(unchecked((uint)ex.HResult)){case 0x88890008:return en?"unsupported audio format":"设备不接受该音频格式";case 0x88890004:return en?"device disconnected or reconfigured":"设备已断开或音频配置已变化";case 0x8889000A:return en?"device is in use":"设备被其他程序占用";case 0x88890010:return en?"Windows audio service unavailable":"Windows 音频服务不可用";case 0x80004001:return en?"driver does not support this audio operation":"驱动不支持当前音频操作";default:return ex is NotSupportedException?(en?"unsupported output encoding or channel layout":"尚不支持该输出编码或声道布局"):(en?"Windows could not open or play this device":"Windows 未能打开或播放此设备");}}
  public override string ToString(){return base.ToString()+Environment.NewLine+"Original output error: "+OriginalException;}
  public static string Display(Exception ex,bool english){var root=ex.GetBaseException();var output=root as PlaybackOutputException;return output!=null&&english?output.EnglishMessage:root.Message;}
 }
 public static class OutputFormatAdapter {
  static readonly Guid Pcm=new Guid("00000001-0000-0010-8000-00aa00389b71"),Float=new Guid("00000003-0000-0010-8000-00aa00389b71");
  public static string Describe(WaveFormat f){if(f==null)return "format unavailable";var e=f as WaveFormatExtensible;bool floating=f.Encoding==WaveFormatEncoding.IeeeFloat||e!=null&&e.SubFormat==Float;return f.SampleRate+" Hz / "+f.BitsPerSample+"-bit "+(floating?"float":"PCM")+" / "+f.Channels+" ch";}
  static byte[] Header(WaveFormat f){using(var stream=new MemoryStream()){using(var w=new BinaryWriter(stream)){f.Serialize(w);return stream.ToArray();}}}
  public static WaveFormat[] Candidates(WaveFormat mix,WaveFormat source){var list=new System.Collections.Generic.List<WaveFormat>();if(mix!=null){list.Add(mix);var ext=mix as WaveFormatExtensible;if(ext!=null){var standard=ext.ToStandardWaveFormat();if(!Object.ReferenceEquals(ext,standard))list.Add(standard);}}if(source!=null)list.Add(source);return list.GroupBy(f=>System.Convert.ToBase64String(Header(f))).Select(g=>g.First()).ToArray();}
  public static IWaveProvider Initialize(IWaveProvider source,WaveFormat mix,string device,Func<WaveFormat,bool> supports,Action<IWaveProvider> initialize,Action<string> diagnostic){
   WaveFormat attempted=mix;Exception last=null;bool nativeRejected=false;
   foreach(var f in Candidates(mix,source.WaveFormat)){attempted=f;bool accepted=false;try{if(!supports(f)){Note(diagnostic,"unsupported "+Describe(f));continue;}accepted=true;var converted=Convert(source,f);Note(diagnostic,"initialize "+Describe(f));initialize(converted);Note(diagnostic,"selected "+Describe(f));return converted;}catch(Exception ex){last=ex;if(accepted&&Object.ReferenceEquals(f,mix)&&ex.HResult==unchecked((int)0x88890008))nativeRejected=true;Note(diagnostic,"failed 0x"+ex.HResult.ToString("X8")+" "+Describe(f));if(ex.HResult!=unchecked((int)0x88890008)&&!(ex is NotSupportedException))throw new PlaybackOutputException(device,attempted,"初始化",ex);}}
   if(last==null)last=new COMException("No supported shared-mode format",unchecked((int)0x88890008));throw new PlaybackOutputException(device,nativeRejected?mix:attempted,"初始化",last,nativeRejected);
  }
  static void Note(Action<string> write,string text){if(write!=null)try{write(text);}catch{/* Diagnostic failures cannot prevent output initialization. */}}
  public static IWaveProvider Convert(IWaveProvider source,WaveFormat target){if(source==null||target==null)throw new ArgumentNullException();var ext=target as WaveFormatExtensible;bool floating=target.Encoding==WaveFormatEncoding.IeeeFloat||ext!=null&&ext.SubFormat==Float;bool pcm=target.Encoding==WaveFormatEncoding.Pcm||ext!=null&&ext.SubFormat==Pcm;
   if((!pcm&&!floating)||floating&&target.BitsPerSample!=32||pcm&&!new[]{8,16,24,32}.Contains(target.BitsPerSample)||target.SampleRate<8000||target.SampleRate>384000||target.Channels<1||target.Channels>32||target.BlockAlign!=target.Channels*(target.BitsPerSample/8))throw new NotSupportedException("Unsupported output format: "+target);
   int valid=target.BitsPerSample,mask=0;if(ext!=null){var h=Header(target);valid=BitConverter.ToInt16(h,22);mask=BitConverter.ToInt32(h,24);if(valid==0)valid=target.BitsPerSample;if(valid<1||valid>target.BitsPerSample)throw new NotSupportedException("Invalid valid bits");}
   // Preserve bit-for-bit PCM when no conversion or layout change is required.
   if(target.Encoding==WaveFormatEncoding.Pcm&&source.WaveFormat.Equals(target))return source;
   ISampleProvider samples=source.ToSampleProvider();if(samples.WaveFormat.Channels==1)samples=new MonoToStereoSampleProvider(samples);if(samples.WaveFormat.Channels!=2)throw new NotSupportedException("Output conversion expects mono or stereo input");if(samples.WaveFormat.SampleRate!=target.SampleRate)samples=new WdlResamplingSampleProvider(new ResamplerLeadIn(samples),target.SampleRate);
   return new EncodedOutput(samples,target,floating,valid,mask);
  }
  // WDL's output-driven interpolator requests four guard frames. Seed only those
  // frames (4/44100 s), so its first callback does not demand a second 10 ms graph block.
  sealed class ResamplerLeadIn:ISampleProvider {
   readonly ISampleProvider source;int remaining;public WaveFormat WaveFormat{get{return source.WaveFormat;}}
   public ResamplerLeadIn(ISampleProvider source){this.source=source;remaining=4*source.WaveFormat.Channels;}
   public int Read(float[] data,int offset,int count){int zero=Math.Min(remaining,count);if(zero>0){Array.Clear(data,offset,zero);remaining-=zero;}return zero+(count>zero?source.Read(data,offset+zero,count-zero):0);}
  }
  [StructLayout(LayoutKind.Explicit)]struct FloatBits {[FieldOffset(0)]public float Value;[FieldOffset(0)]public int Bits;}
  sealed class EncodedOutput:IWaveProvider {
   readonly ISampleProvider source;readonly WaveFormat target;readonly bool floating;readonly int valid,left=0,right=1;float[] input=new float[0];public WaveFormat WaveFormat{get{return target;}}
   public EncodedOutput(ISampleProvider source,WaveFormat target,bool floating,int valid,int mask){this.source=source;this.target=target;this.floating=floating;this.valid=valid;if(target.Channels>1&&mask!=0){int n=0,l=-1,r=-1;for(int bit=0;bit<32;bit++)if(((uint)mask&(1u<<bit))!=0){if(bit==0)l=n;if(bit==1)r=n;n++;}if(n!=target.Channels||l<0||r<0)throw new NotSupportedException("Unsupported speaker channel mask");left=l;right=r;}}
   public int Read(byte[] data,int offset,int count){if(data==null||offset<0||count<0||offset>data.Length-count)throw new ArgumentOutOfRangeException();int frames=count/target.BlockAlign;if(frames==0)return 0;int needed=frames*2;if(input.Length<needed)input=new float[needed];int read=source.Read(input,0,needed)/2,bytes=target.BitsPerSample/8;
    for(int f=0;f<read;f++)for(int c=0;c<target.Channels;c++){float value=target.Channels==1?(input[f*2]+input[f*2+1])*.5f:c==left?input[f*2]:c==right?input[f*2+1]:0;if(Single.IsNaN(value)||Single.IsInfinity(value))value=0;value=Math.Max(-1,Math.Min(1,value));long packed;if(floating)packed=(uint)new FloatBits{Value=value}.Bits;else{long scale=1L<<(valid-1);long sample=Math.Max(-scale,Math.Min(scale-1,(long)Math.Round(value*scale)));packed=sample<<(target.BitsPerSample-valid);if(target.BitsPerSample==8)packed+=128;}for(int b=0;b<bytes;b++)data[offset++]=(byte)(packed>>(8*b));}
    return read*target.BlockAlign;
   }
  }
 }
 // Every initialize attempt owns a fresh client. Nothing changes system default formats or output policy.
 internal sealed class PlaybackOutput:IDisposable {
  MMDevice device;WasapiOut player;public string DeviceId="",DeviceName="";public WaveFormat MixFormat,SelectedFormat;public event EventHandler<StoppedEventArgs> PlaybackStopped;
  public PlaybackOutput(string id,IWaveProvider source,int latency,Action<string> diagnostic){
   try{using(var enumerator=new MMDeviceEnumerator())using(var query=id=="default"?enumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia):enumerator.GetDevice(id)){
    DeviceId=query.ID;DeviceName=query.FriendlyName;var client=query.AudioClient;try{MixFormat=client.MixFormat;Action<string> log=text=>{if(diagnostic!=null)diagnostic("device="+DeviceName+" input="+OutputFormatAdapter.Describe(source.WaveFormat)+" mix="+OutputFormatAdapter.Describe(MixFormat)+" "+text);};
     OutputFormatAdapter.Initialize(source,MixFormat,DeviceName,f=>client.IsFormatSupported(AudioClientShareMode.Shared,f),provider=>{try{using(var fresh=new MMDeviceEnumerator())device=fresh.GetDevice(DeviceId);player=new WasapiOut(device,AudioClientShareMode.Shared,true,latency);player.Init(provider);SelectedFormat=provider.WaveFormat;}catch{Release();throw;}},log);
    }finally{client.Dispose();}}
    player.PlaybackStopped+=(s,e)=>{var handler=PlaybackStopped;if(handler!=null)handler(this,new StoppedEventArgs(e.Exception==null?null:new PlaybackOutputException(DeviceName,SelectedFormat,"播放",e.Exception)));};
   }catch(PlaybackOutputException){Release();throw;}catch(Exception ex){Release();throw new PlaybackOutputException(DeviceName==""?id:DeviceName,SelectedFormat??MixFormat,"初始化",ex);}
  }
  void Release(){try{if(player!=null)player.Dispose();}catch{}finally{player=null;if(device!=null)try{device.Dispose();}catch{}device=null;}}
  public void Play(){try{player.Play();}catch(Exception ex){throw new PlaybackOutputException(DeviceName,SelectedFormat,"播放",ex);}}
  public void Stop(){if(player!=null)player.Stop();}
  public void Dispose(){Release();}
 }
}
