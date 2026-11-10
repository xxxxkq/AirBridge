using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using AirBridge.Desktop;
class SettingsCardTests {
 static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject {foreach(var child in LogicalTreeHelper.GetChildren(root)){var d=child as DependencyObject;if(d==null)continue;var typed=d as T;if(typed!=null)yield return typed;foreach(var nested in Descendants<T>(d))yield return nested;}}
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 [STAThread]static int Main(){MainView view=null;try{var settings=new SettingsStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"layout-fixture"),true);settings.Set("Language","zh");view=new MainView(settings,null,false);
   typeof(MainView).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,new object[]{"Settings"});
   var page=view.U<StackPanel>("SettingsPage");var cards=page.Children.OfType<Border>().ToArray();Func<string,Border> card=name=>cards.Single(c=>Descendants<TextBlock>(c).Any(t=>t.Text==name));
   var update=card("软件更新");var storage=card("数据保存位置");
   Check(!Descendants<Button>(update).Any(b=>Convert.ToString(b.Content).Contains("目录")),"update card has no duplicate directory buttons");Check(!Descendants<ComboBox>(update).Any(),"update policy uses compact arrow menu");Check(Descendants<Button>(storage).Count(b=>Convert.ToString(b.Content)=="打开目录")==1,"one data folder entry");Check(Descendants<StackPanel>(storage).Any(p=>p.Visibility==Visibility.Collapsed&&Descendants<TextBox>(p).Any()),"migration editor collapsed by default");
   Check(Descendants<ComboBox>(storage).Single().SelectedIndex==1&&Descendants<TextBox>(storage).Single().Text==settings.Root,"initial selection matches the actual custom fixture data location");
   var content=(FrameworkElement)view.Window.Content;view.Window.Content=null;var host=new Border{Child=content,Resources=view.Window.Resources};host.Measure(new Size(1080,820));host.Arrange(new Rect(0,0,1080,820));host.UpdateLayout();
   Check(update.ActualHeight>0&&update.ActualHeight<=120,"normal update card height <=120 DIP (actual "+update.ActualHeight+")");Check(storage.ActualHeight>0&&storage.ActualHeight<=120,"normal data card height <=120 DIP (actual "+storage.ActualHeight+")");
   Check(!view.Window.IsVisible,"layout tested without showing a window");Console.WriteLine("PASS settings layout; in-memory WPF only, no UI clicks or playback");return 0;
  }catch(Exception ex){Console.WriteLine("FAIL "+ex);return 1;}finally{if(view!=null){var coordinator=typeof(MainView).GetField("routes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);Task.Run(async()=>await (Task)coordinator.GetType().GetMethod("ShutdownAsync").Invoke(coordinator,null)).GetAwaiter().GetResult();}}
 }
}
