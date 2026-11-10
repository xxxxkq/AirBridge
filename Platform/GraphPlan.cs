using System;
using System.Collections.Generic;
using System.Linq;
namespace AirBridge.Routing {
 public sealed class GraphPlan {
  public bool RequiresVirtualPc;public string[] VirtualApplications;public bool RequiresVirtualApp{get{return VirtualApplications.Length>0;}}
  public void RequireLiveCapabilities(){if(RequiresVirtualPc||RequiresVirtualApp)throw new NotSupportedException("电脑或应用的多输出、效果链需要启用通用设置中的 VB-CABLE 可选扩展。当前连线未改变。");}
  public GraphEdge[] NativeAppEdges;public GraphEdge[] Edges;public ModuleSpec[] Modules;public string[] Order,Sources,Outputs,PhoneIds;
  public static bool IsSource(string id){return id=="pc:system"||id.StartsWith("p:")||id.StartsWith("c:")||id.StartsWith("a:")||id.StartsWith("test:");}
  public static GraphPlan Create(GraphEdge[] edges,ModuleSpec[] modules){
   if(edges==null||modules==null||edges.Length>96||modules.Length>32)throw new ArgumentException("最多 96 条连线、32 个模块。");
   var map=new Dictionary<string,ModuleSpec>();foreach(var m in modules){GraphCompiler.ValidateModule(m);if(map.ContainsKey(m.Id))throw new ArgumentException("重复模块 ID");map.Add(m.Id,m);}
   var nodes=new HashSet<string>();var keys=new HashSet<string>();foreach(var e in edges){if(e==null||String.IsNullOrEmpty(e.Source)||String.IsNullOrEmpty(e.Target)||!keys.Add(e.Source+"|"+e.Target))throw new ArgumentException("无效或重复连线。");GraphCompiler.CheckGain(e.Gain);
    if(!(IsSource(e.Source)||map.ContainsKey(e.Source))||!(e.Target.StartsWith("o:")||map.ContainsKey(e.Target)))throw new ArgumentException("无效的输入或输出节点。");
    if(map.ContainsKey(e.Source)&&map[e.Source].Kind=="rtpout")throw new ArgumentException("网络输出是终点。");if(map.ContainsKey(e.Target)&&map[e.Target].Kind=="rtpin")throw new ArgumentException("网络输入不能接收上游连线。");nodes.Add(e.Source);nodes.Add(e.Target);
   }
   foreach(var m in modules){int count=edges.Count(e=>e.Target==m.Id);if(count>1&&m.Kind!="mixer")throw new ArgumentException("多路输入请先连接到混音 / 切换组件。");if(nodes.Contains(m.Id)&&m.Kind!="rtpin"&&count==0)throw new ArgumentException("模块缺少输入。");if(nodes.Contains(m.Id)&&m.Kind!="rtpout"&&m.Kind!="record"&&!edges.Any(e=>e.Source==m.Id))throw new ArgumentException("模块尚未接到输出。");}
   foreach(var rx in modules.Where(m=>m.Kind=="rtpin"&&m.Enabled&&nodes.Contains(m.Id)))foreach(var tx in modules.Where(m=>m.Kind=="rtpout"&&m.Enabled&&nodes.Contains(m.Id))){System.Net.IPAddress address;if(rx.Port==tx.Port&&System.Net.IPAddress.TryParse(tx.Host,out address)&&(System.Net.IPAddress.IsLoopback(address)||tx.Host==rx.Host)&&Reachable(rx.Id,tx.Id,edges,new HashSet<string>()))throw new ArgumentException("网络输入又发送回了本机同一端口，会形成音频回路。请修改目标端口或连线。");}
   var order=new List<string>();var path=new HashSet<string>();var done=new HashSet<string>();foreach(var n in nodes)Visit(n,edges,path,done,order);
   var sources=order.Where(n=>IsSource(n)||(map.ContainsKey(n)&&map[n].Kind=="rtpin")).ToArray();
   var pcEdges=edges.Where(e=>e.Source=="pc:system").ToArray();bool virtualPc=pcEdges.Length>1||pcEdges.Any(e=>!e.Target.StartsWith("o:")||e.Gain!=1);
   var appEdges=edges.Where(e=>e.Source.StartsWith("a:")).ToArray();
   var virtualApps=appEdges.GroupBy(e=>e.Source).Where(g=>g.Count()!=1||!g.First().Target.StartsWith("o:")||g.First().Gain!=1).Select(g=>g.Key).ToArray();

   return new GraphPlan{RequiresVirtualPc=virtualPc,VirtualApplications=virtualApps,NativeAppEdges=appEdges,Edges=edges,Modules=modules,Order=order.ToArray(),Sources=sources,Outputs=order.Where(n=>n.StartsWith("o:")).ToArray(),PhoneIds=sources.Where(n=>n.StartsWith("p:")).Select(n=>n.Substring(2)).ToArray()};
  }
  static bool Reachable(string source,string target,GraphEdge[] edges,HashSet<string> seen){if(source==target)return true;if(!seen.Add(source))return false;return edges.Where(e=>e.Source==source).Any(e=>Reachable(e.Target,target,edges,seen));}
  static void Visit(string id,GraphEdge[] edges,HashSet<string> path,HashSet<string> done,List<string> order){if(done.Contains(id))return;if(!path.Add(id))throw new ArgumentException("连线形成回路。");foreach(var e in edges.Where(e=>e.Target==id))Visit(e.Source,edges,path,done,order);path.Remove(id);done.Add(id);order.Add(id);}
 }
}
