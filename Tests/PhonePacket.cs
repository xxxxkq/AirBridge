using System;
using System.IO;
using System.Linq;
using AirBridge.Routing;
class PhonePacketTests {
 sealed class Sink:MemoryStream{public int Writes;public override void Write(byte[] b,int o,int n){Writes++;base.Write(b,o,n);}public override void WriteByte(byte b){Writes++;base.WriteByte(b);}}
 static void Main(){var packet=new PhonePcmPacket();var random=new Random(42);for(int n=0;n<1000;n++){
  var pcm=new byte[1764];random.NextBytes(pcm);long frame=Int64.MaxValue-n,signal=(long)n*882,peak=n,shorts=n/2,tails=n/3,clipped=n/4;
  using(var old=new Sink())using(var updated=new Sink()){
   var w=new BinaryWriter(old);w.Write(2);w.Write(1764);w.Write(frame);w.Write(signal);w.Write(peak);w.Write(shorts);w.Write(tails);w.Write(clipped);w.Write(pcm);w.Flush();
   packet.Write(updated,pcm,1764,frame,signal,peak,shorts,tails,clipped);
   if(!old.ToArray().SequenceEqual(updated.ToArray())||updated.Writes!=1||old.Writes!=9)throw new Exception("Wire protocol/write count mismatch");
   Array.Clear(pcm,0,pcm.Length);if(updated.Length!=1820)throw new Exception("Size mismatch");
  }
 }Console.WriteLine("PASS 1000 reused packets byte-identical; stream writes 9 -> 1 per frame; no hardware opened");}
}
