using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using AirBridge.Routing;
namespace AirBridge.Desktop {
 internal sealed partial class RouteCoordinator {
  internal async Task ApplyAudioTuning(PlaybackTuning value){RequireIdle();value.Validate();Busy=true;try{active=Task.Run(()=>Engine.ApplyPlaybackTuning(value));await active;settings.Set("PlaybackTuning",value);settings.Save();}finally{active=null;Busy=false;}}
 }
 internal sealed partial class MainView {
  void BuildAdvancedAudio(Panel parent){
   var body=new StackPanel();var fields=new Dictionary<string,TextBox>();
   var explanation=Label(L("仅影响 AirBridge 转发。输出延迟是请求值，实际取决于设备；应用时可能短暂断音。","Applies to AirBridge rendering. Actual output latency depends on the device; applying may briefly interrupt sound."),12,"7C8595");explanation.TextWrapping=TextWrapping.Wrap;explanation.Margin=new Thickness(0,8,0,16);body.Children.Add(explanation);
   var format=Label(L("固定格式：10ms 音频块 · 44.1kHz · 16位 · 双声道","Fixed format: 10ms blocks · 44.1kHz · 16-bit · stereo"),12,"7C8595");format.Margin=new Thickness(0,0,0,14);body.Children.Add(format);var jitterHelp=Label(L("实测抖动仅在抗卡顿缓冲开启时调整目标；统计 PCM 交付间隔，非蓝牙原始包。有限截断两种模式均生效。","Measured jitter adjusts the target only with buffering enabled; it measures delivered PCM, not raw Bluetooth packets. Finite trimming applies in both modes."),12,"7C8595");jitterHelp.TextWrapping=TextWrapping.Wrap;body.Children.Add(jitterHelp);
   foreach(var spec in PlaybackTuning.Fields){
    if(spec.Key=="LowBufferMs"||spec.Key=="OutputLatencyMs"||spec.Key=="CaptureRequests"){var heading=Label(spec.Key=="LowBufferMs"?L("缓冲与缺音处理","Buffer and gap handling"):spec.Key=="OutputLatencyMs"?L("输出与积压保护","Output and overflow protection"):L("采集 · 下次连接生效","Capture · next connection"),14,"202634");heading.FontWeight=FontWeights.SemiBold;heading.Margin=new Thickness(0,14,0,10);body.Children.Add(heading);}
    var row=new Grid{Margin=new Thickness(0,0,0,10)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(110)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(54)});
    var label=Label(L(spec.Zh,spec.En),13,"202634");label.TextWrapping=TextWrapping.Wrap;label.VerticalAlignment=VerticalAlignment.Center;label.Margin=new Thickness(0,0,12,0);row.Children.Add(label);
    var input=new TextBox{Height=34,Padding=new Thickness(9,5,9,5),VerticalContentAlignment=VerticalAlignment.Center,HorizontalContentAlignment=HorizontalAlignment.Right,ToolTip=spec.Min+"–"+spec.Max+" "+spec.Unit};input.SetResourceReference(Control.BackgroundProperty,"Brush_F8FAFD");input.SetResourceReference(Control.ForegroundProperty,"Brush_202634");input.SetResourceReference(Control.BorderBrushProperty,"Brush_DCE2EC");System.Windows.Automation.AutomationProperties.SetName(input,L(spec.Zh,spec.En));Grid.SetColumn(input,1);row.Children.Add(input);fields.Add(spec.Key,input);
    var unit=Label(spec.Unit,12,"7C8595");unit.Margin=new Thickness(8,0,0,0);unit.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(unit,2);row.Children.Add(unit);body.Children.Add(row);
   }
   Action<PlaybackTuning> fill=value=>{foreach(var pair in fields)pair.Value.Text=Convert.ToString(typeof(PlaybackTuning).GetProperty(pair.Key).GetValue(value,null),System.Globalization.CultureInfo.InvariantCulture);};fill(settings.AudioTuning());
   var status=Label("",12,"7C8595");status.TextWrapping=TextWrapping.Wrap;status.Margin=new Thickness(0,10,0,6);body.Children.Add(status);
   var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,2)};
   Func<Task> apply=async()=>{try{var value=new PlaybackTuning();foreach(var spec in PlaybackTuning.Fields){int number;if(!Int32.TryParse(fields[spec.Key].Text,out number))throw new ArgumentException(L(spec.Zh,spec.En)+L("：请输入整数。",": enter an integer."));typeof(PlaybackTuning).GetProperty(spec.Key).SetValue(value,number,null);}value.Validate();body.IsEnabled=false;status.Text=L("正在应用…","Applying…");await routes.ApplyAudioTuning(value);status.Text=L("已保存。采集参数在下次手机连接时生效。","Saved. Capture parameters take effect on the next phone connection.");}catch(Exception ex){status.Text=ex.GetBaseException().Message;}finally{body.IsEnabled=true;}};
   var reset=Button(L("恢复默认","Restore defaults"),()=>{fill(new PlaybackTuning());Run(async()=>await apply());});reset.Margin=new Thickness(0,0,10,0);actions.Children.Add(reset);actions.Children.Add(Button(L("应用","Apply"),()=>Run(async()=>await apply())));body.Children.Add(actions);
   var expander=new Expander{Header=L("音频高级设置","Advanced audio settings"),Content=body,IsExpanded=false,HorizontalContentAlignment=HorizontalAlignment.Stretch};expander.SetResourceReference(Control.ForegroundProperty,"Brush_202634");parent.Children.Add(new Border{Style=(Style)Window.FindResource("Card"),Padding=new Thickness(22,18,22,18),Margin=new Thickness(0,0,0,12),Child=expander});
  }
 }
}
