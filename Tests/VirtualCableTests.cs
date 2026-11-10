using System;using System.Linq;using AirBridge.Routing;using AirBridge.Platform;
class VirtualCableTests {
 static void Check(bool b,string m){if(!b)throw new Exception(m);}
 static void Reject(Action a,string m){try{a();}catch(InvalidOperationException){return;}throw new Exception(m);}
 static void Main(){var p=new[]{new Endpoint{Id="cable",Name="CABLE Input (VB-Audio Virtual Cable)",Active=true,ContainerId="same"},new Endpoint{Id="sixteen",Name="CABLE In 16ch (VB-Audio Virtual Cable)",Active=true},new Endpoint{Id="headset",Name="Headset",Active=true},new Endpoint{Id="speaker",Name="Speaker",Active=true}};var c=new[]{new Endpoint{Id="capture",Name="CABLE Output (VB-Audio Virtual Cable)",Active=true,ContainerId="same"}};
 var cable=VirtualCableRouting.Detect(p,c);Reject(()=>VirtualCableRouting.Detect(new Endpoint[0],c),"Missing driver accepted");Reject(()=>VirtualCableRouting.Detect(p,new[]{new Endpoint{Id="wrong",Name=c[0].Name,Active=true,ContainerId="different"}}),"Mismatched pair accepted");
 var original=GraphPlan.Create(new[]{new GraphEdge{Source="pc:system",Target="m:volume"},new GraphEdge{Source="m:volume",Target="o:default"},new GraphEdge{Source="m:volume",Target="o:speaker"},new GraphEdge{Source="p:phone",Target="o:default"},new GraphEdge{Source="a:123:456",Target="o:default"}},new[]{new ModuleSpec{Id="m:volume",Kind="volume",Gain=.5f}});
 bool disabled=false;try{GraphRuntime.ValidateLivePlan(original);}catch(NotSupportedException){disabled=true;}Check(disabled,"Disabled extension enabled multi-output");GraphRuntime.ValidateLivePlan(original,true);
 var mapped=VirtualCableRouting.Prepare(original,cable,p,"headset");Check(mapped.Edges.Count(e=>e.Source=="pc:system")==1&&mapped.Edges.Any(e=>e.Source=="pc:system"&&e.Target=="o:cable"),"Missing native redirection");Check(mapped.Edges.Any(e=>e.Source=="c:capture"&&e.Target=="m:volume"),"Capture did not enter effect chain");Check(mapped.Edges.Any(e=>e.Source=="p:phone"&&e.Target=="o:headset"),"Phone feedback risk");Check(mapped.NativeAppEdges.Single().Target=="o:headset","Native app default feedback risk");Check(original.Edges.Any(e=>e.Source=="pc:system"&&e.Target=="m:volume")&&original.Edges.Count(e=>e.Target=="o:default")==3,"Saved graph mutated");
 foreach(string bad in new[]{"o:cable","o:sixteen"})Reject(()=>VirtualCableRouting.Prepare(GraphPlan.Create(new[]{new GraphEdge{Source="pc:system",Target=bad}},new ModuleSpec[0]),cable,p,"headset"),"Feedback accepted");
 Reject(()=>VirtualCableRouting.Prepare(original,cable,p,"cable"),"Virtual original default accepted");
 Reject(()=>VirtualCableRouting.Prepare(GraphPlan.Create(new[]{new GraphEdge{Source="pc:system",Target="o:default"},new GraphEdge{Source="pc:system",Target="o:headset"}},new ModuleSpec[0]),cable,p,"headset"),"Duplicated physical output accepted");
 Reject(()=>VirtualCableRouting.Prepare(GraphPlan.Create(new[]{new GraphEdge{Source="pc:system",Target="o:default"},new GraphEdge{Source="c:capture",Target="o:speaker"}},new ModuleSpec[0]),cable,p,"headset"),"Duplicate capture accepted");

 var appPlan=GraphPlan.Create(new[]{new GraphEdge{Source="a:123:456",Target="m:eq"},new GraphEdge{Source="m:eq",Target="o:default"},new GraphEdge{Source="m:eq",Target="o:speaker"},new GraphEdge{Source="p:phone",Target="o:default"},new GraphEdge{Source="a:789:123",Target="o:speaker"}},new[]{new ModuleSpec{Id="m:eq",Kind="equalizer"}});
 bool appDisabled=false;try{GraphRuntime.ValidateLivePlan(appPlan);}catch(NotSupportedException){appDisabled=true;}Check(appDisabled,"App effects enabled without extension");
 var appMapped=VirtualCableRouting.Prepare(appPlan,cable,p,"headset");
 Check(!appMapped.Edges.Any(e=>e.Source=="pc:system"),"App mode changed system default");
 Check(appMapped.Edges.Any(e=>e.Source=="c:capture"&&e.Target=="m:eq"),"App bypassed effects");
 Check(appMapped.NativeAppEdges.Single(e=>e.Source=="a:123:456").Target=="o:cable","App not isolated to cable");
 Check(appMapped.NativeAppEdges.Single(e=>e.Source=="a:789:123").Target=="o:speaker","Other native app changed");
 Check(appMapped.Outputs.Contains("o:speaker")&&appMapped.Outputs.Contains("o:headset"),"Missing app outputs");
 Reject(()=>VirtualCableRouting.Owner(GraphPlan.Create(appPlan.Edges.Concat(new[]{new GraphEdge{Source="pc:system",Target="o:default"}}).ToArray(),appPlan.Modules)),"PC and app share one cable");
 Reject(()=>VirtualCableRouting.Owner(GraphPlan.Create(new[]{new GraphEdge{Source="a:1:1",Target="o:headset",Gain=.5f},new GraphEdge{Source="a:2:2",Target="o:speaker",Gain=.5f}},new ModuleSpec[0])),"Multiple isolated apps share one cable");
 Check(!appPlan.Edges.Any(e=>e.Source=="c:capture"),"Original application graph mutated");
 Console.WriteLine("PASS optional gate, exact endpoints, effect fanout, phone/app isolation, immutable draft, feedback and duplicate guards");
 }
}
