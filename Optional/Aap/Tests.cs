using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using AirBridge.Aap;
class AapTests {
 sealed class Fake:IChannel {
  public readonly Queue<byte[]> Inbox=new Queue<byte[]>();public readonly List<byte[]> Sent=new List<byte[]>();public int Opens,Closes;public bool Disposed,FailSend,FailReceive;
  public void Connect(ulong address){Opens++;}public void Disconnect(){Closes++;}
  public void Send(byte[] packet){if(FailSend)throw new IOException("simulated send failure");Sent.Add(packet);}
  public byte[] Receive(int timeout){if(FailReceive)throw new IOException("simulated removal");if(Inbox.Count>0)return Inbox.Dequeue();Thread.Sleep(Math.Min(timeout,5));return null;}
  public void Dispose(){Disposed=true;}
 }
 static int checks;
 static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
 static void Throws<T>(Action a) where T:Exception{try{a();}catch(T){checks++;return;}throw new Exception("Expected "+typeof(T).Name);}
 static byte[] Bytes(string hex){var parts=hex.Split(' ');var b=new byte[parts.Length];for(int i=0;i<b.Length;i++)b[i]=Convert.ToByte(parts[i],16);return b;}
 static void Main(string[] args){
  if(args.Length==1&&args[0]=="--list"){var items=WindowsChannel.Enumerate();Console.WriteLine("Read-only AAP interfaces: "+items.Length);foreach(var p in items)Console.WriteLine(p);return;}
  if(args.Length!=0)throw new ArgumentException("Only offline tests or --list are available. Hardware connection CLI intentionally absent.");
  Check(BitConverter.ToString(WindowsChannel.ConnectPayload(0x112233445566))=="66-55-44-33-22-11-00-00-01-10","Packed 10-byte ABI");Throws<ArgumentOutOfRangeException>(()=>WindowsChannel.ConnectPayload(0));Throws<ArgumentOutOfRangeException>(()=>WindowsChannel.ConnectPayload(0x1000000000000));
  Check(BitConverter.ToString(Protocol.SetNoise(NoiseMode.Transparency))=="04-00-04-00-09-00-0D-03-00-00-00","Noise wire fixture");
  var battery=Protocol.Parse(Bytes("04 00 04 00 04 00 03 02 01 64 02 01 04 01 63 01 01 08 01 11 02 01"));Check(battery.Battery[2]==100&&battery.Battery[4]==99&&battery.Battery[8]==17,"Battery identities");
  Check(Protocol.Parse(Bytes("04 00 04 00 04 00 01 08 01 FF 04 01")).Battery[8]==null,"Unknown battery is not zero");
  Check(Protocol.Parse(Bytes("04 00 04 00 06 00 00 01")).Primary==EarState.InEar,"Wear primary");Check(Protocol.Parse(Bytes("04 00 04 00 06 00 FF 04")).Primary==EarState.Unknown,"Unknown wear must not become out of ear");
  Check(Protocol.Parse(Bytes("04 00 04 00 06 00 FF 04")).Secondary==EarState.Transitional,"Transition distinguished");
  var fixture=Protocol.SetNoise(NoiseMode.Cancellation);for(int n=0;n<fixture.Length;n++){var truncated=new byte[n];Array.Copy(fixture,truncated,n);Check(Protocol.Parse(truncated)==null,"Truncated packet");}
  var random=new Random(23);for(int i=0;i<10000;i++){var data=new byte[random.Next(128)];random.NextBytes(data);Protocol.Parse(data);}Check(true,"Fuzz no exceptions");
  var f=new Fake();using(var s=new Session(f)){s.Start(1);Check(!s.Ready&&f.Sent.Count==2,"Handshake is not readiness");Throws<InvalidOperationException>(()=>s.ChangeNoise(NoiseMode.Transparency,true,30));f.Inbox.Enqueue(fixture);s.Poll(10);var stamp=s.NoiseUpdatedUtc;s.Poll(10);Check(s.Noise==NoiseMode.Cancellation&&stamp==s.NoiseUpdatedUtc,"Timeout retains successful state/timestamp");f.Inbox.Enqueue(Protocol.SetNoise(NoiseMode.Transparency));s.ChangeNoise(NoiseMode.Transparency,true,30);Check(s.Noise==NoiseMode.Transparency,"Confirmed change");Throws<TimeoutException>(()=>s.ChangeNoise(NoiseMode.Cancellation,true,30));Check(s.Noise==NoiseMode.Transparency,"No optimistic success");Throws<NotSupportedException>(()=>s.ChangeNoise(NoiseMode.Adaptive,false,30));using(var adapter=new NativeAdapter("buds",0x2019,s)){Check(!adapter.IsReady("other"),"Identity isolation");Throws<NotSupportedException>(()=>adapter.Write("buds","noise-control","Cancellation"));}Check(!s.Ready,"Dispose invalidates session");}Check(f.Closes==1&&f.Disposed,"Exactly one disconnect");
  var failed=new Fake{FailSend=true};using(var s=new Session(failed)){Throws<IOException>(()=>s.Start(1));Check(failed.Closes==1&&!s.Ready,"Partial start cleanup");}
  var removed=new Fake();using(var s=new Session(removed)){s.Start(1);removed.Inbox.Enqueue(fixture);s.Poll(1);removed.FailReceive=true;Throws<IOException>(()=>s.Poll(1));Check(!s.Ready,"Removal invalidates ready");}
  Throws<InvalidOperationException>(()=>new WindowsChannel("unused",false));
  Console.WriteLine("PASS "+checks+" checks + 10000 malformed packet cases; no hardware connection or settings changes.");
 }
}
