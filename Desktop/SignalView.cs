using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace AirBridge.Desktop {
 internal sealed partial class MainView {
  sealed class SignalView { public StackPanel Panel;public TextBlock Text;public Rectangle[] Bars; }
  readonly Dictionary<string,SignalView> signalViews=new Dictionary<string,SignalView>();
  UIElement WithSignal(string id,UIElement battery){
   var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(battery);
   var panel=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Center};
   var bars=new Rectangle[4];for(int i=0;i<4;i++){bars[i]=new Rectangle{Width=3,Height=4+i*3,RadiusX=1,RadiusY=1,Margin=new Thickness(0,0,2,0),VerticalAlignment=VerticalAlignment.Bottom};bars[i].SetResourceReference(Shape.FillProperty,"Brush_7C8595");panel.Children.Add(bars[i]);}
   var text=Label("—",11,"7C8595");text.Margin=new Thickness(5,0,0,0);text.VerticalAlignment=VerticalAlignment.Center;panel.Children.Add(text);row.Children.Add(panel);
   signalViews[id]=new SignalView{Panel=panel,Text=text,Bars=bars};return row;
  }
  void TickSignals(DateTime now){
   foreach(var pair in signalViews){
    var address=SettingsStore.DeviceAddress(pair.Key);var reading=monitor.Signal(address);bool estimated=false;
    var identity=inventory.BluetoothDevices.FirstOrDefault(d=>String.Equals(d.Address,address,StringComparison.OrdinalIgnoreCase));
    if(identity!=null&&identity.LeAddress!=0){var le=monitor.Signal(identity.LeAddress.ToString("X12"));if(le!=null&&(reading==null||le.Seen>reading.Seen))reading=le;}
    var device=inventory.Devices.FirstOrDefault(d=>d.Id==pair.Key);
    bool directFresh=reading!=null&&(now-reading.Seen).TotalSeconds>=0&&(now-reading.Seen).TotalSeconds<=12;
    if(!directFresh&&device!=null&&device.IsActive&&providers.Resolve(device).ProviderId=="apple-airpods"){
     var model=providers.Resolve(device).Model;
     var pods=monitor.Snapshot().Where(p=>p.Model==model&&(now-p.Seen).TotalSeconds>=0&&(now-p.Seen).TotalSeconds<=12).ToArray();
     // Do not assign a nearby stranger's signal when more than one broadcast identity exists.
     if(inventory.Devices.Count(d=>!Ignored(d.Id)&&providers.Resolve(d).Model==model)==1&&pods.Length==1){reading=monitor.Signal(pods[0].Address);estimated=true;}
    }
    bool known=reading!=null&&reading.Rssi>-127&&reading.Rssi<=20&&(now-reading.Seen).TotalSeconds>=0;
    bool fresh=known&&(now-reading.Seen).TotalSeconds<=12;
    int level=!fresh?0:reading.Rssi>=-55?4:reading.Rssi>=-67?3:reading.Rssi>=-80?2:1;
    var view=pair.Value;for(int i=0;i<4;i++)view.Bars[i].Opacity=i<level?1:.2;
    view.Text.Text=fresh?reading.Rssi+" dBm":"—";
    view.Panel.ToolTip=known?L("蓝牙广播强度，不代表音频链路质量；更新于 ","BLE broadcast strength, not audio link quality; updated ")+reading.Seen.ToLocalTime().ToString("HH:mm:ss")+(!fresh?L("。历史值，非当前信号。",". Historical value, not current signal."):"")+(estimated?L("。附近同型号广播，身份未经验证。",". Nearby matching model; identity unverified."):""):L("此设备未提供可关联的 BLE 广播；音频已连接也不一定能读到信号。","No identifiable BLE advertisement; an audio connection does not guarantee RSSI availability.");
    if(fresh)view.Panel.ToolTip=L("最近 ","Average of last ")+reading.Samples+L(" 次有效信号的平均值。"," valid signal samples. ")+view.Panel.ToolTip;
    if(!fresh){var system=monitor.SystemSignal(address,identity==null?null:identity.ContainerId);if(system!=null){
     // Windows' classic Bluetooth property has no documented dBm scale or sample timestamp.
     // Keep it visibly distinct from a measured BLE advertisement, without invented bars.
     foreach(var bar in view.Bars)bar.Opacity=.2;
     view.Text.Text="—";
     view.Panel.ToolTip=L("暂无实时信号。Windows 缓存值：","No current signal. Windows cached value: ")+system.Value+" · "+(system.LowEnergy?"BLE":"Bluetooth Classic")+L(" 系统属性；采样时间和标度未知，不能等同音频链路质量。查询于 "," system property; sample time and scale unknown, not audio link quality. Queried ")+system.Queried.ToLocalTime().ToString("HH:mm:ss")+(!system.Connected?L(" · 系统报告未连接"," · OS reports disconnected"):"");
    }}
   }
  }
 }
}
