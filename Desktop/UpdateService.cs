using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace AirBridge.Desktop {
 internal sealed class UpdatePolicy {
  public bool AutoCheck,Forbidden,SessionSkipped;public string SkipDate="",IgnoredTag="";
  public bool CanAutomaticallyCheck(DateTime local){return AutoCheck&&!Forbidden&&!SessionSkipped&&SkipDate!=local.ToString("yyyy-MM-dd");}
  public bool CanNotify(string tag){return !Forbidden&&!SessionSkipped&&IgnoredTag!=tag;}
 }
 internal sealed class UpdateRelease {public string Tag,Name,Notes,Url,Digest,Page;public Version Version;public long Size;}
 internal sealed class UpdateProxyException:IOException {public UpdateProxyException(string message):base(message){}}
 internal sealed class UpdateRepositoryException:IOException {public UpdateRepositoryException():base("Update repository is not anonymously accessible; it may be private, renamed or unavailable.") {}}
 internal static class UpdateService {
  internal const string Api="https://api.github.com/repos/xxxxkq/AirBridge/releases/latest";
  internal const string Page="https://github.com/xxxxkq/AirBridge/releases";
  const string DownloadPrefix="https://github.com/xxxxkq/AirBridge/releases/download/";
  internal static Version ParseVersion(string tag){Version v;if(String.IsNullOrWhiteSpace(tag)||!Version.TryParse(tag.TrimStart('v','V'),out v))return null;return new Version(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision));}
  static string Text(Dictionary<string,object> d,string key){object v;return d.TryGetValue(key,out v)?Convert.ToString(v):"";}
  internal static UpdateRelease Parse(string json,string current){
   var d=new JavaScriptSerializer{MaxJsonLength=1048576}.Deserialize<Dictionary<string,object>>(json);object value;
   if(d==null)throw new InvalidDataException("Invalid release metadata");
   if(d.TryGetValue("draft",out value)&&Convert.ToBoolean(value)||d.TryGetValue("prerelease",out value)&&Convert.ToBoolean(value))return null;
   string tag=Text(d,"tag_name");var version=ParseVersion(tag);var installed=ParseVersion(current);if(version==null||installed==null)throw new InvalidDataException("Invalid update version");if(version<=installed)return null;
   if(!d.TryGetValue("assets",out value))throw new InvalidDataException("Release has no assets");
   var assets=value as System.Collections.IEnumerable;if(assets==null||value is string)throw new InvalidDataException("Invalid release assets");
   var matches=assets.Cast<object>().OfType<Dictionary<string,object>>().Where(a=>Text(a,"name")=="AirBridge.exe").ToArray();
   if(matches.Length!=1)throw new InvalidDataException("Release must contain one AirBridge.exe single-file asset");
   var asset=matches[0];string url=Text(asset,"browser_download_url"),digest=Text(asset,"digest");long size;
   Uri downloadUri;if(!url.StartsWith(DownloadPrefix,StringComparison.Ordinal)||!Uri.TryCreate(url,UriKind.Absolute,out downloadUri)||downloadUri.Host!="github.com"||!downloadUri.AbsolutePath.StartsWith("/xxxxkq/AirBridge/releases/download/",StringComparison.Ordinal))throw new InvalidDataException("Unexpected update download source");
   if(!System.Text.RegularExpressions.Regex.IsMatch(digest,@"^sha256:[a-fA-F0-9]{64}$"))throw new InvalidDataException("Missing SHA256 digest; automatic download is unavailable");
   if(!Int64.TryParse(Text(asset,"size"),out size)||size<1024||size>50*1024*1024)throw new InvalidDataException("Unexpected update size");
   string notes=Text(d,"body");return new UpdateRelease{Tag=tag,Version=version,Name=Text(d,"name"),Notes=notes.Length>2000?notes.Substring(0,2000):notes,Url=url,Digest=digest.Substring(7).ToLowerInvariant(),Size=size,Page=Page+"/tag/"+Uri.EscapeDataString(tag)};
  }
  static bool Trusted(Uri u){return u.Scheme=="https"&&(u.Host=="api.github.com"||u.Host=="github.com"||u.Host=="release-assets.githubusercontent.com"||u.Host=="objects.githubusercontent.com"||u.Host=="github-releases.githubusercontent.com");}
  // The legacy compiler does not emit a target-framework attribute, so Framework
  // defaults can still be SSL3/TLS1.0 despite running on 4.8. Scope this to our AppDomain.
  internal static void ConfigureTransport(){ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;}
  internal static IWebProxy SelectProxy(string url,string configured,string exclusions){
   if(String.IsNullOrWhiteSpace(configured))return WebRequest.DefaultWebProxy;var target=new Uri(url);
   foreach(var item in (exclusions??"").Split(',')){string host=item.Trim().TrimStart('*','.');if(item.Trim()=="*"||host!=""&&(String.Equals(target.Host,host,StringComparison.OrdinalIgnoreCase)||target.Host.EndsWith("."+host,StringComparison.OrdinalIgnoreCase)))return null;}
   Uri address;string value=configured.Contains("://")?configured:"http://"+configured;if(!Uri.TryCreate(value,UriKind.Absolute,out address)||address.Scheme!="http"&&address.Scheme!="https")throw new UpdateProxyException("Unsupported HTTPS_PROXY/HTTP_PROXY configuration");
   var builder=new UriBuilder(address){UserName="",Password=""};var proxy=new WebProxy(builder.Uri);if(address.UserInfo!=""){var credentials=address.UserInfo.Split(new[]{':'},2);proxy.Credentials=new NetworkCredential(Uri.UnescapeDataString(credentials[0]),credentials.Length>1?Uri.UnescapeDataString(credentials[1]):"");}return proxy;
  }
  internal static string FailureCode(Exception ex){
   if(ex is UpdateRepositoryException)return "repository-access";
   var web=ex as WebException;if(web!=null){var response=web.Response as HttpWebResponse;if(response!=null){if(response.StatusCode==HttpStatusCode.NotFound)return "no-release";if((int)response.StatusCode==429||response.Headers["X-RateLimit-Remaining"]=="0")return "rate-limit";if(response.StatusCode==HttpStatusCode.Forbidden||response.StatusCode==HttpStatusCode.Unauthorized)return "access";}
    if(web.Status==WebExceptionStatus.SecureChannelFailure)return "tls";if(web.Status==WebExceptionStatus.TrustFailure)return "certificate";if(web.Status==WebExceptionStatus.Timeout)return "timeout";if(web.Status==WebExceptionStatus.NameResolutionFailure)return "dns";if(web.Status==WebExceptionStatus.ProxyNameResolutionFailure)return "proxy";}
   if(ex is UpdateProxyException)return "proxy";if(ex is InvalidDataException)return "release-format";return "network";
  }
  static async Task<HttpWebResponse> Response(string url,CancellationToken token){
   ConfigureTransport();
   for(int i=0;i<6;i++){token.ThrowIfCancellationRequested();var uri=new Uri(url);if(!Trusted(uri))throw new InvalidDataException("Unexpected update redirect");
    var request=(HttpWebRequest)WebRequest.Create(uri);request.UserAgent="AirBridge-Updater";request.Accept="application/vnd.github+json";request.Headers["X-GitHub-Api-Version"]="2022-11-28";request.AllowAutoRedirect=false;request.Timeout=15000;request.ReadWriteTimeout=15000;
    string configured=Environment.GetEnvironmentVariable("HTTPS_PROXY")??Environment.GetEnvironmentVariable("https_proxy")??Environment.GetEnvironmentVariable("HTTP_PROXY")??Environment.GetEnvironmentVariable("http_proxy");request.Proxy=SelectProxy(url,configured,Environment.GetEnvironmentVariable("NO_PROXY")??Environment.GetEnvironmentVariable("no_proxy"));
    HttpWebResponse response;using(token.Register(request.Abort)){response=(HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false);}
    int status=(int)response.StatusCode;if(status>=300&&status<=399){string location=response.Headers["Location"];response.Dispose();if(String.IsNullOrEmpty(location))throw new InvalidDataException("Missing redirect");url=new Uri(uri,location).AbsoluteUri;continue;}return response;
   }throw new InvalidDataException("Too many redirects");
  }
  internal static async Task<UpdateRelease> Check(string current,CancellationToken token){
   WebException missing;
   try{using(var response=await Response(Api,token).ConfigureAwait(false))using(token.Register(response.Close))using(var input=response.GetResponseStream())using(var memory=new MemoryStream()){
    await CopyBounded(input,memory,1048576,token).ConfigureAwait(false);return Parse(Encoding.UTF8.GetString(memory.ToArray()),current);
   }}catch(WebException failure){var failed=failure.Response as HttpWebResponse;if(failed==null||failed.StatusCode!=HttpStatusCode.NotFound)throw;failed.Dispose();missing=failure;}
    // A private/nonexistent repo also returns 404. Do not mislabel it as an empty release list.
    try{using(var repository=await Response("https://api.github.com/repos/xxxxkq/AirBridge",token).ConfigureAwait(false)){} }
    catch(WebException denied){var response=denied.Response as HttpWebResponse;if(response!=null&&(response.StatusCode==HttpStatusCode.NotFound||response.StatusCode==HttpStatusCode.Forbidden||response.StatusCode==HttpStatusCode.Unauthorized)){response.Dispose();throw new UpdateRepositoryException();}throw;}
    throw missing;
  }
  static async Task CopyBounded(Stream input,Stream output,long limit,CancellationToken token){var buffer=new byte[16384];long total=0;int count;
   while((count=await input.ReadAsync(buffer,0,buffer.Length,token).ConfigureAwait(false))>0){total+=count;if(total>limit)throw new InvalidDataException("Update response exceeds limit");await output.WriteAsync(buffer,0,count,token).ConfigureAwait(false);}}
  internal static string HashFile(string file){using(var input=File.OpenRead(file))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();}
  internal static async Task<string> Download(UpdateRelease release,string root,CancellationToken token){
   string directory=Path.Combine(root,"updates","downloads");Directory.CreateDirectory(directory);string path=Path.Combine(directory,Guid.NewGuid().ToString("N")+".exe");
   try{using(var response=await Response(release.Url,token).ConfigureAwait(false))using(token.Register(response.Close))using(var input=response.GetResponseStream())using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,16384,true)){await CopyBounded(input,file,release.Size,token).ConfigureAwait(false);}
    Verify(path,release.Digest,release.Size,release.Version);return path;
   }catch{if(File.Exists(path))File.Delete(path);throw;}
  }
  internal static void Verify(string path,string digest,long size,Version version){if(new FileInfo(path).Length!=size||!String.Equals(HashFile(path),digest,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update checksum mismatch");if(ParseVersion(FileVersionInfo.GetVersionInfo(path).FileVersion)!=version)throw new InvalidDataException("Update executable version mismatch");}
 }
}
