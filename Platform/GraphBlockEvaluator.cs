using System;
using System.Collections.Generic;
using System.Linq;
namespace AirBridge.Routing {
 // Same graph execution for live render and offline virtual-card tests. Sources are read once per block.
 public static class GraphBlockEvaluator {
  public static HashSet<string> Run(GraphPlan plan,Func<string,float[]> read,Func<string,float[],ModuleSpec,float[]> process,Action<string,float[]> output,Action<GraphEdge,float[],float> meter=null){
   var data=new Dictionary<string,float[]>();
   foreach(var id in plan.Order){
    var source=read(id);if(source!=null){if(source.Length!=882)throw new ArgumentException("Expected one 441-frame stereo block.");data[id]=source;continue;}
    if(GraphPlan.IsSource(id)||plan.Modules.Any(m=>m.Id==id&&m.Kind=="rtpin")){data[id]=new float[882];continue;}
    var incoming=plan.Edges.Where(e=>e.Target==id).OrderBy(e=>e.Source,StringComparer.Ordinal).ToArray();var spec=plan.Modules.FirstOrDefault(m=>m.Id==id);
    string mode=spec!=null&&spec.Kind=="mixer"&&spec.Enabled?spec.Mode:"mix";
    int selected=spec==null?-1:String.IsNullOrEmpty(spec.SelectedSource)?0:Array.FindIndex(incoming,e=>e.Source==spec.SelectedSource);
    if(meter!=null)for(int i=0;i<incoming.Length;i++)meter(incoming[i],data[incoming[i].Source],mode=="select"&&i!=selected?0:incoming[i].Gain);
    var block=BlockProcessor.Mix(incoming.Select(e=>data[e.Source]).ToArray(),incoming.Select(e=>e.Gain).ToArray(),mode,selected);
    block=process(id,block,spec);data[id]=block;if(id.StartsWith("o:"))output(id,block);
   }
   return new HashSet<string>(data.Keys);
  }
 }
}
