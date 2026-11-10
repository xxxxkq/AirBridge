using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Threading;
using System.Threading.Tasks;
namespace AirBridge.Desktop {
 internal sealed partial class MainView {
  bool updateSessionSkipped,updateBusy,checkingAutomatically;UpdateRelease updateRelease;string updateDownload="",updateMessage="",updateNotified="";CancellationTokenSource updateCancellation;DateTime nextUpdateCheck=DateTime.UtcNow.AddSeconds(15);
  TextBlock updateStatus;CheckBox updateAuto;Button updateCheck,updateAction;Button updateLater;
  UpdatePolicy UpdatePolicyNow(){return new UpdatePolicy{AutoCheck=settings.Bool("UpdateChecksEnabled"),Forbidden=settings.Bool("UpdatesDisabled"),SessionSkipped=updateSessionSkipped,SkipDate=settings.Text("UpdateSkipDate"),IgnoredTag=settings.Text("UpdateIgnoredTag")};}
  void BuildUpdatePreference(Panel parent){
   var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,20,0)};var heading=new StackPanel{Orientation=Orientation.Horizontal};var title=Label(L("软件更新","Software updates"),14);title.FontWeight=FontWeights.SemiBold;heading.Children.Add(title);var version=Label("v"+BuildVersion.Value,12,"8A94A5");version.Margin=new Thickness(10,0,0,0);version.VerticalAlignment=VerticalAlignment.Center;heading.Children.Add(version);text.Children.Add(heading);updateStatus=Label("",12,"697386");updateStatus.Margin=new Thickness(0,5,0,0);text.Children.Add(updateStatus);grid.Children.Add(text);
   var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(actions,1);
   updateCheck=Button(L("检查更新","Check updates"),()=>Run(()=>CheckUpdates(false)));updateCheck.Height=36;updateCheck.Padding=new Thickness(12,0,12,0);updateCheck.Margin=new Thickness(0,0,8,0);actions.Children.Add(updateCheck);
   updateAction=Button("",()=>Run(DownloadOrInstallUpdate));updateAction.Height=36;updateAction.Padding=new Thickness(12,0,12,0);updateAction.Margin=new Thickness(0,0,12,0);actions.Children.Add(updateAction);
   updateAuto=new CheckBox{IsChecked=settings.Bool("UpdateChecksEnabled"),Width=36,Height=20,ClipToBounds=true,HorizontalContentAlignment=HorizontalAlignment.Left,Margin=new Thickness(4,0,0,0),VerticalAlignment=VerticalAlignment.Center,ToolTip=L("自动检查：每12小时检查，不自动下载安装。","Automatic checks every 12 hours; downloads and installation need confirmation.")};actions.Children.Add(updateAuto);
   updateLater=Button("",()=>{});updateLater.Style=(Style)Window.FindResource("GhostIcon");updateLater.Width=28;updateLater.Height=32;updateLater.Padding=new Thickness(0);updateLater.Margin=new Thickness(4,0,0,0);updateLater.FontFamily=new System.Windows.Media.FontFamily("Segoe Fluent Icons");updateLater.Content=Char.ConvertFromUtf32(0xE70D);updateLater.ToolTip=L("更新选项","Update options");
   updateLater.Click+=(s,e)=>{var menu=new ContextMenu{PlacementTarget=updateLater,MinWidth=172,Padding=new Thickness(4),FontFamily=Window.FontFamily,FontSize=13};menu.Resources.MergedDictionaries.Add(Window.Resources);menu.SetResourceReference(Control.BackgroundProperty,"Brush_FFFFFF");menu.SetResourceReference(Control.ForegroundProperty,"Brush_202634");menu.SetResourceReference(Control.BorderBrushProperty,"Brush_E8EBF0");string[] labels={L("本次跳过","Skip this session"),L("今日跳过","Skip today"),L("忽略本次更新","Ignore this release"),L("禁止更新","Disable updates")};for(int i=0;i<labels.Length;i++){int choice=i+1;var item=new MenuItem{Header=labels[i],Padding=new Thickness(10,7,10,7),IsEnabled=choice!=3||updateRelease!=null};item.Click+=(sender,args)=>SkipUpdate(choice);menu.Items.Add(item);}menu.Items.Add(new Separator());var releases=new MenuItem{Header=L("查看发布页","View releases"),Padding=new Thickness(10,7,10,7)};releases.Click+=(sender,args)=>Open(UpdateService.Page);menu.Items.Add(releases);menu.IsOpen=true;};actions.Children.Add(updateLater);grid.Children.Add(actions);
   parent.Children.Add(new Border{Style=(Style)Window.FindResource("Card"),Padding=new Thickness(22,18,22,18),Child=grid});
   updateAuto.Click+=(s,e)=>{bool on=updateAuto.IsChecked==true;settings.Set("UpdateChecksEnabled",on);if(on){settings.Set("UpdatesDisabled",false);settings.Set("UpdateLastCheckUtc","");updateSessionSkipped=false;nextUpdateCheck=DateTime.UtcNow;}else if(checkingAutomatically&&updateCancellation!=null)updateCancellation.Cancel();settings.Save();updateMessage=on?L("自动检查已开启","Automatic checks on"):L("自动检查已关闭","Automatic checks off");RefreshUpdateControls();};RefreshUpdateControls();
  }
  void RefreshUpdateControls(){if(updateStatus==null)return;var policy=UpdatePolicyNow();if(updateAuto!=null)updateAuto.IsChecked=policy.AutoCheck;
   updateStatus.Text=policy.Forbidden?L("更新已禁止","Updates disabled"):updateMessage!=""?updateMessage:policy.AutoCheck?L("自动检查已开启","Automatic checks on"):L("自动检查已关闭","Automatic checks off");
   updateCheck.IsEnabled=!updateBusy&&!policy.Forbidden;updateLater.IsEnabled=!updateBusy||updateDownload=="";
   updateAction.Visibility=updateRelease==null?Visibility.Collapsed:Visibility.Visible;updateAction.IsEnabled=!updateBusy&&!policy.Forbidden;
   updateAction.Content=updateDownload==""?L("下载更新","Download update"):UpdateInstaller.Packaged?L("更新并重启","Update and restart"):L("打开下载目录","Open download folder");
   updateAction.ToolTip=updateRelease==null?null:updateRelease.Notes;
  }
  string UpdateErrorMessage(Exception ex){switch(UpdateService.FailureCode(ex)){case "repository-access":return L("更新仓库未公开或无访问权限","Update repository is private or inaccessible");case "tls":return L("连接失败：TLS握手失败","Connection failed: TLS handshake");case "certificate":return L("连接失败：证书验证失败","Connection failed: certificate validation");case "proxy":return L("连接失败：代理配置不可用","Connection failed: proxy unavailable");case "dns":return L("连接失败：无法解析更新服务器","Connection failed: DNS");case "timeout":return L("检查超时，请稍后重试","Check timed out; retry later");case "no-release":return L("暂无可访问的正式发布","No accessible release yet");case "rate-limit":return L("GitHub请求受限，请稍后重试","GitHub rate limit; retry later");case "access":return L("无权访问更新，请检查仓库可见性","Update access denied; check repository visibility");case "release-format":return L("发布包信息不完整，暂不可自动更新","Release metadata incomplete");default:return L("连接失败，请检查网络或系统代理","Connection failed; check network or proxy");}}
  void SkipUpdate(int choice){if(choice==3&&updateRelease==null){updateMessage=L("尚无可忽略的新版本","No new release to ignore");RefreshUpdateControls();return;}
   if(updateCancellation!=null)updateCancellation.Cancel();updateSessionSkipped=choice==1;
   if(choice==2)settings.Set("UpdateSkipDate",DateTime.Now.ToString("yyyy-MM-dd"));
   if(choice==3)settings.Set("UpdateIgnoredTag",updateRelease.Tag);
   if(choice==4){settings.Set("UpdatesDisabled",true);settings.Set("UpdateChecksEnabled",false);}
   settings.Save();updateRelease=null;updateDownload="";updateMessage=choice==1?L("本次运行已跳过","Skipped for this session"):choice==2?L("今日已跳过，明日恢复检查","Skipped today; checks resume tomorrow"):choice==3?L("已忽略该版本，新版本仍会提醒","Release ignored; future releases can notify"):"";RefreshUpdateControls();
  }
  void TickUpdates(){if(settings.Test||exiting||updateBusy||DateTime.UtcNow<nextUpdateCheck||!UpdatePolicyNow().CanAutomaticallyCheck(DateTime.Now))return;
   DateTime checkedAt;if(DateTime.TryParse(settings.Text("UpdateLastCheckUtc"),null,System.Globalization.DateTimeStyles.RoundtripKind,out checkedAt)&&DateTime.UtcNow-checkedAt.ToUniversalTime()<TimeSpan.FromHours(12))return;
   nextUpdateCheck=DateTime.UtcNow.AddMinutes(30);Run(()=>CheckUpdates(true));
  }
  async Task CheckUpdates(bool automatic){if(settings.Test||exiting||updateBusy||UpdatePolicyNow().Forbidden||automatic&&!UpdatePolicyNow().CanAutomaticallyCheck(DateTime.Now))return;
   updateBusy=true;checkingAutomatically=automatic;var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(45));updateCancellation=cancellation;updateMessage=L("正在检查…","Checking…");updateStatus.ToolTip=null;RefreshUpdateControls();
   try{var found=await UpdateService.Check(BuildVersion.Value,cancellation.Token);if(exiting||cancellation.IsCancellationRequested||UpdatePolicyNow().Forbidden||automatic&&!UpdatePolicyNow().CanAutomaticallyCheck(DateTime.Now))return;
    settings.Set("UpdateLastCheckUtc",DateTime.UtcNow.ToString("o"));settings.Save();nextUpdateCheck=DateTime.UtcNow.AddHours(12);
    if(automatic&&found!=null&&!UpdatePolicyNow().CanNotify(found.Tag)){updateMessage=L("已忽略该版本","This release is ignored");return;}
    updateRelease=found;updateDownload="";updateMessage=found==null?L("已是最新正式版","Up to date"):L("发现新版本 ","New version ")+found.Tag;
    if(automatic&&found!=null&&updateNotified!=found.Tag){updateNotified=found.Tag;Notify(updateMessage+L("，可在偏好设置中查看。", "; see Preferences."));if(!Window.IsVisible)Popup("AirBridge",updateMessage+L("，请打开偏好设置。", "; open Preferences."));}
   }catch(Exception ex){if(!cancellation.IsCancellationRequested){updateMessage=UpdateErrorMessage(ex);updateStatus.ToolTip=ex.GetBaseException().Message;var web=ex as System.Net.WebException;Log("Update check: kind="+UpdateService.FailureCode(ex)+" protocol="+System.Net.ServicePointManager.SecurityProtocol+" status="+(web==null?ex.GetType().Name:web.Status.ToString())+" "+ex.GetBaseException().Message);if(web!=null&&web.Response!=null)web.Response.Dispose();nextUpdateCheck=DateTime.UtcNow.AddHours(1);}}
   finally{if(updateCancellation==cancellation)updateCancellation=null;cancellation.Dispose();updateBusy=false;checkingAutomatically=false;RefreshUpdateControls();}
  }
  async Task DownloadOrInstallUpdate(){if(exiting||updateBusy||updateRelease==null||UpdatePolicyNow().Forbidden)return;var release=updateRelease;
   if(updateDownload!=""){if(!UpdateInstaller.Packaged){Open(Path.GetDirectoryName(updateDownload));return;}if(routes.Busy){Notify(L("音频操作进行中，请稍后更新。","Audio operation in progress; try again shortly."));return;}
    updateBusy=true;RefreshUpdateControls();try{await Task.Run(()=>UpdateInstaller.Prepare(release,updateDownload,settings.Root));Exit();}catch(Exception ex){Error(ex);}finally{updateBusy=false;RefreshUpdateControls();}return;}
   updateBusy=true;var cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(5));updateCancellation=cancellation;updateMessage=L("正在下载并校验…","Downloading and verifying…");RefreshUpdateControls();
   try{string file=await UpdateService.Download(release,settings.Root,cancellation.Token);if(cancellation.IsCancellationRequested||UpdatePolicyNow().Forbidden||updateRelease!=release){if(File.Exists(file))File.Delete(file);return;}updateDownload=file;updateMessage=L("下载已校验；点击更新会短暂中断音频并重启。","Download verified. Installing will interrupt audio and restart AirBridge.");}
   catch(Exception ex){if(!cancellation.IsCancellationRequested){updateMessage=L("下载失败，当前版本未改变。","Download failed; current version unchanged.");Log("Update download: "+ex.GetBaseException().Message);}}
   finally{if(updateCancellation==cancellation)updateCancellation=null;cancellation.Dispose();updateBusy=false;RefreshUpdateControls();}
  }
 }
}
