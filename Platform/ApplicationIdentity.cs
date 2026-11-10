using System;
using System.Diagnostics;
using System.Linq;
namespace AirBridge.Routing {
 // PID + creation time identifies a live instance; executable path identifies a restart.
 public static class ApplicationIdentity {
  public static string ProcessPath(Process process){try{return process.MainModule.FileName??"";}catch{return "";}}
  public static ApplicationInput Resolve(string source,string path,ApplicationInput[] applications){
   applications=applications??new ApplicationInput[0];
   var exact=applications.FirstOrDefault(a=>a.Id==source&&(String.IsNullOrEmpty(path)||String.Equals(a.Path,path,StringComparison.OrdinalIgnoreCase)));
   if(exact!=null)return exact;
   if(String.IsNullOrWhiteSpace(path))return null;
   var matches=applications.Where(a=>String.Equals(a.Path,path,StringComparison.OrdinalIgnoreCase)).ToArray();
   return matches.Length==1?matches[0]:null;
  }
  public static string Status(string source,string path,ApplicationInput[] applications){
   applications=applications??new ApplicationInput[0];
   if(Resolve(source,path,applications)!=null)return "ready";
   if(String.IsNullOrWhiteSpace(path))return "select";
   return applications.Count(a=>String.Equals(a.Path,path,StringComparison.OrdinalIgnoreCase))>1?"ambiguous":"closed";
  }
 }
}
