using System;
using System.Threading.Tasks;
namespace AirBridge.Platform {
 // Completion represents ownership release, not just completion of Start/Open.
 public static class PhoneRelease {
  public static Task Begin(IDisposable connection, Task operation) {
   Exception closeError=null;
   try { if(connection!=null)connection.Dispose(); } catch(Exception ex){closeError=ex;}
   return Finish(connection,operation,closeError);
  }
  static async Task Finish(IDisposable connection,Task operation,Exception closeError){
   if(operation!=null){
    try{await operation.ConfigureAwait(false);}catch{/* Cancellation/failure is expected after Close. */}
    // A late Open completion must not outlive the release barrier.
    try{if(connection!=null)connection.Dispose();}catch(ObjectDisposedException){}catch(Exception ex){closeError=ex;}
   }
   if(closeError!=null)throw new InvalidOperationException("Phone connection release failed; replacement was not opened.",closeError);
  }
 }
}
