using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
namespace AirBridge.Routing {
 // RTP v2 dynamic payload: L16/44100/2, network byte order. No proprietary audio header.
 public sealed class RtpSender:IDisposable {
  readonly UdpClient socket;readonly int payload;readonly uint ssrc;ushort sequence;uint timestamp;readonly List<byte> pending=new List<byte>();
  public long Packets;readonly BlockingCollection<byte[]> outgoing=new BlockingCollection<byte[]>(256);readonly Task worker;volatile bool stopping;volatile string error;
  public RtpSender(string host,int port,int payload){this.payload=payload;ssrc=(uint)new Random().Next();IPAddress address;if(!IPAddress.TryParse(host,out address))throw new ArgumentException("RTP 目标请填写 IP 地址。");socket=new UdpClient(address.AddressFamily);socket.Client.SendTimeout=100;socket.Connect(address,port);worker=Task.Run(()=>{try{foreach(var packet in outgoing.GetConsumingEnumerable()){if(stopping)break;socket.Send(packet,packet.Length);Interlocked.Increment(ref Packets);}}catch(Exception ex){if(!stopping)error=ex.Message;}});}
  public void Add(byte[] pcm,int count){if(error!=null)throw new InvalidOperationException(error);for(int i=0;i<count;i++)pending.Add(pcm[i]);while(pending.Count>=880){byte[] packet=new byte[892];packet[0]=0x80;packet[1]=(byte)payload;packet[2]=(byte)(sequence>>8);packet[3]=(byte)sequence++;Put(packet,4,timestamp);timestamp+=220;Put(packet,8,ssrc);for(int i=0;i<880;i+=2){packet[12+i]=pending[i+1];packet[13+i]=pending[i];}pending.RemoveRange(0,880);if(!outgoing.TryAdd(packet))throw new InvalidOperationException("网络发送队列已满，已停止此网络输出。");}}
  static void Put(byte[] b,int i,uint n){b[i]=(byte)(n>>24);b[i+1]=(byte)(n>>16);b[i+2]=(byte)(n>>8);b[i+3]=(byte)n;}
  public void Dispose(){stopping=true;outgoing.CompleteAdding();socket.Close();worker.GetAwaiter().GetResult();outgoing.Dispose();}
 }
 public sealed class RtpReceiver:IDisposable {
  readonly UdpClient socket;readonly Action<byte[],int> receive;readonly Action<Exception> fail;readonly int payload;Task worker;volatile bool stopping;readonly SortedDictionary<long,byte[]> queue=new SortedDictionary<long,byte[]>();long expected=-1;int packetBytes=880;bool primed;uint? sender;DateTime last=DateTime.MinValue;
  public long Packets,Lost,Rejected;
  public RtpReceiver(string host,int port,int payload,Action<byte[],int> receive,Action<Exception> fail){IPAddress address;if(!IPAddress.TryParse(host,out address))throw new ArgumentException("接收地址必须是本机 IP，或 0.0.0.0。");this.payload=payload;this.receive=receive;this.fail=fail;socket=new UdpClient(new IPEndPoint(address,port));socket.Client.ReceiveTimeout=200;worker=Task.Run((Action)Run);}
  public static byte[] Decode(byte[] packet,int payload,out ushort sequence,out uint ssrc){sequence=0;ssrc=0;if(packet.Length<12||(packet[0]>>6)!=2||(packet[1]&127)!=payload)return null;int start=12+(packet[0]&15)*4;if(start>packet.Length)return null;if((packet[0]&16)!=0){if(start+4>packet.Length)return null;int words=(packet[start+2]<<8)|packet[start+3];start+=4+words*4;}int end=packet.Length;if((packet[0]&32)!=0){int padding=packet[end-1];if(padding==0)return null;end-=padding;}int length=end-start;if(length<=0||length>8192||length%4!=0)return null;sequence=(ushort)((packet[2]<<8)|packet[3]);ssrc=((uint)packet[8]<<24)|((uint)packet[9]<<16)|((uint)packet[10]<<8)|packet[11];var pcm=new byte[length];for(int i=0;i<length;i+=2){pcm[i]=packet[start+i+1];pcm[i+1]=packet[start+i];}return pcm;}
  void Run(){try{while(!stopping){byte[] packet;try{IPEndPoint remote=null;packet=socket.Receive(ref remote);}catch(SocketException ex){if(ex.SocketErrorCode==SocketError.TimedOut)continue;if(stopping)break;throw;}
   ushort seq;uint ssrc;var data=Decode(packet,payload,out seq,out ssrc);if(data==null){Rejected++;continue;}
   if(DateTime.UtcNow-last>TimeSpan.FromSeconds(3)){queue.Clear();expected=-1;sender=null;primed=false;}last=DateTime.UtcNow;
   if(sender.HasValue&&sender.Value!=ssrc){Rejected++;continue;}sender=ssrc;if(expected<0)expected=seq;
   long extended=expected+(short)(seq-(ushort)expected);if(extended<expected||extended>expected+1024){Rejected++;continue;}if(!queue.ContainsKey(extended))queue.Add(extended,data);Packets++;
   if(!primed&&queue.Count<12)continue;primed=true;
   for(int n=0;n<64;n++){byte[] next;if(queue.TryGetValue(expected,out next)){queue.Remove(expected++);receive(next,next.Length);packetBytes=next.Length;}else if(queue.Count>=12){Lost++;expected++;receive(new byte[packetBytes],packetBytes);}else break;}
  }}catch(Exception ex){if(!stopping)fail(ex);}}
  public void Dispose(){stopping=true;socket.Close();if(worker!=null)worker.GetAwaiter().GetResult();}
 }
}
