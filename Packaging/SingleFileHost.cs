using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
namespace AirBridge.Packaging {
 // A single distribution file. The runtime stays in an immutable, content-addressed cache.
 internal static class SingleFileHost {
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetDllDirectory(string path);
  internal static bool ValidArguments(string[] a){return a.Length==0||(a.Length==1&&a[0]=="--background");}
  static string Hash(Stream stream){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
  static string Digest(byte[] data){using(var stream=new MemoryStream(data))return Hash(stream);}
  static bool ValidCache(string directory,ZipArchive archive){
   if(!Directory.Exists(directory))return false;
   if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("Runtime cache is a linked directory.");
   foreach(var e in archive.Entries){string path=Target(directory,e.FullName);if(!File.Exists(path))return false;
    // Reject linked path components before hashing or loading code.
    for(string part=path;part!=directory;part=Path.GetDirectoryName(part))if((File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked runtime cache entry.");
    using(var expected=e.Open())using(var actual=File.OpenRead(path))if(Hash(expected)!=Hash(actual))return false;
   }return true;
  }
  static string Target(string root,string name){string path=Path.GetFullPath(Path.Combine(root,name.Replace('/',Path.DirectorySeparatorChar)));if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid bundle entry.");return path;}
  internal static string PreparePayload(string cacheRoot){
   byte[] data;using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("AirBridge.Payload.zip"))using(var memory=new MemoryStream()){if(input==null)throw new InvalidDataException("Missing runtime payload.");input.CopyTo(memory);data=memory.ToArray();}
   string hash=Digest(data),parent=Path.GetFullPath(cacheRoot),directory=Path.Combine(parent,hash);
   Directory.CreateDirectory(parent);if((File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0)throw new IOException("Runtime cache parent is linked.");
   using(var mutex=new Mutex(false,@"Local\AirBridge.Bundle."+Digest(System.Text.Encoding.UTF8.GetBytes(parent+hash)))){
    bool held=false;try{try{held=mutex.WaitOne(30000);}catch(AbandonedMutexException){held=true;}if(!held)throw new TimeoutException("Runtime extraction is busy.");
     using(var memory=new MemoryStream(data))using(var zip=new ZipArchive(memory,ZipArchiveMode.Read)){
      if(ValidCache(directory,zip))return directory;
      // Never replace a possibly loaded cache. A damaged cache gets a fresh sibling.
      if(Directory.Exists(directory))directory=Path.Combine(parent,hash+"-"+Guid.NewGuid().ToString("N"));
      string stage=Path.Combine(parent,"extract-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
      try{foreach(var entry in zip.Entries){string path=Target(stage,entry.FullName);Directory.CreateDirectory(Path.GetDirectoryName(path));using(var source=entry.Open())using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))source.CopyTo(output);}
       if(!ValidCache(stage,zip))throw new InvalidDataException("Runtime verification failed.");Directory.Move(stage,directory);return directory;
      }finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
     }
    }finally{if(held)mutex.ReleaseMutex();}
   }
  }
  [STAThread]static int Main(string[] args){if(!ValidArguments(args))return 2;
   try{string launch=Assembly.GetExecutingAssembly().Location;Environment.SetEnvironmentVariable("AIRBRIDGE_LAUNCH_PATH",launch);if(AirBridge.Storage.DataLocation.SignalExisting())return 0;
    var storage=AirBridge.Storage.DataLocation.Resolve(launch,AirBridge.Storage.DataLocation.LegacyRoot,true,AirBridge.Storage.DataLocation.RecoveryIsQuiet);Environment.SetEnvironmentVariable(AirBridge.Storage.DataLocation.EnvironmentKey,storage.Root);Environment.SetEnvironmentVariable("AIRBRIDGE_STORAGE_NOTICE",storage.Notice);
    string runtime=PreparePayload(Path.Combine(storage.Root,"runtime"));
    if(!SetDllDirectory(runtime))throw new System.ComponentModel.Win32Exception();
    var setup=new AppDomainSetup{ApplicationBase=runtime,ConfigurationFile=Path.Combine(runtime,"AirBridge.exe.config")};
    var domain=AppDomain.CreateDomain("AirBridge.Runtime",null,setup);
    try{return domain.ExecuteAssembly(Path.Combine(runtime,"AirBridge.exe"),args);}finally{AppDomain.Unload(domain);}
   }catch(Exception ex){try{string root=AirBridge.Storage.DataLocation.Current;Directory.CreateDirectory(root);File.AppendAllText(Path.Combine(root,"bundle-error.log"),DateTime.UtcNow.ToString("o")+" "+ex+Environment.NewLine);}catch{}MessageBox.Show(ex.GetBaseException().Message,"AirBridge");return 1;}
  }
 }
}
