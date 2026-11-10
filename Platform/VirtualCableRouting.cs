using System;
using System.Linq;
using AirBridge.Platform;
namespace AirBridge.Routing {
 public sealed class VirtualCableEndpoints {public string Playback,Capture;}
 // Optional adapter: no installer, driver files, persistent device IDs or fallback to mixed loopback.
 public static class VirtualCableRouting {
  public static VirtualCableEndpoints Detect(Endpoint[] playback,Endpoint[] capture){
   var p=playback.Where(e=>e.Active&&e.Name=="CABLE Input (VB-Audio Virtual Cable)").ToArray();
   var c=capture.Where(e=>e.Active&&e.Name=="CABLE Output (VB-Audio Virtual Cable)").ToArray();
   if(p.Length!=1||c.Length!=1)throw new InvalidOperationException("未找到可用的 VB-CABLE，请先安装并重启电脑。 / VB-CABLE is unavailable.");
   if(!String.IsNullOrEmpty(p[0].ContainerId)&&!String.IsNullOrEmpty(c[0].ContainerId)&&!String.Equals(p[0].ContainerId,c[0].ContainerId,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("VB-CABLE 输入输出不属于同一设备。 / VB-CABLE endpoints do not match.");
   return new VirtualCableEndpoints{Playback=p[0].Id,Capture=c[0].Id};
  }
  public static bool IsCablePlayback(Endpoint e){return e.Name=="CABLE Input (VB-Audio Virtual Cable)"||e.Name=="CABLE In 16ch (VB-Audio Virtual Cable)";}
  public static string Owner(GraphPlan plan){
   bool pc=plan.Edges.Any(e=>e.Source=="pc:system");
   if(plan.VirtualApplications.Length>1||(pc&&plan.RequiresVirtualApp))throw new InvalidOperationException("当前只有一条 VB-CABLE：电脑整体取流与应用效果链不能共用，也不能同时隔离多个应用。请只保留一个虚拟输入。");
   return plan.RequiresVirtualApp?plan.VirtualApplications[0]:pc?"pc:system":"";
  }
  public static GraphPlan Prepare(GraphPlan original,VirtualCableEndpoints cable,Endpoint[] playback,string physicalDefault){
   string owner=Owner(original);if(owner=="")return original;
   var device=playback.SingleOrDefault(e=>e.Id==physicalDefault&&e.Active);
   if(device==null||IsCablePlayback(device))throw new InvalidOperationException("请先选择实际耳机或扬声器作为系统默认输出。 / Select a physical default output first.");
   if(original.Edges.Any(e=>e.Source=="c:"+cable.Capture))throw new InvalidOperationException("当前虚拟输入已接管 VB-CABLE，请勿重复添加其录音输入。 / Duplicate VB-CABLE input.");
   var forbidden=playback.Where(IsCablePlayback).Select(e=>"o:"+e.Id).ToArray();
   if(original.Edges.Any(e=>forbidden.Contains(e.Target)))throw new InvalidOperationException("不能将声音送回 VB-CABLE，会形成回路。 / Cannot route audio back into VB-CABLE.");
   if(original.Outputs.Contains("o:default")&&original.Outputs.Contains("o:"+physicalDefault))throw new InvalidOperationException("系统默认播放器与同一实际设备不能重复输出。 / Duplicate physical output.");
   var edges=original.Edges.Select(e=>new GraphEdge{Source=e.Source==owner?"c:"+cable.Capture:e.Source,Target=e.Target=="o:default"?"o:"+physicalDefault:e.Target,Gain=e.Gain}).ToList();
   var modules=original.Modules.Select(m=>{var copy=m.Copy();if(copy.SelectedSource==owner)copy.SelectedSource="c:"+cable.Capture;return copy;}).ToArray();
   // Existing write-ahead default routing handles activation, rollback and restore.
   edges.Add(new GraphEdge{Source=owner,Target="o:"+cable.Playback});
   return GraphPlan.Create(edges.ToArray(),modules);
  }
 }
}
