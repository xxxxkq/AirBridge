using System;
using System.Threading;
using System.Threading.Tasks;
namespace AirBridge.Desktop {
 // A stalled native call occupies one worker, never queues an unbounded backlog.
 internal sealed class BackgroundQuery<T> {
  int busy;
  public Task<T> TryRun(Func<Task<T>> read){
   if(Interlocked.CompareExchange(ref busy,1,0)!=0)return null;
   return Task.Run(async()=>{try{return await read().ConfigureAwait(false);}finally{Interlocked.Exchange(ref busy,0);}});
  }
 }
}
