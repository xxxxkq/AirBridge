using System;
using System.Collections.Generic;
using System.Linq;
namespace AirBridge.Routing {
 public sealed class TuningField {
  public string Key,Zh,En,Unit;public int Min,Max;
  public TuningField(string key,string zh,string en,string unit,int min,int max){Key=key;Zh=zh;En=en;Unit=unit;Min=min;Max=max;}
 }
 public sealed class PlaybackTuning {
  public int PhoneHardLimitMs{get;set;}
  public int CutCrossfadeMs{get;set;}
  public int JitterMeasurement{get;set;}
  public int JitterWindowMs{get;set;}
  public int JitterMultiplierPercent{get;set;}
  public int JitterPauseMs{get;set;}
  public int JitterClipMs{get;set;}
  public int LowBufferMs{get;set;}
  public int InitialBufferMs{get;set;}
  public int MinimumBufferMs{get;set;}
  public int MaximumBufferMs{get;set;}
  public int IncreaseMs{get;set;}
  public int DecreaseMs{get;set;}
  public int StableSeconds{get;set;}
  public int DrainMarginMs{get;set;}
  public int DrainFrames{get;set;}
  public int FadeMs{get;set;}
  public int OutputLatencyMs{get;set;}
  public int InputOverflowMs{get;set;}
  public int OutputOverflowMs{get;set;}
  public int CaptureRequests{get;set;}
  public int CompletedFrames{get;set;}
  public int CaptureWaitMs{get;set;}
  public PlaybackTuning(){PhoneHardLimitMs=200;CutCrossfadeMs=5;JitterMeasurement=1;JitterWindowMs=2000;JitterMultiplierPercent=200;JitterPauseMs=250;JitterClipMs=60;LowBufferMs=10;InitialBufferMs=30;MinimumBufferMs=30;MaximumBufferMs=120;IncreaseMs=10;DecreaseMs=10;StableSeconds=10;DrainMarginMs=20;DrainFrames=1;FadeMs=2;OutputLatencyMs=100;InputOverflowMs=500;OutputOverflowMs=400;CaptureRequests=8;CompletedFrames=16;CaptureWaitMs=100;}
  public static readonly TuningField[] Fields={
   new TuningField("LowBufferMs","关闭抗卡顿时的缓冲","Buffer with anti-stutter off","ms",10,200),
   new TuningField("InitialBufferMs","自适应初始缓冲","Initial adaptive buffer","ms",10,500),
   new TuningField("MinimumBufferMs","自适应最小缓冲","Minimum adaptive buffer","ms",10,500),
   new TuningField("MaximumBufferMs","自适应最大缓冲","Maximum adaptive buffer","ms",10,500),
   new TuningField("IncreaseMs","欠载增加量","Increase on underrun","ms",1,100),
   new TuningField("DecreaseMs","稳定降低量","Decrease when stable","ms",1,100),
   new TuningField("StableSeconds","稳定观察时间","Stability window","s",1,120),
   new TuningField("DrainMarginMs","积压回收余量","Drain margin","ms",10,200),
   new TuningField("DrainFrames","每块额外消化样本帧（0关闭）","Extra sample frames per block (0 disables)","frames",0,4),
   new TuningField("FadeMs","缺音淡入淡出（0关闭）","Gap fade (0 disables)","ms",0,10),
   new TuningField("PhoneHardLimitMs","手机有限截断阈值（至少为当前目标＋10ms）","Phone trim threshold (at least current target + 10ms)","ms",30,1500),
   new TuningField("CutCrossfadeMs","截断交叉淡化（0关闭）","Trim crossfade (0 disables)","ms",0,10),
   new TuningField("JitterMeasurement","实测抖动调整（1开启，0关闭）","Measured jitter adaptation (1 on, 0 off)","",0,1),
   new TuningField("JitterWindowMs","抖动统计窗口（按10ms帧数）","Jitter window (10ms frame count)","ms",200,10000),
   new TuningField("JitterMultiplierPercent","抖动安全系数（200＝2倍）","Jitter margin (200 = 2x)","%",100,400),
   new TuningField("JitterPauseMs","长间隔重置门槛","Long gap reset threshold","ms",100,2000),
   new TuningField("JitterClipMs","抖动样本上限","Jitter sample clamp","ms",20,200),
   new TuningField("OutputLatencyMs","WASAPI 请求输出延迟","Requested WASAPI output latency","ms",10,500),
   new TuningField("InputOverflowMs","其他输入积压清空阈值","Other input overflow threshold","ms",100,1500),
   new TuningField("OutputOverflowMs","输出积压清空阈值","Output overflow threshold","ms",100,1500),
   new TuningField("CaptureRequests","并行采集请求（下次手机连接）","Capture requests (next phone connection)","",2,32),
   new TuningField("CompletedFrames","完成暂存容量（下次手机连接）","Completion capacity (next phone connection)","frames",2,128),
   new TuningField("CaptureWaitMs","采集检查超时（下次手机连接）","Capture check timeout (next phone connection)","ms",20,1000)};
  public PlaybackTuning Clone(){return (PlaybackTuning)MemberwiseClone();}
  public void Validate(){foreach(var f in Fields){int v=(int)GetType().GetProperty(f.Key).GetValue(this,null);if(v<f.Min||v>f.Max)throw new ArgumentException(f.Zh+" / "+f.En+": "+f.Min+"–"+f.Max+" "+f.Unit);}
   if(JitterClipMs>=JitterPauseMs)throw new ArgumentException("抖动样本上限须小于长间隔重置门槛 / Jitter clamp must be below reset gap.");
   if(MinimumBufferMs>InitialBufferMs||InitialBufferMs>MaximumBufferMs)throw new ArgumentException("缓冲须满足：最小 ≤ 初始 ≤ 最大 / Minimum ≤ initial ≤ maximum.");
   if(OutputOverflowMs<OutputLatencyMs+20)throw new ArgumentException("输出清空阈值至少为请求输出延迟＋20ms / Output overflow must exceed latency by 20ms.");
   if(CompletedFrames<CaptureRequests)throw new ArgumentException("完成暂存容量不能小于采集请求数 / Completion capacity must cover capture requests.");
  }
 }
 public sealed partial class GraphRuntime {
  PlaybackTuning tuning=new PlaybackTuning();
  public PlaybackTuning GetPlaybackTuning(){lock(gate)return tuning.Clone();}
  public void ApplyPlaybackTuning(PlaybackTuning value){value=value.Clone();value.Validate();lock(applyGate){if(stopping)throw new ObjectDisposedException("GraphRuntime");
   var previous=tuning;Dictionary<string,Output> old=null,replacement=null;
   try{
    if(previous.OutputLatencyMs!=value.OutputLatencyMs){lock(gate)old=new Dictionary<string,Output>(outputs);replacement=new Dictionary<string,Output>();
     foreach(var pair in old){var output=new Output(pair.Key.Substring(2),this,value.OutputLatencyMs);replacement.Add(pair.Key,output);output.Open();}
     foreach(var output in old.Values)output.Pause();
     lock(gate)outputs=replacement;
     foreach(var output in replacement.Values){output.Start();if(output.Error!="")throw new InvalidOperationException(output.Error);}
    }
    lock(gate){tuning=value;foreach(var source in sources.Values)source.ApplyTuning(value,phoneBuffering);}
   }catch{if(replacement!=null)foreach(var output in replacement.Values)try{output.Pause();}catch{}if(old!=null){lock(gate)outputs=old;foreach(var output in old.Values)output.Start();}if(replacement!=null)foreach(var output in replacement.Values)output.Dispose();throw;}
   if(old!=null)foreach(var output in old.Values)output.Dispose();
  }}
 }
}
