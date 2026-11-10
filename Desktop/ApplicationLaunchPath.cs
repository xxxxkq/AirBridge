using System;
using System.IO;
namespace AirBridge.Desktop {
 internal static class ApplicationLaunchPath {
  internal static string Current {get{var path=Environment.GetEnvironmentVariable("AIRBRIDGE_LAUNCH_PATH");return !String.IsNullOrEmpty(path)&&Path.IsPathRooted(path)&&File.Exists(path)?path:Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"AirBridge.exe");}}
 }
}
