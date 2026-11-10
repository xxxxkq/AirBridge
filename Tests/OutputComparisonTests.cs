using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;using AirBridge.Routing;
class OutputComparisonTests {
 static int passed;static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
 sealed class Fake {
  public string Output="original",Identity="11:1";public int Buffer=10,Changes,Restores,Polls;public bool FailChange,FailRestore,Pause,FailSink;public long Frames,Dropped;public readonly List<string> Reports=new List<string>();public Action Step;
  public ComparisonSnapshot Snapshot(){return new ComparisonSnapshot{Phones=new Dictionary<string,string>{{"phoneA",Identity},{"phoneB","22:2"}},Outputs=new[]{Output},TraceDropped=Dropped,Routes=new[]{new RouteState{Source="phoneA",Status="running",BufferTargetMilliseconds=Buffer,PhoneFrames=Frames},new RouteState{Source="phoneB",Status="running",BufferTargetMilliseconds=Buffer,PhoneFrames=0},new RouteState{Source="output",Status=FailSink?"error":"running"}}};}
  public Task Change(string id){Changes++;Output=id;if(FailChange)throw new Exception("output failed");return Task.FromResult(0);}
  public Task Restore(){Restores++;if(FailRestore)throw new Exception("restore failed");Output="original";return Task.FromResult(0);}
  public Task Delay(int ms,CancellationToken token){token.ThrowIfCancellationRequested();Polls++;if(!Pause)Frames+=25;if(Step!=null)Step();return Task.FromResult(0);}
  public Task Run(CancellationToken token){return OutputComparisonRunner.Run(new OutputComparisonConfig{SpeakerId="speaker",HeadphoneId="stereo",SettleSeconds=1,SampleSeconds=1},Change,Snapshot,Restore,(kind,value)=>Reports.Add(kind),token,Delay);}
 }
 static Exception Run(Fake f,CancellationToken token=default(CancellationToken)){try{f.Run(token).GetAwaiter().GetResult();return null;}catch(Exception ex){return ex;}}
 static void Main(){
  var normal=new Fake();Check(Run(normal)==null&&normal.Changes==4&&normal.Restores==1&&normal.Output=="original"&&normal.Reports.Count(x=>x=="phase")==4,"A/B/A/B and exactly one restore");
  var pause=new Fake{Pause=true};Check(Run(pause)==null&&pause.Changes==4&&pause.Frames==0,"zero frames remain waiting; no reconnect or abort");
  var identity=new Fake();identity.Step=()=>identity.Identity="99:3";Check(Run(identity)!=null&&identity.Restores==1&&identity.Changes==1,"identity change invalidates comparison and restores");
  var changedBuffer=new Fake();changedBuffer.Step=()=>changedBuffer.Buffer=160;Check(Run(changedBuffer)!=null&&changedBuffer.Restores==1,"buffer changed: abort, never silently enable buffering");
  var lost=new Fake();lost.Step=()=>lost.Output="hands-free";Check(Run(lost)!=null&&lost.Restores==1,"unexpected endpoint invalidates comparison");
  var error=new Fake{FailChange=true};Check(Run(error)!=null&&error.Restores==1,"failed output open still restores");
  var cancel=new Fake();var cts=new CancellationTokenSource();cancel.Step=()=>cts.Cancel();Check(Run(cancel,cts.Token) is OperationCanceledException&&cancel.Restores==1,"cancel restores without cancelled token");
  var drop=new Fake();drop.Step=()=>drop.Dropped++;Check(Run(drop)!=null&&drop.Restores==1,"lost diagnostics invalidate phase");
  var badInitial=new Fake{Buffer=160};Check(Run(badInitial)!=null&&badInitial.Changes==0&&badInitial.Restores==0,"invalid baseline never touches endpoints");
  var both=new Fake{FailChange=true,FailRestore=true};Check(Run(both) is AggregateException&&both.Restores==1,"preserve primary and restoration failures");
  var outputFail=new Fake();outputFail.Step=()=>outputFail.FailSink=true;Check(Run(outputFail)!=null&&outputFail.Restores==1,"output error cancels test");
  Console.WriteLine("PASS total="+passed+"; no hardware, settings or Bluetooth touched");
 }
}
