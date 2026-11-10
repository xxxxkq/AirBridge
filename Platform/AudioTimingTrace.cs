using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace AirBridge.Routing {
 // Opt-in only. Original QPC/native thread ID are captured on the producer;
 // formatting and disk I/O happen on a bounded background consumer. No audio content.
 public static class AudioTimingTrace {
  struct Entry {public long Begin,End,A,B,C;public uint Thread;public string Kind,Key;}
  sealed class Sink {
   internal readonly BlockingCollection<Entry> Queue=new BlockingCollection<Entry>(8192);
   internal long Dropped,Errors;internal int Closed;internal readonly Thread Writer;
   internal Sink(string directory){
    Directory.CreateDirectory(directory);
    string path=Path.Combine(directory,"timing-"+Process.GetCurrentProcess().Id+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+".csv");
    var file=new StreamWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read),System.Text.Encoding.UTF8,65536);
    file.WriteLine("# frequency="+Stopwatch.Frequency+",pid="+Process.GetCurrentProcess().Id+",utc="+DateTime.UtcNow.ToString("o"));
    file.WriteLine("begin_qpc,end_qpc,thread,kind,key,a,b,c");file.Flush();
    Writer=new Thread(()=>{try{int count=0;var lifetime=Stopwatch.StartNew();long checkedAt=-1000;while(!Queue.IsCompleted){
     if(lifetime.ElapsedMilliseconds-checkedAt>=1000){checkedAt=lifetime.ElapsedMilliseconds;if(lifetime.Elapsed.TotalMinutes>=10||File.Exists(Path.Combine(directory,"trace-stop"))){if(Interlocked.Exchange(ref Closed,1)==0)Queue.CompleteAdding();}}
     Entry e;if(!Queue.TryTake(out e,250))continue;
     file.WriteLine(e.Begin+","+e.End+","+e.Thread+","+Csv(e.Kind)+","+Csv(e.Key)+","+e.A+","+e.B+","+e.C);
     if(++count%256==0)file.Flush();
    }}catch{Interlocked.Increment(ref Errors);}finally{try{file.WriteLine("# dropped="+Dropped+",errors="+Errors);file.Dispose();}catch{Interlocked.Increment(ref Errors);}}}){IsBackground=true,Name="AirBridge timing writer"};Writer.Start();
   }
  }
  static readonly Sink sink=Create();static long startupErrors;
  static Sink Create(){string path=Environment.GetEnvironmentVariable("AIRBRIDGE_TIMING_DIRECTORY");if(String.IsNullOrWhiteSpace(path))return null;try{return new Sink(path);}catch{startupErrors=1;return null;}}
  static string Csv(string s){return "\""+(s??"").Replace("\"","\"\"")+"\"";}
  [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
  public static bool Enabled{get{return sink!=null&&Volatile.Read(ref sink.Closed)==0;}}
  public static long Dropped{get{return sink==null?0:Interlocked.Read(ref sink.Dropped);}}
  public static long Errors{get{return startupErrors+(sink==null?0:Interlocked.Read(ref sink.Errors));}}
  public static long Now(){return !Enabled?0:Stopwatch.GetTimestamp();}
  public static void Record(string kind,string key,long begin,long a=0,long b=0,long c=0){
   if(sink==null||Volatile.Read(ref sink.Closed)!=0)return;
   var entry=new Entry{Begin=begin,End=Stopwatch.GetTimestamp(),Thread=GetCurrentThreadId(),Kind=kind,Key=key,A=a,B=b,C=c};
   try{if(!sink.Queue.TryAdd(entry))Interlocked.Increment(ref sink.Dropped);}catch(InvalidOperationException){Interlocked.Increment(ref sink.Dropped);}
  }
  // Management/exit only; never called from capture or render callbacks.
  public static void Close(){if(sink==null||Interlocked.Exchange(ref sink.Closed,1)!=0)return;sink.Queue.CompleteAdding();if(!sink.Writer.Join(2000))Interlocked.Increment(ref sink.Errors);}
 }
}
