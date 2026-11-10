using System;
using System.IO;
using System.Collections.Generic;
namespace AirBridge.Desktop {
 internal static class BuildVersion {
  public static readonly string Value=Read();
  static string Read(){try{var data=SettingsStore.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"build-info.json")));object value;if(data.TryGetValue("Version",out value))return Convert.ToString(value);}catch{}return "dev";}
 }
}
