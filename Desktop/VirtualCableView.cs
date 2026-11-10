using System;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using AirBridge.Routing;
namespace AirBridge.Desktop {
 internal sealed partial class MainView {
  Task virtualCableScan=Task.FromResult(0);
  async Task ScanVirtualCable(Button check){
   check.IsEnabled=false;
   bool installed=false;
   try{await Task.Run(()=>VirtualCableRouting.Detect(AirBridge.Platform.Audio.List(),AirBridge.Platform.Audio.CaptureList())).ConfigureAwait(false);installed=true;}
   catch(Exception){/* Missing or ambiguous endpoints are a normal optional-extension state. */}
   // Construction can run before WPF installs its synchronization context.
   // Always marshal completion explicitly, including failed detection.
   if(Window.Dispatcher.HasShutdownStarted)return;
   await Window.Dispatcher.InvokeAsync(()=>{
    if(exiting)return;
    check.Content=installed?L("已安装","Installed"):L("未检测到","Not detected");
    check.ToolTip=installed?L("点击重新检测","Click to check again"):L("安装后重启，再点击检测","Restart after installation, then check again");
    check.IsEnabled=true;
   });
  }
  void BuildVirtualCablePreference(Panel options){
   var toggle=new CheckBox{IsChecked=settings.Bool("VirtualCableEnabled"),Margin=new Thickness(0)};
   
   var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
   var check=Button(L("检测","Check"),()=>{});check.Margin=new Thickness(0,0,12,0);actions.Children.Add(check);actions.Children.Add(toggle);
   check.Click+=(s,e)=>Run(()=>virtualCableScan=ScanVirtualCable(check));
   toggle.Click+=(s,e)=>Run(async()=>{bool enabled=toggle.IsChecked==true;toggle.IsEnabled=false;try{
    if(routes.Busy)throw new InvalidOperationException(L("音频操作进行中，请稍后。","Audio operation in progress."));
    if(enabled)await Task.Run(()=>VirtualCableRouting.Detect(AirBridge.Platform.Audio.List(),AirBridge.Platform.Audio.CaptureList()));
    if(routes.Busy)throw new InvalidOperationException(L("音频操作进行中，请稍后。","Audio operation in progress."));
    // Stop under the previous mode before changing capability and saved startup intent.
    if(routes.Engine.VirtualPcActive){await routes.Stop();if(routes.Engine.PendingAppRestores>0)throw new InvalidOperationException(L("请先恢复原输出，再关闭扩展。","Restore the original output before disabling."));}
    settings.Set("VirtualCableEnabled",enabled);routes.Engine.VirtualPcEnabled=enabled;settings.Save();graphSignature="";RefreshGraph();Translate();Notify(L("设置已保存，应用连线后生效。","Saved. Apply routes to activate."));
   }finally{toggle.IsChecked=settings.Bool("VirtualCableEnabled");toggle.IsEnabled=true;}});
   Preference(options,L("VB-CABLE 音频扩展","VB-CABLE audio extension"),L("电脑或单个应用支持效果链与多输出；停止后恢复原输出。","PC or one app: effects and multiple outputs; Stop restores output."),actions);
   
   var help=Button(L("说明","Help"),()=>MessageBox.Show(Window,L("可选第三方扩展，不随 AirBridge 安装。\n从 vb-audio.com/Cable 下载 VB-CABLE，以管理员身份安装并重启。\n卸载：重新运行其安装器，选择 Remove Driver 并重启。\n先关闭此扩展并停止连线，再卸载。一条 VB-CABLE 同时隔离一个应用或电脑整体音频，不能共用。","Optional third-party extension, not bundled with AirBridge.\nDownload VB-CABLE from vb-audio.com/Cable, install as administrator, then restart.\nTo uninstall, run its installer and choose Remove Driver, then restart.\nDisable this extension and stop routes before uninstalling. One VB-CABLE isolates one app or PC audio at a time."),"VB-CABLE"));help.Margin=new Thickness(0,0,12,0);actions.Children.Insert(1,help);virtualCableScan=ScanVirtualCable(check);
  }
 }
}
