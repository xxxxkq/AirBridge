using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AirBridge.Desktop;
using AirBridge.Core;
using AirBridge.Platform;
using AirBridge.UI;
class ReadmeScreenshots {
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Call(MainView v,string method,params object[] args){typeof(MainView).GetMethod(method,Flags).Invoke(v,args);}
 static void Render(string output,string page){
  MainView view=null;
  try{
   var settings=new SettingsStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"demo-"+page),true);settings.Set("Language","zh-CN");settings.Set("Theme","light");settings.Set("AutoConnect",false);settings.Set("ResumeGraph",false);settings.Set("NodeLibraryCollapsed",false);settings.Set("SidebarCollapsed",false);
   view=new MainView(settings,null,false);
   var inventory=new DeviceInventory{ReadAt=new DateTime(2026,11,11,12,0,0),Endpoints=new[]{new Endpoint{Id="demo:headphones",Name="耳机 (AirPods)",Active=true,FormFactor=3},new Endpoint{Id="demo:speaker",Name="扬声器",Active=true,FormFactor=1}},Captures=new[]{new Endpoint{Id="demo:mic",Name="USB 麦克风",Active=true,IsCapture=true}},Devices=new[]{
    new DeviceIdentity{Id="demo:pods",DisplayName="AirPods 4 (ANC)",IsActive=true,IsDefault=true,EndpointId="demo:headphones",HardwareIds="VID&0001004C_PID&201B",Battery=76,Channels=2},
    new DeviceIdentity{Id="demo:microphone",DisplayName="USB 麦克风",IsLocal=true,Kind="microphone",CaptureId="demo:mic",CaptureActive=true,IsActive=true,Battery=-1},
    new DeviceIdentity{Id="demo:speakers",DisplayName="扬声器 (Realtek Audio)",IsLocal=true,Kind="speaker",EndpointId="demo:speaker",IsActive=true,Battery=-1},
    new DeviceIdentity{Id="demo:monitor",DisplayName="显示器音频",IsLocal=true,Kind="display",EndpointId="demo:monitor-output",IsActive=true,Battery=-1}}};
   typeof(MainView).GetField("inventory",Flags).SetValue(view,inventory);Call(view,"RenderDevices");Call(view,"Navigate",page);
   var content=(FrameworkElement)view.Window.Content;view.Window.Content=null;
   var host=new Border{Child=content,Resources=view.Window.Resources,Background=(Brush)view.Window.FindResource("Brush_F5F6F8"),Width=1200,Height=800,UseLayoutRounding=true,SnapsToDevicePixels=true};
   System.Windows.Documents.TextElement.SetFontFamily(host,view.Window.FontFamily);System.Windows.Documents.TextElement.SetFontSize(host,view.Window.FontSize);System.Windows.Documents.TextElement.SetForeground(host,view.Window.Foreground);
   host.Measure(new Size(1200,800));host.Arrange(new Rect(0,0,1200,800));host.UpdateLayout();
   if(page=="Audio"){
    var board=view.U<AudioBoard>("RouteBoard");board.Nodes.Clear();board.Links.Clear();
    board.Nodes.Add(new AudioNode{Id="demo:phone",Name="iPhone",Detail="蓝牙媒体输入",Input=true,Available=true,Artwork="phone"});
    board.Nodes.Add(new AudioNode{Id="demo:mic",Name="USB 麦克风",Detail="独立录音输入",Input=true,Available=true,Artwork="microphone"});
    board.Nodes.Add(new AudioNode{Id="demo:eq",Name="均衡器",Detail="Music",Module=true,Available=true,Artwork="audio"});
    board.Nodes.Add(new AudioNode{Id="demo:mix",Name="混音 / 切换",Detail="mix",Module=true,Available=true,Artwork="audio"});
    board.Nodes.Add(new AudioNode{Id="demo:pods-out",Name="耳机 (AirPods)",Detail="播放输出",Available=true,Artwork="headset"});
    board.Nodes.Add(new AudioNode{Id="demo:speaker-out",Name="扬声器",Detail="播放输出",Available=true,Artwork="speaker"});
    board.Links.Add(new AudioLink{Source="demo:phone",Target="demo:eq"});board.Links.Add(new AudioLink{Source="demo:eq",Target="demo:mix"});board.Links.Add(new AudioLink{Source="demo:mic",Target="demo:mix"});board.Links.Add(new AudioLink{Source="demo:mix",Target="demo:pods-out"});board.Links.Add(new AudioLink{Source="demo:mix",Target="demo:speaker-out"});
    Call(view,"SetLibrary",true);board.ArrangeNodes();host.UpdateLayout();board.Fit();var positions=board.ExportPositions();board.CenterOn(new Point((positions.Min(p=>p.X)+positions.Max(p=>p.X)+244)/2,(positions.Min(p=>p.Y)+positions.Max(p=>p.Y)+80)/2));view.U<TextBlock>("GraphStatus").Text="演示连线 · 尚未应用";
   }
   if(page=="Devices")view.U<TextBlock>("InventoryTime").Text="演示设备";
   host.UpdateLayout();var image=new RenderTargetBitmap(1200,800,96,96,PixelFormats.Pbgra32);image.Render(host);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(output))encoder.Save(stream);
   if(view.Window.IsVisible)throw new Exception("Unexpected visible window");Console.WriteLine("Rendered "+Path.GetFileName(output)+" (demo data; no visible window)");
  }finally{if(view!=null){var coordinator=typeof(MainView).GetField("routes",Flags).GetValue(view);Task.Run(async()=>await (Task)coordinator.GetType().GetMethod("ShutdownAsync").Invoke(coordinator,null)).GetAwaiter().GetResult();}}
 }
 [STAThread]static int Main(string[] args){try{Directory.CreateDirectory(args[0]);Render(Path.Combine(args[0],"audio-graph.png"),"Audio");Render(Path.Combine(args[0],"devices.png"),"Devices");return 0;}catch(Exception ex){Console.WriteLine(ex);return 1;}}
}
