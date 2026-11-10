using System;using System.Linq;using AirBridge.Routing;
namespace AirBridge.Desktop {
 // Preserve the desired graph separately; unavailable applications contribute no live edge.
 internal static class ApplicationRouteSelection {
  public static GraphPlan Select(GraphPlan desired,RememberedApp[] remembered,ApplicationInput[] available){
   var edges=desired.Edges.Select(e=>{if(!e.Source.StartsWith("a:"))return new GraphEdge{Source=e.Source,Target=e.Target,Gain=e.Gain};var identity=(remembered??new RememberedApp[0]).FirstOrDefault(a=>a.Id==e.Source);var match=AirBridge.Routing.ApplicationIdentity.Resolve(e.Source,identity==null?"":identity.Path,available);return match==null?null:new GraphEdge{Source=match.Id,Target=e.Target,Gain=e.Gain};}).Where(e=>e!=null).ToArray();// Remove only processing branches whose inputs disappeared; retain shared mixers.
   bool changed;do{changed=false;foreach(var module in desired.Modules.Where(m=>m.Kind!="rtpin")){
    if(!edges.Any(e=>e.Target==module.Id)&&edges.Any(e=>e.Source==module.Id)){edges=edges.Where(e=>e.Source!=module.Id).ToArray();changed=true;}
   }}while(changed);
   var modules=desired.Modules.Select(m=>{var copy=m.Copy();if((copy.SelectedSource??"").StartsWith("a:")){
    var identity=(remembered??new RememberedApp[0]).FirstOrDefault(a=>a.Id==copy.SelectedSource);
    var match=AirBridge.Routing.ApplicationIdentity.Resolve(copy.SelectedSource,identity==null?"":identity.Path,available);if(match!=null)copy.SelectedSource=match.Id;
   }return copy;}).ToArray();
   return GraphPlan.Create(edges,modules);
  }
  public static string Signature(GraphEdge[] edges){return String.Join("\n",edges.Select(e=>e.Source+"|"+e.Target).OrderBy(s=>s));}
 }
}
