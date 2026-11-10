using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
namespace AirBridge.Core {
 public enum CapabilityState { Available, RequiresTransport, Unsupported }
 public sealed class Capability {
  public string Id {get;set;} public string Name {get;set;} public string NameEn {get;set;}
  public CapabilityState State {get;set;} public string Reason {get;set;} public string ReasonEn {get;set;}
 }
 public sealed class DeviceIdentity {
  public string Id {get;set;} public string ContainerId {get;set;} public string DisplayName {get;set;}
  public string Kind {get;set;} public bool IsLocal {get;set;}
  public string HardwareIds {get;set;} public string EndpointId {get;set;}
  public bool IsDefault {get;set;} public bool IsActive {get;set;}
  public string CaptureId {get;set;} public bool CaptureActive {get;set;}
  public int Channels {get;set;} public int Battery {get;set;}
 }
 public sealed class DeviceProfile {
  public string Kind {get;set;}
  public string ProviderId {get;set;} public string Model {get;set;} public string Brand {get;set;}
  public string Evidence {get;set;} public string EvidenceEn {get;set;}
  public List<Capability> Capabilities {get;set;}
 }
 public interface IHeadsetProvider {
  string Id {get;}
  bool Matches(DeviceIdentity identity);
  DeviceProfile Describe(DeviceIdentity identity);
 }
 // A future adapter must provide a validated transport and read back the actual
 // device state. Advertising a product capability alone never enables a write.
 public interface INativeHeadsetTransport : IDisposable {
  bool IsReady(string deviceId);
  string Read(string deviceId,string capability);
  void Write(string deviceId,string capability,string value);
 }
 public class GenericProvider : IHeadsetProvider {
  public virtual string Id {get{return "windows-generic";}}
  public virtual bool Matches(DeviceIdentity d){return true;}
  protected Capability Feature(string id,string zh,string en,bool available,string reason,string reasonEn){return new Capability{Id=id,Name=zh,NameEn=en,State=available?CapabilityState.Available:CapabilityState.Unsupported,Reason=reason,ReasonEn=reasonEn};}
  public virtual DeviceProfile Describe(DeviceIdentity d){return new DeviceProfile {
   Kind=d.Kind,ProviderId=Id,Model=d.DisplayName??"Audio device",Brand="Windows",
   Evidence="Windows 报告的设备名称",EvidenceEn="Device name reported by Windows",
   Capabilities=new List<Capability>{
    Feature("volume","设备音量","Endpoint volume",d.IsActive,"Windows 音频端点","Windows audio endpoint"),
    Feature("mute","静音","Mute",d.IsActive,"Windows 音频端点","Windows audio endpoint"),
    Feature("balance","左右声道","Channel levels",d.IsActive&&d.Channels>=2,"需要至少两个可用声道","Requires two available channels")
   }};}
 }
 public sealed class AppleProvider : GenericProvider {
  public override string Id {get{return "apple-airpods";}}
  static readonly Dictionary<int,string> Models=new Dictionary<int,string>{{0x2002,"AirPods 1"},{0x200f,"AirPods 2"},{0x2013,"AirPods 3"},{0x2019,"AirPods 4"},{0x201b,"AirPods 4 (ANC)"},{0x200e,"AirPods Pro"},{0x2014,"AirPods Pro 2 (Lightning)"},{0x2024,"AirPods Pro 2 (USB-C)"},{0x200a,"AirPods Max"}};
  static int Product(DeviceIdentity d){var h=d.HardwareIds??"";if(!Regex.IsMatch(h,@"VID&(?:0001)?004C",RegexOptions.IgnoreCase))return -1;var m=Regex.Match(h,@"PID&([0-9A-F]{4})",RegexOptions.IgnoreCase);return m.Success?Convert.ToInt32(m.Groups[1].Value,16):-1;}
  // Only models decoded by BluetoothMonitor's wear advertisement parser.
  public static bool SupportsBroadcastWear(DeviceIdentity d){int product=Product(d);return !d.IsLocal&&Models.ContainsKey(product)&&product!=0x200a;}
  public override bool Matches(DeviceIdentity d){return !d.IsLocal&&(Models.ContainsKey(Product(d))||(d.DisplayName??"").IndexOf("AirPods",StringComparison.OrdinalIgnoreCase)>=0);}
  public override DeviceProfile Describe(DeviceIdentity d){var p=base.Describe(d);p.Brand="Apple";int product=Product(d);
   if(Models.ContainsKey(product)){p.Model=Models[product];p.Evidence="硬件 VID / PID，关联同一设备容器";p.EvidenceEn="Hardware VID/PID in the same device container";}
   else {p.Evidence="Windows 名称；具体代际未确认";p.EvidenceEn="Windows name; generation unconfirmed";}
   bool known=Models.ContainsKey(product),anc=product==0x201b||product==0x200e||product==0x2014||product==0x2024||product==0x200a;
   foreach(var item in new[]{new[]{"noise-control","降噪 / 通透","Noise control"},new[]{"wear-detection","佩戴检测","Wear detection"},new[]{"gestures","按压与手势","Press and gesture controls"}}){
    p.Capabilities.Add(new Capability{Id=item[0],Name=item[1],NameEn=item[2],State=known&&item[0]=="noise-control"&&!anc?CapabilityState.Unsupported:CapabilityState.RequiresTransport,
     Reason=known&&item[0]=="noise-control"&&!anc?"此型号不支持主动降噪":"当前未接入 Apple AAP 控制通道",
     ReasonEn=known&&item[0]=="noise-control"&&!anc?"This model has no ANC":"Apple AAP control transport is not connected"});
   }return p;
  }
 }
 public sealed class NamedProvider : GenericProvider {
  readonly string brand,pattern;
  public NamedProvider(string brand,string pattern){this.brand=brand;this.pattern=pattern;}
  public override string Id {get{return brand.ToLowerInvariant()+"-windows";}}
  public override bool Matches(DeviceIdentity d){return !d.IsLocal&&Regex.IsMatch(d.DisplayName??"",pattern,RegexOptions.IgnoreCase);}
  public override DeviceProfile Describe(DeviceIdentity d){var p=base.Describe(d);p.Brand=brand;p.Evidence="Windows 型号名称；厂商协议未接入";p.EvidenceEn="Windows model name; vendor protocol not connected";p.Capabilities.Add(new Capability{Id="vendor-settings",Name="厂商原生功能",NameEn="Vendor-specific controls",State=CapabilityState.RequiresTransport,Reason="需为该型号实现并验证专用适配器",ReasonEn="A model-specific adapter must be implemented and verified"});return p;}
 }
 public sealed class ProviderRegistry {
  readonly List<IHeadsetProvider> providers=new List<IHeadsetProvider>{new AppleProvider(),new NamedProvider("Sony",@"Sony|WH-1000|WF-1000"),new NamedProvider("vivo",@"vivo|iQOO"),new NamedProvider("Bose",@"Bose|QuietComfort"),new NamedProvider("Beats",@"Beats"),new GenericProvider()};
  public void Register(IHeadsetProvider provider){if(provider==null)throw new ArgumentNullException("provider");providers.Insert(0,provider);}
  public DeviceProfile Resolve(DeviceIdentity identity){if(identity==null)throw new ArgumentNullException("identity");return providers.First(p=>p.Matches(identity)).Describe(identity);}
 }
}
