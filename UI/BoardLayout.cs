using System;
using System.Collections.Generic;
using System.Linq;
namespace AirBridge.UI {
 // Layered DAG layout with stable barycentric sweeps; never edits graph connections.
 public static class BoardLayout {
  public static NodePosition[] Arrange(IList<AudioNode> nodes,IList<AudioLink> links){
   var ids=nodes.Select(n=>n.Id).Distinct().ToArray();var original=ids.Select((id,i)=>new{id,i}).ToDictionary(x=>x.id,x=>x.i);
   var edges=links.Where(e=>original.ContainsKey(e.Source)&&original.ContainsKey(e.Target)&&e.Source!=e.Target).GroupBy(e=>e.Source+"\n"+e.Target).Select(g=>g.First()).ToArray();
   var layers=ids.ToDictionary(id=>id,id=>0);var degree=ids.ToDictionary(id=>id,id=>edges.Count(e=>e.Target==id));var queue=new Queue<string>(ids.Where(id=>degree[id]==0));int visited=0;
   while(queue.Count>0){var id=queue.Dequeue();visited++;foreach(var edge in edges.Where(e=>e.Source==id)){layers[edge.Target]=Math.Max(layers[edge.Target],layers[id]+1);if(--degree[edge.Target]==0)queue.Enqueue(edge.Target);}}
   if(visited!=ids.Length){foreach(var n in nodes)layers[n.Id]=n.Module?1:n.Input?0:2;}
   int last=Math.Max(1,layers.Values.DefaultIfEmpty(0).Max());foreach(var n in nodes.Where(n=>!n.Input&&!n.Module))layers[n.Id]=last;
   var rows=ids.GroupBy(id=>layers[id]).OrderBy(g=>g.Key).ToDictionary(g=>g.Key,g=>g.ToList());
   var rank=ids.ToDictionary(id=>id,id=>0.0);Action update=()=>{foreach(var row in rows.Values)for(int i=0;i<row.Count;i++)rank[row[i]]=i;};update();
   Func<long> score=()=>{long count=0;for(int i=0;i<edges.Length;i++)for(int j=i+1;j<edges.Length;j++){var a=edges[i];var b=edges[j];if(layers[a.Source]==layers[b.Source]&&layers[a.Target]==layers[b.Target]&&(rank[a.Source]-rank[b.Source])*(rank[a.Target]-rank[b.Target])<0)count++;}return count;};
   long best=score();var bestRows=rows.ToDictionary(p=>p.Key,p=>p.Value.ToList());
   for(int sweep=0;sweep<12;sweep++){
    bool forward=sweep%2==1;foreach(int layer in (forward?rows.Keys.OrderBy(x=>x):rows.Keys.OrderByDescending(x=>x)).ToArray()){
     var sorted=rows[layer].Select(id=>{var neighbors=edges.Where(e=>forward?e.Target==id&&layers[e.Source]<layer:e.Source==id&&layers[e.Target]>layer).Select(e=>forward?e.Source:e.Target).ToArray();return new{Id=id,Weight=neighbors.Length==0?rank[id]:neighbors.Average(n=>rank[n]),Old=rank[id]};}).OrderBy(x=>x.Weight).ThenBy(x=>x.Old).Select(x=>x.Id).ToList();rows[layer]=sorted;update();
    }
    long current=score();if(current<best){best=current;bestRows=rows.ToDictionary(p=>p.Key,p=>p.Value.ToList());}
   }
   return bestRows.SelectMany(row=>row.Value.Select((id,i)=>new NodePosition{Id=id,X=row.Key*350,Y=i*110})).ToArray();
  }
 }
}
