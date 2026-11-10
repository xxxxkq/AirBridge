using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AirBridge.Desktop;
class SettingsCardTests {
 static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject {foreach(var child in LogicalTreeHelper.GetChildren(root)){var d=child as DependencyObject;if(d==null)continue;var typed=d as T;if(typed!=null)yield return typed;foreach(var nested in Descendants<T>(d))yield return nested;}}
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 [STAThread]static int Main(){MainView view=null;try{var settings=new SettingsStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"layout-fixture"),true);settings.Set("Language","zh");view=new MainView(settings,null,false);
   typeof(MainView).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,new object[]{"Settings"});
   var page=view.U<StackPanel>("SettingsPage");var cards=page.Children.OfType<Border>().ToArray();Func<string,Border> card=name=>cards.Single(c=>Descendants<TextBlock>(c).Any(t=>t.Text==name));
   var update=card("软件更新");var storage=card("数据保存位置");
   Check(!Descendants<Button>(update).Any(b=>Convert.ToString(b.Content).Contains("目录")),"update card has no duplicate directory buttons");Check(!Descendants<ComboBox>(update).Any(),"update policy uses compact more menu");Check(Descendants<Button>(storage).Count(b=>Convert.ToString(b.Content)=="打开目录")==1,"one data folder entry");Check(Descendants<StackPanel>(storage).Any(p=>p.Visibility==Visibility.Collapsed&&Descendants<TextBox>(p).Any()),"migration editor collapsed by default");
   Check(Descendants<ComboBox>(storage).Single().SelectedIndex==1&&Descendants<TextBox>(storage).Single().Text==settings.Root,"initial selection matches the actual custom fixture data location");
   var content=(FrameworkElement)view.Window.Content;view.Window.Content=null;var host=new Border{Child=content,Resources=view.Window.Resources};host.Measure(new Size(1080,820));host.Arrange(new Rect(0,0,1080,820));host.UpdateLayout();
   Check(update.ActualHeight>0&&update.ActualHeight<=120,"normal update card height <=120 DIP (actual "+update.ActualHeight+")");Check(storage.ActualHeight>0&&storage.ActualHeight<=120,"normal data card height <=120 DIP (actual "+storage.ActualHeight+")");
   var flags=BindingFlags.Instance|BindingFlags.NonPublic;Func<string,object> field=name=>typeof(MainView).GetField(name,flags).GetValue(view);Action refresh=()=>typeof(MainView).GetMethod("RefreshUpdateControls",flags).Invoke(view,null);Action<int> policy=choice=>typeof(MainView).GetMethod("SkipUpdate",flags).Invoke(view,new object[]{choice});
   var badge=(TextBlock)field("updateBadgeText");Check(badge.Text=="手动检查","badge reflects default manual-only policy");Check(!Descendants<CheckBox>(update).Any(),"automatic switch is in the menu, not the header");
   policy(2);Check(badge.Text=="今日跳过"&&((TextBlock)field("updateStatus")).Text.Contains("明日"),"today skip shows amber-policy badge and short expiry hint");
   var menu=(ContextMenu)typeof(MainView).GetMethod("BuildUpdateMenu",flags).Invoke(view,null);Check(menu.Placement==PlacementMode.Custom&&!menu.IsOpen&&menu.CustomPopupPlacementCallback!=null,"menu has bounded custom placement without opening UI");Check(menu.Items.OfType<MenuItem>().Any(i=>Descendants<CheckBox>(i).Any()),"automatic checking remains available in menu");
   var target=(Button)field("updateLater");var origin=target.TranslatePoint(new Point(0,0),view.Window);var placement=menu.CustomPopupPlacementCallback(new Size(228,360),new Size(30,34),new Point())[0].Point;Check(origin.X+placement.X>=8&&origin.X+placement.X+228<=1080-8,"menu is aligned within window horizontal bounds");
   policy(0);Check(badge.Text=="自动提醒"&&settings.Bool("UpdateChecksEnabled")&&settings.Text("UpdateSkipDate")=="","normal reminders clear skip policy and enable automatic checks");policy(1);Check(badge.Text=="本次跳过","session skip badge persists independently of check message");policy(4);Check(badge.Text=="禁止更新"&&!((Button)field("updateCheck")).IsEnabled,"forbidden policy still blocks manual checking");
   policy(0);settings.Set("UpdateIgnoredTag","v3.0.66");refresh();Check(badge.Text=="已忽略此版本"&&Convert.ToString(((Border)field("updateBadge")).ToolTip).Contains("v3.0.66"),"ignored release is displayed with exact tag in tooltip");
   settings.Set("UpdateIgnoredTag","");settings.Set("UpdateSkipDate",DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd"));refresh();Check(badge.Text=="自动提醒","expired day skip does not leave a stale badge");
   Check(!view.Window.IsVisible,"layout tested without showing a window");Console.WriteLine("PASS settings layout; in-memory WPF only, no UI clicks or playback");return 0;
  }catch(Exception ex){Console.WriteLine("FAIL "+ex);return 1;}finally{if(view!=null){var coordinator=typeof(MainView).GetField("routes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);Task.Run(async()=>await (Task)coordinator.GetType().GetMethod("ShutdownAsync").Invoke(coordinator,null)).GetAwaiter().GetResult();}}
 }
}
