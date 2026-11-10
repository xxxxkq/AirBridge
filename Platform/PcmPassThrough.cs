using System;
using System.Linq;
using NAudio.Wave;
namespace AirBridge.Routing {
 // Unity routes stay bit-for-bit PCM16, as in the original successful relay.
 public sealed class PcmPassThrough:IWaveProvider {
  readonly IWaveProvider raw,processed;readonly Func<ModuleSpec[]> modules;readonly Func<float> inputGain;readonly float gain;
  public WaveFormat WaveFormat{get{return raw.WaveFormat;}}
  public PcmPassThrough(IWaveProvider raw,Func<ModuleSpec[]> modules,Func<float> inputGain,float gain){
   if(raw.WaveFormat.Encoding!=WaveFormatEncoding.Pcm||raw.WaveFormat.BitsPerSample!=16)throw new ArgumentException("PCM16 required");
   this.raw=raw;this.modules=modules;this.inputGain=inputGain;this.gain=gain;
   processed=new EffectsProvider(raw.ToSampleProvider(),modules,inputGain,gain).ToWaveProvider16();
  }
  public int Read(byte[] data,int offset,int count){bool unchanged=gain==1&&inputGain()==1&&modules().All(m=>!m.Enabled||(m.Kind=="equalizer"&&m.Preset=="flat")||(m.Kind=="volume"&&m.Gain==1));return (unchanged?raw:processed).Read(data,offset,count);}
 }
}
