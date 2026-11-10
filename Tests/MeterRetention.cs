using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using AirBridge.Routing;
class MeterRetention {
 static int Main(string[] args){using(var graph=new GraphRuntime()){
 var mark=typeof(GraphRuntime).GetMethod("MarkSignal",BindingFlags.Instance|BindingFlags.NonPublic);
 var field=typeof(GraphRuntime).GetField("signalAt",BindingFlags.Instance|BindingFlags.NonPublic);
 for(int i=0;i<10000;i++)mark.Invoke(graph,new object[]{"fixture:"+i,"o:fixture"});
 Thread.Sleep(800);var active=graph.ActiveEdges();int retained=((IDictionary)field.GetValue(graph)).Count;
 Console.WriteLine("Expired edges: active="+active.Length+", retained="+retained);
 if(args.Length>0&&args[0]=="baseline")return 0;
 if(active.Length!=0||retained!=0)return 1;
 mark.Invoke(graph,new object[]{"fresh","o:fixture"});if(graph.ActiveEdges().Length!=1)return 2;
 Console.WriteLine("PASS: expired activity cache released; fresh activity retained");return 0;
 }}
}
