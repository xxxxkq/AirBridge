using System;
using System.Reflection;
class PhoneTransportHealthTests {
 static void Main() {
  var type=Assembly.LoadFrom("AirBridge.Routing.dll").GetType("AirBridge.Routing.PhoneTransportHealth",true);
  object h=Activator.CreateInstance(type,true);
  Action<double> frame=t=>type.GetMethod("Frame").Invoke(h,new object[]{t});
  Func<bool,double,bool> check=(closed,t)=>(bool)type.GetMethod("Check").Invoke(h,new object[]{closed,t});
  Action<bool,string> require=(value,message)=>{if(!value)throw new Exception(message);};
  require(!check(false,60000),"Paused open connection failed");
  require(!check(true,61000),"Closed notification immediately failed");
  for(int t=61000;t<=70000;t+=500){frame(t);require(!check(true,t),"Live stream closed by peer notification");}
  require(!check(true,120000),"Normal pause after disproven Closed state failed");
  require(!check(false,121000)&&!check(true,122000),"Fresh close not observed");
  require(!check(true,124999),"Closed stream failed before grace");
  require(check(true,125000),"Dead closed stream never failed");
  require(!check(false,126000),"Opened state did not reset");
  require(!check(true,127000)&&!check(false,127100)&&!check(true,130000),"Transient close did not reset grace");
  require(check(true,133000),"Repeated close did not expire");
  Console.WriteLine("PASS: paused, live frames during Closed, timeout, reopen and transient close");
 }
}
