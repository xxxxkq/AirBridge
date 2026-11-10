using System;
using System.IO;

namespace AirBridge.Routing {
 // One reusable packet per worker, protected by that worker's outputGate.
 // Keep the existing little-endian pipe protocol exactly unchanged.
 public sealed class PhonePcmPacket {
  readonly byte[] packet=new byte[1820];
  static void Int64(byte[] bytes,int offset,long value){ulong bits=unchecked((ulong)value);for(int i=0;i<8;i++)bytes[offset+i]=(byte)(bits>>(i*8));}
  public void Write(Stream stream,byte[] pcm,int count,long frames,long signal,long peak,long shortFrames,long tailFrames,long clipped){
   if(pcm==null||count!=1764||pcm.Length<count)throw new ArgumentException("Expected one 10 ms PCM frame");
   packet[0]=2;packet[4]=0xE4;packet[5]=6;
   Int64(packet,8,frames);Int64(packet,16,signal);Int64(packet,24,peak);
   Int64(packet,32,shortFrames);Int64(packet,40,tailFrames);Int64(packet,48,clipped);
   Buffer.BlockCopy(pcm,0,packet,56,count);
   stream.Write(packet,0,packet.Length);
  }
 }
}
