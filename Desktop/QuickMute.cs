using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace AirBridge.Desktop {
 internal sealed partial class MainView {
  readonly List<Action> muteRefreshers=new List<Action>();
  Button QuickMute(Func<bool> read,Action<bool> write,bool microphone){
   var icon=new TextBlock{FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=17,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
   var content=new Grid{Width=20,Height=20};content.Children.Add(icon);
   var slash=new System.Windows.Shapes.Path{Data=Geometry.Parse("M 3,17 L 17,3"),StrokeThickness=1.8,Stroke=Brush("#D65464"),IsHitTestVisible=false};content.Children.Add(slash);
   var button=new Button{Content=content,Width=34,Height=34,Padding=new Thickness(0),Margin=new Thickness(10,0,0,0),Background=Brushes.Transparent};
   Action refresh=()=>{try{bool muted=read();button.IsEnabled=true;icon.Text=Char.ConvertFromUtf32(microphone?0xE720:muted?0xE74F:0xE767);icon.Foreground=Brush(muted?"#D65464":microphone?"#36A79A":"#5796E5");slash.Visibility=microphone&&muted?Visibility.Visible:Visibility.Collapsed;button.ToolTip=microphone?(muted?L("取消闭麦","Unmute microphone"):L("快速闭麦","Mute microphone")):(muted?L("取消静音","Unmute"):L("快速静音","Mute"));System.Windows.Automation.AutomationProperties.SetName(button,Convert.ToString(button.ToolTip));}catch{button.IsEnabled=false;button.ToolTip=L("音频端点暂不可用","Audio endpoint unavailable");}};
   button.Click+=(s,e)=>{e.Handled=true;try{write(!read());}catch(Exception ex){Error(ex);}refresh();PollDevices();};
   muteRefreshers.Add(refresh);refresh();return button;
  }
 }
}
