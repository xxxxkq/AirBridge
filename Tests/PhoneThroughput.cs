using System;using System.Diagnostics;using System.Threading;using AirBridge.Routing;
class Throughput {static int Main(){using(var a=new PhoneWorkerClient("throughput","diagnostic-pcm",null,null)){a.Start();long first=a.Frames;var watch=Stopwatch.StartNew();Thread.Sleep(5000);Console.WriteLine("frames="+(a.Frames-first)+" seconds="+watch.Elapsed.TotalSeconds+" error="+a.Error);}return 0;}}
