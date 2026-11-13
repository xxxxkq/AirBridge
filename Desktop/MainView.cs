using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using AirBridge.Core;
using AirBridge.Platform;
using AirBridge.UI;
using AirBridge.Routing;
namespace AirBridge.Desktop {
 internal sealed partial class MainView {
  public readonly Window Window;readonly WindowWorkArea windowWorkArea;public bool TestFailed;
  readonly SettingsStore settings;readonly EventWaitHandle signal;readonly bool background;readonly PhoneService phones;readonly RouteCoordinator routes;readonly AirBridge.Monitor monitor=new AirBridge.Monitor();readonly ProviderRegistry providers=new ProviderRegistry();
  readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(150)};
  DeviceInventory inventory;Task<DeviceInventory> inventoryTask;DateTime inventoryAt,noticeUntil,lastPoll,lastWearPoll;string inventoryError="",page="Devices",notice="";bool exiting,rendering,dirty;int phoneRevision=-1;
  readonly Dictionary<string,bool> expanded=new Dictionary<string,bool>();
  internal T U<T>(string name) where T:class{return Window.FindName(name) as T;}
  internal string L(string zh,string en){return settings.Text("Language")=="en"||settings.Text("Language")=="system"&&System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName!="zh"?en:zh;}
  internal static Brush Brush(string color){return (Brush)new BrushConverter().ConvertFromString(color);}
  internal TextBlock Label(string text,int size=14,string color="202634"){var t=new TextBlock{Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap};t.SetResourceReference(TextBlock.ForegroundProperty,"Brush_"+color);return t;}
  internal StackPanel Glyph(int code,string text){var p=new StackPanel{Orientation=Orientation.Horizontal};p.Children.Add(new TextBlock{Text=Char.ConvertFromUtf32(code),FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=16,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,14,0)});var label=Label(text);label.VerticalAlignment=VerticalAlignment.Center;p.Children.Add(label);return p;}
  internal Button Button(string text,Action action){var b=new Button{Content=text};b.Click+=(s,e)=>{try{action();}catch(Exception ex){Error(ex);}};return b;}
  internal void Click(string name,Action action){U<Button>(name).Click+=(s,e)=>{try{action();}catch(Exception ex){Error(ex);}};}
  internal void Error(Exception ex){Notify(PlaybackOutputException.Display(ex,L("zh","en")=="en"));Log(ex.ToString());}
  internal void Log(string text){if(settings.Test||settings.Text("LogLevel")=="Off")return;try{string path=Path.Combine(settings.Root,"AirBridge-desktop.log");if(File.Exists(path)&&new FileInfo(path).Length>1048576){if(File.Exists(path+".1"))File.Delete(path+".1");File.Move(path,path+".1");}File.AppendAllText(path,DateTime.Now.ToString("o")+" "+text+Environment.NewLine);}catch{}}
  internal async void Run(Func<Task> action){try{await action();}catch(Exception ex){if(!exiting)Error(ex);else Log(ex.ToString());}}
  public MainView(SettingsStore settings,EventWaitHandle signal,bool background){this.settings=settings;this.signal=signal;this.background=background;phones=new PhoneService(settings);routes=new RouteCoordinator(settings,phones);
   using(var reader=System.Xml.XmlReader.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"UI","Shell.xaml")))Window=(Window)System.Windows.Markup.XamlReader.Load(reader);
   windowWorkArea=new WindowWorkArea(Window);Window.Icon=new BitmapImage(new Uri(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Assets","AirBridge.ico")));U<TextBlock>("SidebarVersion").Text="AirBridge  "+BuildVersion.Value;
   SetupDesktop();SetupGraph();SetupDevices();BuildPreferences();Translate();
   U<Button>("Exit").MouseEnter+=(s,e)=>((TextBlock)((StackPanel)U<Button>("Exit").Content).Children[0]).Foreground=Brush("#CE7585");U<Button>("Exit").MouseLeave+=(s,e)=>((TextBlock)((StackPanel)U<Button>("Exit").Content).Children[0]).SetResourceReference(TextBlock.ForegroundProperty,"Brush_7C8595");
   Click("TitleMinimize",()=>Window.WindowState=WindowState.Minimized);Click("TitleMaximize",ToggleMaximize);Click("TitleClose",()=>Window.Hide());Click("Exit",Exit);
   Window.Closing+=(s,e)=>{if(!exiting){e.Cancel=true;Window.Hide();}};Window.StateChanged+=(s,e)=>{bool maximized=Window.WindowState==WindowState.Maximized;U<Button>("TitleMaximize").Content=Char.ConvertFromUtf32(maximized?0xE923:0xE922);U<Button>("TitleMaximize").ToolTip=maximized?L("还原","Restore"):L("最大化","Maximize");if(Window.WindowState==WindowState.Minimized)Window.Hide();};
   U<Grid>("TitleBar").MouseLeftButtonDown+=(s,e)=>{if(e.ClickCount==2){e.Handled=true;ToggleMaximize();}else if(e.LeftButton==MouseButtonState.Pressed)Window.DragMove();};
   Click("ToggleSidebar",()=>{settings.Set("SidebarCollapsed",!settings.Bool("SidebarCollapsed"));settings.Save();Sidebar(true);});
   foreach(string name in new[]{"Devices","Audio","General","Hotkeys","Settings","Info"}){string current=name;Click("Nav"+name,()=>Navigate(current));}
   Click("RefreshDevices",()=>{StartInventory();Run(phones.Discover);});Click("Scan",()=>Run(phones.Discover));Click("Sound",()=>Open("ms-settings:sound"));Click("Bluetooth",()=>Open("ms-settings:bluetooth"));Click("OpenSupportLogs",()=>Open(settings.Root));
   U<TextBox>("DeviceSearch").TextChanged+=(s,e)=>FilterDevices();Click("BatteryInfo",()=>{var tip=U<Button>("BatteryInfo").ToolTip as ToolTip;if(tip!=null)tip.IsOpen=!tip.IsOpen;});
   timer.Tick+=(s,e)=>Tick();Window.ContentRendered+=(s,e)=>{if(settings.Test){RunDevelopmentSelfTest();return;}try{monitor.Start();}catch(Exception ex){Error(ex);}UpdateInstaller.ConfirmStartup();string storageNotice=Environment.GetEnvironmentVariable("AIRBRIDGE_STORAGE_NOTICE");if(!String.IsNullOrEmpty(storageNotice)){Notify(storageNotice);Environment.SetEnvironmentVariable("AIRBRIDGE_STORAGE_NOTICE",null);}StartInventory();Run(phones.Discover);if(background)Window.Hide();timer.Start();};
  }
  static void Open(string path){System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true});}
  void ToggleMaximize(){if(Window.WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(Window);else SystemCommands.MaximizeWindow(Window);}
  internal void Show(){Window.Show();Window.WindowState=WindowState.Normal;Window.Activate();}
  internal void Notify(string text){if(text==notice)return;notice=text;noticeUntil=DateTime.UtcNow.AddSeconds(3);U<TextBlock>("Footer").Text=text;var box=U<Border>("StatusNotice");box.Visibility=String.IsNullOrEmpty(text)?Visibility.Collapsed:Visibility.Visible;Motion.Enter(box,200);}
  partial void TickDevelopment();
  partial void RunDevelopmentSelfTest();
  void Tick(){if(exiting)return;try{if(signal!=null&&signal.WaitOne(0))Show();FinishInventory();if(inventoryTask==null&&DateTime.UtcNow>=inventoryAt)StartInventory();if(board.IsVisible)board.SetActivity(dirty?new string[0]:routes.Engine.ActiveEdges());if(!routes.Busy)phones.Tick(routes.Reserved);routes.Tick(inventory);TickDevelopment();if(phoneRevision!=phones.Revision){phoneRevision=phones.Revision;RenderDevices();}
    if(inventory!=null&&DateTime.UtcNow>=lastWearPoll){lastWearPoll=DateTime.UtcNow.AddMilliseconds(250);TickWear(monitor.Snapshot(),DateTime.UtcNow);}
    if(DateTime.UtcNow>=lastPoll){lastPoll=DateTime.UtcNow.AddSeconds(1);PollAudioObservation();if(Window.IsVisible&&page=="Devices")PollDevices();TickDesktop();TickUpdates();TickBatteries();RefreshGraph();UpdateGraphStatus();}
    if(DateTime.UtcNow>=noticeUntil)U<Border>("StatusNotice").Visibility=Visibility.Collapsed;
   }catch(Exception ex){Error(ex);}}
  void StartInventory(){if(settings.Test||inventoryTask!=null)return;inventoryTask=Task.Run(()=>InventoryService.Read());inventoryAt=DateTime.UtcNow.AddSeconds(30);U<TextBlock>("InventoryStatus").Text=L("正在读取设备…","Reading devices…");Motion.Spin(U<Button>("RefreshDevices"),true);}
  void FinishInventory(){if(inventoryTask==null||!inventoryTask.IsCompleted)return;try{inventory=inventoryTask.GetAwaiter().GetResult();audioObservation=inventory.AudioState;inventoryError="";RenderDevices();Motion.Enter(U<StackPanel>("DeviceCards"),220);RefreshGraph();}catch(Exception ex){inventoryError=ex.GetBaseException().Message;Error(ex);}finally{inventoryTask=null;inventoryAt=DateTime.UtcNow.AddSeconds(30);Motion.Spin(U<Button>("RefreshDevices"),false);}}
  readonly Dictionary<FrameworkElement,PageSpring> pageSprings=new Dictionary<FrameworkElement,PageSpring>();
  void AnimatePage(FrameworkElement target){if(settings.Test)return;PageSpring spring;if(!pageSprings.TryGetValue(target,out spring)){spring=new PageSpring(target,target==U<FrameworkElement>("PageHeading"));pageSprings[target]=spring;}spring.Enter();}
  void Navigate(string name){if(name=="Phone")name="Devices";bool changed=page!=name;page=name;U<ScrollViewer>("PageScroll").Visibility=name=="Audio"?Visibility.Collapsed:Visibility.Visible;
   foreach(string p in new[]{"Devices","Phone","Audio","General","Hotkeys","Settings","Info"}){U<FrameworkElement>(p+"Page").Visibility=p==name?Visibility.Visible:Visibility.Collapsed;var b=U<Button>("Nav"+p);b.SetValue(Selector.IsSelectedProperty,p==name);if(p==name)b.SetResourceReference(Control.BackgroundProperty,"Brush_E6EEFC");else b.Background=Brushes.Transparent;}
   U<LiquidIndicator>("NavLiquidIndicator").Select(U<Button>("Nav"+name),!settings.Test);
   string[] ids={"Devices","Audio","General","Hotkeys","Settings","Info"},zh={"音频设备","音频管理","通用设置","快捷键","偏好设置","支持与诊断"},en={"Audio devices","Audio management","General","Hotkeys","Preferences","Support & diagnostics"};int index=Array.IndexOf(ids,name);U<TextBlock>("PageTitle").Text=L(zh[index],en[index]);
   U<TextBlock>("PageSubtitle").Text=name=="Audio"?L("连接输入与输出节点，管理多路声音转发。","Connect input and output nodes to route audio."):name=="Devices"?L("管理蓝牙、USB、显示器、麦克风及系统音频端点。","Manage Bluetooth, USB, displays and audio endpoints."):name=="General"?L("弹窗、托盘和声音提示。","Popups, tray icons and audio notifications."):name=="Hotkeys"?L("在其他应用中也可以操作 AirBridge。","Control AirBridge while using other applications."):L("按你的习惯使用 AirBridge。","Make AirBridge work your way.");
   if(changed){U<ScrollViewer>("PageScroll").ScrollToTop();foreach(var target in new[]{U<FrameworkElement>(name=="Audio"?"AudioPage":"PageScroll"),U<FrameworkElement>("PageHeading")}){target.UpdateLayout();AnimatePage(target);}}
  }
  SidebarMotion sidebarMotion;
  void Sidebar(bool animate){
   bool collapsed=settings.Bool("SidebarCollapsed");
   U<DockPanel>("SidebarDock").Margin=new Thickness(8,20,8,2);
   var toggle=U<Button>("ToggleSidebar");toggle.HorizontalAlignment=HorizontalAlignment.Left;toggle.Width=40;toggle.Margin=new Thickness(0);
   toggle.ToolTip=collapsed?L("展开侧栏","Expand sidebar"):L("收起侧栏","Collapse sidebar");
   System.Windows.Automation.AutomationProperties.SetName(toggle,Convert.ToString(toggle.ToolTip));
   foreach(string name in new[]{"NavDevices","NavAudio","NavGeneral","NavHotkeys","NavSettings","NavInfo","Exit"}){
    var button=U<Button>(name);var content=button.Content as StackPanel;if(content==null)continue;
    var icon=(TextBlock)content.Children[0];var label=(TextBlock)content.Children[1];
    button.ToolTip=label.Text;button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Padding=new Thickness(10,10,10,10);button.ClipToBounds=true;button.Height=46;
    content.Width=132;icon.Width=20;icon.TextAlignment=TextAlignment.Center;icon.Margin=new Thickness(0,0,14,0);
    label.FontSize=14;label.FontWeight=FontWeights.Normal;label.FontFamily=Window.FontFamily;
    TextOptions.SetTextFormattingMode(label,TextFormattingMode.Display);TextOptions.SetTextRenderingMode(label,TextRenderingMode.ClearType);RenderOptions.SetClearTypeHint(label,ClearTypeHint.Enabled);
    label.Visibility=Visibility.Visible;label.Width=98;label.TextWrapping=TextWrapping.NoWrap;label.TextTrimming=TextTrimming.CharacterEllipsis;
   }
   U<TextBlock>("SidebarVersion").Visibility=Visibility.Visible;
   if(sidebarMotion==null)sidebarMotion=new SidebarMotion(U<ColumnDefinition>("SidebarColumn"),opacity=>{
    foreach(string name in new[]{"NavDevices","NavAudio","NavGeneral","NavHotkeys","NavSettings","NavInfo","Exit"}){var content=U<Button>(name).Content as StackPanel;if(content!=null)content.Children[1].Opacity=opacity;}
    U<TextBlock>("SidebarVersion").Opacity=opacity;
    U<FrameworkElement>("PageHeading").Margin=new Thickness(12+16*opacity,0,28,0);
    U<FrameworkElement>("PageBody").Margin=new Thickness(12+16*opacity,8,28,12);
    U<FrameworkElement>("AudioPage").Margin=new Thickness(8+4*opacity,0,24,24);
   });
   sidebarMotion.Set(collapsed,animate&&!settings.Test);
  }
  void Translate(){string[] names={"Devices","Audio","General","Hotkeys","Settings","Info"},zh={"音频设备","音频管理","通用设置","快捷键","偏好设置","支持与诊断"},en={"Audio devices","Audio management","General","Hotkeys","Preferences","Support"},colors={"388BD8","20A68A","6888D9","C28A38","A77AE3","279FAF"};int[] codes={0xE7F6,0xE767,0xE713,0xE765,0xE790,0xE946};for(int i=0;i<names.Length;i++){var content=Glyph(codes[i],L(zh[i],en[i]));((TextBlock)content.Children[0]).Foreground=Brush("#"+colors[i]);U<Button>("Nav"+names[i]).Content=content;}U<Button>("Exit").Content=Glyph(0xE7E8,L("退出","Exit"));((TextBlock)((StackPanel)U<Button>("Exit").Content).Children[0]).SetResourceReference(TextBlock.ForegroundProperty,"Brush_7C8595");Sidebar(false);Navigate(page);
   U<TextBlock>("DeviceIntro").Text=L("设备列表","Devices");U<Button>("RefreshDevices").Content="\uE72C";U<Button>("RefreshDevices").FontFamily=new FontFamily("Segoe Fluent Icons");U<Button>("Scan").Content=L("刷新设备","Refresh devices");U<Button>("Sound").Content=Glyph(0xE767,L("声音设置","Sound settings"));U<Button>("Bluetooth").Content=Glyph(0xE702,L("蓝牙设备设置","Bluetooth settings"));((TextBlock)((StackPanel)U<Button>("Sound").Content).Children[0]).Foreground=Brush("#A78BE8");((TextBlock)((StackPanel)U<Button>("Bluetooth").Content).Children[0]).Foreground=Brush("#6495ED");U<Button>("Sound").Width=U<Button>("Bluetooth").Width=L("zh","en")=="en"?184:160;U<TextBlock>("SystemLinksTitle").Text=L("Windows 设置","Windows settings");U<TextBlock>("SystemLinksHelp").Text=L("管理蓝牙配对和系统声音设备。","Manage Bluetooth pairing and audio devices.");
   var tip=new ToolTip{Content=Label(L("优先显示直读电量；读取失败保留上次成功值并标记过期。系统缓存更新时间可能未知。耳机广播是估计值。","Direct readings are preferred. Failures retain stale values; system caches may have unknown timestamps. Headphone broadcasts are estimates."),12),MaxWidth=350,Placement=PlacementMode.Top};U<Button>("BatteryInfo").ToolTip=tip;
   string[] categories={L("全部节点","All nodes"),L("输入","Inputs"),L("输出","Outputs"),L("中间件","Processors")};for(int i=0;i<4;i++)((ComboBoxItem)U<ComboBox>("NodeCategory").Items[i]).Content=categories[i];((TextBlock)((Grid)U<Button>("ExpandNodeLibrary").Content).Children[1]).Text=L("节点库","Node library");System.Windows.Automation.AutomationProperties.SetHelpText(U<TextBox>("DeviceSearch"),L("搜索设备","Search devices"));System.Windows.Automation.AutomationProperties.SetHelpText(U<TextBox>("NodeSearch"),L("搜索节点…","Search nodes…"));Support();ApplyTheme();RenderDevices();RefreshGraph();}
  void Support(){U<TextBlock>("InfoTitle").Text="AirBridge "+BuildVersion.Value+" · C# / WPF · x64";U<TextBlock>("InfoText").Text=L("原生托管桌面版，不加载 PowerShell，不捆绑或自动安装驱动。\n\n手机独立输入支持效果链及多输出；电脑媒体可通过通用设置中的 VB-CABLE 可选扩展启用效果链和多输出，默认关闭。应用直连保留原生单输出；效果链及多输出可独占 VB-CABLE。一条虚拟线不能同时隔离电脑整体音频和应用，也不能隔离多个应用。后台进程包含1个主程序、每台手机1个采集进程，以及按需启动的1个输出恢复守护，均共用AirBridge.exe；退出时统一清理。双手机短时取流已验证，长时间运行及单台断开对另一台的影响仍待验证。蓝牙取流能力依赖现有驱动。\n\n启动恢复上次成功运行的连线，设备未就绪时等待重试；手动停止取消自动恢复。关闭窗口收起至托盘，退出恢复由本软件接管的输出。\n\n电量失败时保留上次成功值和更新时间。佩戴广播可控制 Windows 媒体暂停与恢复；手机播放控制不保证支持。佩戴检测开关位于已适配耳机的设备设置中；不提供降噪、通透控制。\n\nRTP 为基础 L16/44100/2，没有公网穿透或加密。分享日志前请检查设备标识和本机路径。","Native managed desktop, without PowerShell or bundled/automatic driver installation.\n\nPhone inputs support effects and multiple outputs. The optional VB-CABLE extension enables PC effects and multiple outputs; it is off by default. App effects and fanout can exclusively use VB-CABLE; one cable isolates one app or PC audio, not both. Background processes share AirBridge.exe: one main, one worker per phone and one on-demand recovery watcher, cleaned up on exit. Short dual-phone capture is verified; long runs and cross-device disconnect effects remain unverified. Bluetooth support depends on existing drivers.\n\nStartup resumes the last running graph and waits for devices. Stop cancels resume. Closing hides to tray; Exit restores managed outputs.\n\nFailed battery reads retain stale values. Wear advertisements can control Windows media. Phone playback control is not guaranteed; Wear controls are per supported headset; ANC and transparency controls are not provided.\n\nRTP L16/44100/2 has no Internet traversal or encryption. Review identifiers and paths before sharing logs.");U<Button>("OpenSupportLogs").Content=L("打开日志与配置目录","Open logs and settings");U<TextBlock>("Diagnostics").Text=settings.Root+"\n"+monitor.State+" "+monitor.Error+"\n"+inventoryError+"\n"+wearPlayback.Status;}
  public void Shutdown(){Exit();}
  async void Exit(){if(exiting)return;exiting=true;if(updateCancellation!=null)updateCancellation.Cancel();var elapsed=System.Diagnostics.Stopwatch.StartNew();Window.IsEnabled=false;Window.Hide();if(sidebarMotion!=null)sidebarMotion.Dispose();wearPlayback.Stop();timer.Stop();windowWorkArea.Dispose();foreach(var spring in pageSprings.Values)spring.Dispose();try{StopDesktop();}catch(Exception ex){Log(ex.ToString());}
   try{await routes.ShutdownAsync();}catch(Exception ex){Log(ex.ToString());}Log("Shutdown routes: "+elapsed.ElapsedMilliseconds+" ms");
   try{await Task.Run(()=>phones.Dispose());}catch(Exception ex){Log(ex.ToString());}try{monitor.Dispose();}catch{}try{await Task.Run(()=>PhoneBattery.Close());}catch{}Log("Shutdown complete: "+elapsed.ElapsedMilliseconds+" ms");Window.Close();Application.Current.Shutdown();}
#if AIRBRIDGE_DEVELOPMENT
  async Task SelfTest(){try{await virtualCableScan;await Task.Delay(100);foreach(string p in new[]{"Devices","Audio","General","Hotkeys","Settings","Info"}){Navigate(p);Window.UpdateLayout();if(!U<FrameworkElement>(p+"Page").IsVisible)throw new Exception("Navigation failed "+p);}await MigrationChecks();if(routes.Engine.Snapshot().Length!=0)throw new Exception("Self-test opened audio");if(AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="System.Management.Automation"))throw new Exception("PowerShell runtime loaded");File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test-result.txt"),"PASS: settings compatibility and backup, stopped intent, ignore identity, graph add/delete, library layout, battery cache retention, tray actions, language switching, minimize/hide/restore, WPF pages, no audio started, no PowerShell runtime");}catch(Exception ex){TestFailed=true;File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test-error.txt"),ex.ToString());}finally{Exit();}}
#endif
 }
}

