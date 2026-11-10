using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Ellipse=System.Windows.Shapes.Ellipse;
using System.Threading;
using System.Threading.Tasks;
namespace AirBridge.Desktop {
 internal sealed partial class MainView {
  bool updateSessionSkipped,updateBusy,checkingAutomatically;UpdateRelease updateRelease;string updateDownload="",updateMessage="",updateNotified="";CancellationTokenSource updateCancellation;DateTime nextUpdateCheck=DateTime.UtcNow.AddSeconds(15);
  TextBlock updateStatus,updateBadgeText;Border updateBadge;Ellipse updateBadgeDot;Button updateCheck,updateAction,updateLater;
  UpdatePolicy UpdatePolicyNow(){return new UpdatePolicy{AutoCheck=settings.Bool("UpdateChecksEnabled"),Forbidden=settings.Bool("UpdatesDisabled"),SessionSkipped=updateSessionSkipped,SkipDate=settings.Text("UpdateSkipDate"),IgnoredTag=settings.Text("UpdateIgnoredTag")};}
  void BuildUpdatePreference(Panel parent){
   var grid=new Grid();grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   var heading=new WrapPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,16,0)};var title=Label(L("软件更新","Software updates"),14);title.FontWeight=FontWeights.SemiBold;title.VerticalAlignment=VerticalAlignment.Center;heading.Children.Add(title);
   var version=Label("v"+BuildVersion.Value,12,"8A94A5");version.Margin=new Thickness(10,0,12,0);version.VerticalAlignment=VerticalAlignment.Center;heading.Children.Add(version);
   updateBadgeText=Label("",11);updateBadgeText.VerticalAlignment=VerticalAlignment.Center;updateBadgeDot=new Ellipse{Width=5,Height=5,Margin=new Thickness(0,0,6,0),VerticalAlignment=VerticalAlignment.Center};var badgeContent=new StackPanel{Orientation=Orientation.Horizontal};badgeContent.Children.Add(updateBadgeDot);badgeContent.Children.Add(updateBadgeText);updateBadge=new Border{CornerRadius=new CornerRadius(10),Padding=new Thickness(8,3,8,3),BorderThickness=new Thickness(1),VerticalAlignment=VerticalAlignment.Center,Child=badgeContent};heading.Children.Add(updateBadge);grid.Children.Add(heading);
   var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(actions,1);
   updateCheck=Button("",()=>Run(()=>CheckUpdates(false)));var checkContent=Glyph(0xE72C,L("检查更新","Check updates"));checkContent.Children[0].SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,7,0));((TextBlock)checkContent.Children[0]).SetResourceReference(TextBlock.ForegroundProperty,"Brush_7C8595");updateCheck.Content=checkContent;updateCheck.Height=34;updateCheck.Padding=new Thickness(12,0,12,0);updateCheck.Margin=new Thickness(0,0,8,0);actions.Children.Add(updateCheck);
   updateAction=Button("",()=>Run(DownloadOrInstallUpdate));updateAction.Height=34;updateAction.Padding=new Thickness(12,0,12,0);updateAction.Margin=new Thickness(0,0,8,0);actions.Children.Add(updateAction);
   updateLater=Button("",()=>{});updateLater.Style=(Style)Window.FindResource("GhostIcon");updateLater.Width=30;updateLater.Height=34;updateLater.Padding=new Thickness(0);updateLater.FontFamily=new FontFamily("Segoe Fluent Icons");updateLater.Content=Char.ConvertFromUtf32(0xE712);updateLater.SetResourceReference(Control.ForegroundProperty,"Brush_7C8595");updateLater.ToolTip=L("更新提醒设置","Update reminder settings");updateLater.Click+=(s,e)=>{var menu=BuildUpdateMenu();menu.IsOpen=true;};actions.Children.Add(updateLater);grid.Children.Add(actions);
   updateStatus=Label("",12,"697386");updateStatus.Margin=new Thickness(0,8,0,0);Grid.SetRow(updateStatus,1);Grid.SetColumnSpan(updateStatus,2);grid.Children.Add(updateStatus);
   parent.Children.Add(new Border{Style=(Style)Window.FindResource("Card"),Padding=new Thickness(22,18,22,18),Child=grid});RefreshUpdateControls();
  }
  string UpdateReminderMode(){var policy=UpdatePolicyNow();return policy.Forbidden?"disabled":policy.SessionSkipped?"session":policy.SkipDate==DateTime.Now.ToString("yyyy-MM-dd")?"today":!String.IsNullOrEmpty(policy.IgnoredTag)?"ignored":policy.AutoCheck?"auto":"off";}
  ContextMenu BuildUpdateMenu(){
   var menu=new ContextMenu{PlacementTarget=updateLater,Placement=PlacementMode.Custom,MinWidth=228,FontFamily=Window.FontFamily,FontSize=12,Style=(Style)Window.FindResource("UpdateReminderMenu")};menu.Resources.MergedDictionaries.Add(Window.Resources);
   menu.CustomPopupPlacementCallback=(popup,target,offset)=>{Point anchor=updateLater.TranslatePoint(new Point(0,0),Window);double x=Math.Max(8-anchor.X,target.Width-popup.Width);double y=target.Height+6;if(anchor.Y+y+popup.Height>Window.ActualHeight-8)y=-popup.Height-6;return new[]{new CustomPopupPlacement(new Point(x,Math.Max(8-anchor.Y,y)),PopupPrimaryAxis.Horizontal)};};
   menu.Items.Add(new MenuItem{Header=L("更新提醒设置","Update reminders"),IsEnabled=false,Style=(Style)Window.FindResource("UpdateReminderItem")});
   var autoRow=new Grid();autoRow.ColumnDefinitions.Add(new ColumnDefinition());autoRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var autoLabel=Label(L("自动检查","Automatic checks"),12);autoLabel.VerticalAlignment=VerticalAlignment.Center;autoRow.Children.Add(autoLabel);var auto=new CheckBox{IsChecked=settings.Bool("UpdateChecksEnabled"),Width=36,Height=20,VerticalAlignment=VerticalAlignment.Center,ToolTip=L("每12小时检查，不自动下载安装","Check every 12 hours; never install automatically")};Grid.SetColumn(auto,1);autoRow.Children.Add(auto);auto.Click+=(s,e)=>{SetAutomaticUpdateChecks(auto.IsChecked==true);menu.IsOpen=false;};menu.Items.Add(new MenuItem{Header=autoRow,StaysOpenOnClick=true,Style=(Style)Window.FindResource("UpdateReminderItem")});menu.Items.Add(new Separator());
   string mode=UpdateReminderMode();string[] labels={L("正常自动提醒","Normal reminders"),L("本次跳过","Skip this session"),L("今日跳过提醒","Skip today"),L("忽略本次更新","Ignore this release"),L("禁止更新","Disable updates")};string[] modes={"auto","session","today","ignored","disabled"};
   for(int i=0;i<labels.Length;i++){int choice=i;var header=new Grid();header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(18)});var name=Label(labels[i],12);name.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(name);var mark=new TextBlock{Text="\uE73E",FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=12,VerticalAlignment=VerticalAlignment.Center,Visibility=mode==modes[i]?Visibility.Visible:Visibility.Hidden};mark.SetResourceReference(TextBlock.ForegroundProperty,"Brush_2868EB");Grid.SetColumn(mark,1);header.Children.Add(mark);var item=new MenuItem{Header=header,IsEnabled=choice!=3||updateRelease!=null,Style=(Style)Window.FindResource("UpdateReminderItem")};if(mode==modes[i])item.SetResourceReference(Control.BackgroundProperty,"Brush_E8EFFF");if(choice==3)item.ToolTip=updateRelease==null?L("发现新版本后可用","Available when an update is found"):updateRelease.Tag;item.Click+=(sender,args)=>SkipUpdate(choice);menu.Items.Add(item);}
   menu.Items.Add(new Separator());var releases=new MenuItem{Header=Glyph(0xE8A7,L("查看更新日志","View release notes")),Style=(Style)Window.FindResource("UpdateReminderItem")};releases.Click+=(s,e)=>Open(UpdateService.Page);menu.Items.Add(releases);return menu;
  }
  void SetAutomaticUpdateChecks(bool on){settings.Set("UpdateChecksEnabled",on);if(on){settings.Set("UpdatesDisabled",false);settings.Set("UpdateLastCheckUtc","");nextUpdateCheck=DateTime.UtcNow;}else if(checkingAutomatically&&updateCancellation!=null)updateCancellation.Cancel();settings.Save();updateMessage="";RefreshUpdateControls();}
  void RefreshUpdateControls(){if(updateStatus==null)return;var policy=UpdatePolicyNow();string mode=UpdateReminderMode();string badge=mode=="disabled"?L("禁止更新","Disabled"):mode=="session"?L("本次跳过","Session skipped"):mode=="today"?L("今日跳过","Skipped today"):mode=="ignored"?L("已忽略此版本","Release ignored"):mode=="auto"?L("自动提醒","Auto reminders"):L("手动检查","Manual checks");
   string color=mode=="today"||mode=="session"?"#B77916":mode=="auto"?"#4285DC":"#7C8595";var accent=Brush(color);updateBadgeText.Text=badge;updateBadgeText.Foreground=accent;updateBadgeDot.Fill=accent;var tint=((SolidColorBrush)accent).Color;updateBadge.Background=new SolidColorBrush(Color.FromArgb(18,tint.R,tint.G,tint.B));updateBadge.BorderBrush=new SolidColorBrush(Color.FromArgb(48,tint.R,tint.G,tint.B));
   string hint=mode=="disabled"?L("已停止检查与下载，可在菜单中恢复。","Checks and downloads disabled; restore from the menu."):mode=="session"?L("本次不再自动提醒，重启后恢复。","Reminders resume next time AirBridge starts."):mode=="today"?L("今日暂停自动提醒，明日恢复。","Automatic reminders resume tomorrow."):mode=="ignored"?L("已忽略该版本，仍可提醒后续更新。","This release is ignored; future updates can still notify."):policy.AutoCheck?L("每12小时检查，不自动下载安装。","Checks every 12 hours; never downloads or installs automatically."):L("仅手动检查，不自动下载安装。","Manual checks only; never downloads or installs automatically.");
   updateBadge.ToolTip=hint+(mode=="ignored"?" ("+policy.IgnoredTag+")":"");updateStatus.Text=updateMessage!=""?updateMessage:hint;
   updateCheck.IsEnabled=!updateBusy&&!policy.Forbidden;updateLater.IsEnabled=!updateBusy||updateDownload=="";
   updateAction.Visibility=updateRelease==null?Visibility.Collapsed:Visibility.Visible;updateAction.IsEnabled=!updateBusy&&!policy.Forbidden;
   updateAction.Content=updateDownload==""?L("下载更新","Download update"):UpdateInstaller.Packaged?L("更新并重启","Update and restart"):L("打开下载目录","Open download folder");updateAction.ToolTip=updateRelease==null?null:updateRelease.Notes;
  }
  string UpdateErrorMessage(Exception ex){switch(UpdateService.FailureCode(ex)){case "repository-access":return L("更新仓库未公开或无访问权限","Update repository is private or inaccessible");case "tls":return L("连接失败：TLS握手失败","Connection failed: TLS handshake");case "certificate":return L("连接失败：证书验证失败","Connection failed: certificate validation");case "proxy":return L("连接失败：代理配置不可用","Connection failed: proxy unavailable");case "dns":return L("连接失败：无法解析更新服务器","Connection failed: DNS");case "timeout":return L("检查超时，请稍后重试","Check timed out; retry later");case "no-release":return L("暂无可访问的正式发布","No accessible release yet");case "rate-limit":return L("GitHub请求受限，请稍后重试","GitHub rate limit; retry later");case "access":return L("无权访问更新，请检查仓库可见性","Update access denied; check repository visibility");case "release-format":return L("发布包信息不完整，暂不可自动更新","Release metadata incomplete");default:return L("连接失败，请检查网络或系统代理","Connection failed; check network or proxy");}}
  void SkipUpdate(int choice){if(choice==3&&updateRelease==null){updateMessage=L("尚无可忽略的新版本","No new release to ignore");RefreshUpdateControls();return;}
   if(updateCancellation!=null)updateCancellation.Cancel();updateSessionSkipped=choice==1;
   if(choice==0){settings.Set("UpdatesDisabled",false);settings.Set("UpdateChecksEnabled",true);settings.Set("UpdateSkipDate","");settings.Set("UpdateIgnoredTag","");settings.Set("UpdateLastCheckUtc","");nextUpdateCheck=DateTime.UtcNow;}
   if(choice==2)settings.Set("UpdateSkipDate",DateTime.Now.ToString("yyyy-MM-dd"));
   if(choice==3)settings.Set("UpdateIgnoredTag",updateRelease.Tag);
   if(choice==4){settings.Set("UpdatesDisabled",true);settings.Set("UpdateChecksEnabled",false);}
   settings.Save();updateRelease=null;updateDownload="";updateMessage="";RefreshUpdateControls();
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
