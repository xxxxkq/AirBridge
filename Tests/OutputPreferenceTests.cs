using System;
using System.IO;
using System.Reflection;
using AirBridge.Routing;
class OutputPreferenceTests {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static int Main(){try{Run();return 0;}catch(Exception ex){Console.WriteLine("FAIL "+ex.Message);return 1;}}
 static void Run(){var folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"output-preference-fixture");Directory.CreateDirectory(folder);
  var path=Path.Combine(folder,Guid.NewGuid().ToString("N")+".xml");var state=new[]{"speaker","speaker"};int writes=0;
  Func<DefaultOutputRoute> create=()=>new DefaultOutputRoute(path,r=>state[r],(id,r)=>{state[r]=id;writes++;},id=>id!="missing");
  var route=create();route.Apply("headphones");
  var remember=typeof(DefaultOutputRoute).GetMethod("RememberUserSelection");Check(remember!=null,"explicit user preference supported");
  remember.Invoke(route,new object[]{"headphones"});int before=writes;create().Restore();
  Check(state[0]=="headphones"&&state[1]=="headphones"&&writes==before,"same-as-managed user selection survives restart and restore");
  route=create();route.Apply("cable");remember.Invoke(route,new object[]{"headphones"});create().Restore();
  Check(state[0]=="headphones"&&state[1]=="headphones","unselected target cannot replace recovery baseline");
  route=create();route.Apply("cable");state[0]="external";state[1]="external";create().Restore();
  Check(state[0]=="external"&&state[1]=="external","external default choice is preserved");
  state[0]=state[1]="speaker";route=create();route.Apply("cable");create().Restore();
  Check(state[0]=="speaker"&&state[1]=="speaker","temporary virtual endpoint still restores original");
  File.Delete(path);DefaultRouteTests.Run(folder);Console.WriteLine("PASS output preference; no system endpoints opened");
 }
}
