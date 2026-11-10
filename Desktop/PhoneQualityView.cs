using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using AirBridge.Routing;
namespace AirBridge.Desktop {
 internal sealed partial class MainView {
  readonly Dictionary<string,TextBlock> phoneQualityViews=new Dictionary<string,TextBlock>();
  UIElement WithPhoneQuality(string id,UIElement detail){var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(detail);var text=Label("—",11,"7C8595");text.Margin=new Thickness(12,0,0,0);text.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(text);phoneQualityViews[id]=text;return row;}
  void UpdatePhoneQuality(RouteState[] states){foreach(var pair in phoneQualityViews){var state=states.FirstOrDefault(s=>s.Source==pair.Key&&s.PhoneQualityState!=null);var text=pair.Value;
   if(state==null||state.PhoneQualityState=="inactive"){text.Text="—";text.ToolTip=L("未通过节点图转发，暂无缺音统计。","No graph forwarding; missing-audio measurement unavailable.");continue;}
   text.Text=state.PhoneQualityState=="waiting"?L("等待音频","Waiting for audio"):state.PhoneQualityState=="buffering"?L("缓冲中","Buffering"):L("缺音 ","Missing ")+state.PhoneMissingPercent.ToString("F1")+"%";
   text.ToolTip=L("近10秒：播放所需但缺少的音频占比，启动蓄积不计入。连续2秒未到帧显示等待，无法仅据此区分暂停与断流；恢复后重新统计。不是蓝牙丢包率。","Last 10s: missing audio as a share of render demand; startup buffering excluded. No frames for 2s means waiting, not proof of pause or disconnection; measurement restarts on return. Not Bluetooth packet loss.")+"\n"+
    L("本次通道主动丢弃（不计入缺音率）：有限截断 ","Channel lifetime intentional discard (excluded from missing rate): finite trim ")+(state.QueueTrimmedBytes/176.4).ToString("F0")+"ms · "+L("微幅追赶 ","catch-up ")+(state.AdaptiveTrimmedSampleFrames/44.1).ToString("F1")+"ms · "+L("静音跳过 ","silence skipped ")+(state.QuietTrimmedBytes/176.4).ToString("F0")+"ms · "+L("其他清空 ","other clears ")+(Math.Max(0,state.QueueDroppedBytes-state.QueueTrimmedBytes)/176.4).ToString("F0")+"ms";
  }}
 }
}
