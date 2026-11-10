using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace AirBridge.Routing {
 // Bounded, non-blocking submission. A slow/broken log sink must not stall PCM delivery.
 public sealed class AsyncAudioLog : IDisposable {
  readonly BlockingCollection<Action> queue;readonly Thread writer;
  long accepted,completed,dropped,errors;int closed;
  public long Dropped{get{return Interlocked.Read(ref dropped);}}
  public long Errors{get{return Interlocked.Read(ref errors);}}
  public AsyncAudioLog(int capacity){queue=new BlockingCollection<Action>(capacity);writer=new Thread(Write){IsBackground=true,Name="AirBridge diagnostics"};writer.Start();}
  public bool Post(Action action){if(action==null)throw new ArgumentNullException("action");try{if(Volatile.Read(ref closed)==0&&queue.TryAdd(action)){Interlocked.Increment(ref accepted);return true;}}catch(InvalidOperationException){}Interlocked.Increment(ref dropped);return false;}
  void Write(){foreach(var action in queue.GetConsumingEnumerable()){try{action();}catch{Interlocked.Increment(ref errors);}finally{Interlocked.Increment(ref completed);}}}
  // Only management/test callers may flush; audio producers never wait for logging.
  public bool Flush(int milliseconds){long target=Interlocked.Read(ref accepted);var clock=Stopwatch.StartNew();while(Interlocked.Read(ref completed)<target){if(clock.ElapsedMilliseconds>=milliseconds)return false;Thread.Sleep(1);}return true;}
  public void Dispose(){if(Interlocked.Exchange(ref closed,1)!=0)return;queue.CompleteAdding();writer.Join(250);}
 }

 public sealed class AudioReadMetrics {
  public long QueueTrimCount,QueueTrimmedBytes;public void Trim(int bytes){if(bytes<=0)return;QueueTrimCount++;QueueTrimmedBytes+=bytes;QueueDroppedBytes+=bytes;}
  public long MissingBytes,MissingBlocks,MissingEvents,StartupSilenceBytes,QueueClearCount,QueueDroppedBytes,QueueOverflowCount;
  bool started,missing;
  public void Read(int missingBytes,int blockBytes){if(missingBytes>0){if(!started)StartupSilenceBytes+=missingBytes;else{MissingBytes+=missingBytes;MissingBlocks++;if(!missing)MissingEvents++;}missing=started;}else missing=false;if(missingBytes<blockBytes)started=true;}
  public void Clear(int bytes,bool overflow){if(bytes<=0)return;QueueClearCount++;QueueDroppedBytes+=bytes;if(overflow)QueueOverflowCount++;}
  public double MissingMilliseconds{get{return MissingBytes/176.4;}}
 }
}
