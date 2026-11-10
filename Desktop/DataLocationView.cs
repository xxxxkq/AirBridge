using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using AirBridge.Storage;
namespace AirBridge.Desktop {
 internal sealed partial class MainView {
  void BuildDataLocationPreference(Panel parent){
   var body=new StackPanel();var header=new Grid();header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   var labels=new StackPanel{Margin=new Thickness(0,0,20,0),VerticalAlignment=VerticalAlignment.Center};var title=Label(L("数据保存位置","Data location"),14);title.FontWeight=FontWeights.SemiBold;labels.Children.Add(title);var current=Label(settings.Root,12,"697386");current.TextWrapping=TextWrapping.NoWrap;current.TextTrimming=TextTrimming.CharacterEllipsis;current.ToolTip=settings.Root;current.Margin=new Thickness(0,5,0,0);labels.Children.Add(current);header.Children.Add(labels);
   var controls=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(controls,1);var open=Button(L("打开目录","Open folder"),()=>Open(settings.Root));open.Height=36;open.Padding=new Thickness(12,0,12,0);open.Margin=new Thickness(0,0,8,0);controls.Children.Add(open);
   var details=new StackPanel{Visibility=Visibility.Collapsed,Margin=new Thickness(0,16,0,0)};Button change=null;change=Button(L("更改","Change"),()=>{bool show=details.Visibility!=Visibility.Visible;details.Visibility=show?Visibility.Visible:Visibility.Collapsed;change.Content=show?L("收起","Collapse"):L("更改","Change");});change.Height=36;change.Padding=new Thickness(12,0,12,0);controls.Children.Add(change);header.Children.Add(controls);body.Children.Add(header);
   var pending=Label("",12,"697386");pending.Margin=new Thickness(0,8,0,0);pending.Visibility=Visibility.Collapsed;
   Button undo=null;undo=Button(L("撤销待生效更改","Undo pending change"),()=>{if(settings.Test)return;DataLocation.Cancel(ApplicationLaunchPath.Current);pending.Visibility=Visibility.Collapsed;undo.Visibility=Visibility.Collapsed;});undo.Height=32;undo.Padding=new Thickness(10,0,10,0);undo.Margin=new Thickness(0,8,0,0);undo.HorizontalAlignment=HorizontalAlignment.Left;undo.Visibility=Visibility.Collapsed;
   var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(210)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   string portable=Path.Combine(Path.GetDirectoryName(ApplicationLaunchPath.Current),"Data"),selectedPath=settings.Root;
   if(!settings.Test){string saved=DataLocation.PendingTarget(ApplicationLaunchPath.Current,DataLocation.LegacyRoot);if(saved!=null)selectedPath=saved;}
   var mode=new ComboBox{Height=36,ItemsSource=new[]{L("软件旁的 Data 文件夹","Data beside the EXE"),L("自定义目录","Custom folder"),L("Windows 用户数据目录","Windows user data folder")},SelectedIndex=String.Equals(selectedPath,portable,StringComparison.OrdinalIgnoreCase)?0:String.Equals(selectedPath,DataLocation.LegacyRoot,StringComparison.OrdinalIgnoreCase)?2:1};row.Children.Add(mode);
   var destination=new TextBox{Text=selectedPath,IsReadOnly=true,Height=36,Margin=new Thickness(10,0,0,0),Padding=new Thickness(10,0,10,0),ToolTip=selectedPath};Grid.SetColumn(destination,1);row.Children.Add(destination);
   var browse=Button(L("浏览…","Browse…"),()=>{using(var dialog=new System.Windows.Forms.FolderBrowserDialog{Description=L("选择专用数据目录；迁移时目标需为空。","Select a dedicated folder; migration needs an empty destination."),ShowNewFolderButton=true}){if(dialog.ShowDialog()==System.Windows.Forms.DialogResult.OK){mode.SelectedIndex=1;destination.Text=dialog.SelectedPath;destination.ToolTip=dialog.SelectedPath;}}});browse.Height=36;browse.Padding=new Thickness(12,0,12,0);browse.Margin=new Thickness(8,0,0,0);browse.Visibility=mode.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;Grid.SetColumn(browse,2);row.Children.Add(browse);details.Children.Add(row);
   var copyRow=new Grid{Margin=new Thickness(0,12,0,0)};copyRow.ColumnDefinitions.Add(new ColumnDefinition());copyRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var copyLabel=Label(L("迁移现有配置与日志","Copy current configuration and logs"),13);copyLabel.VerticalAlignment=VerticalAlignment.Center;copyRow.Children.Add(copyLabel);
   var copy=new CheckBox{IsChecked=settings.Test||DataLocation.PendingCopies(ApplicationLaunchPath.Current),Width=36,Height=20,ClipToBounds=true,HorizontalContentAlignment=HorizontalAlignment.Left,Margin=new Thickness(0),ToolTip=L("目标目录需为空；关闭后使用目标已有数据，不合并。录音、旧缓存和更新备份不搬动。","Requires an empty destination. Disable to use existing data without merging. Recordings and old caches/backups stay in place.")};Grid.SetColumn(copy,1);copyRow.Children.Add(copy);details.Children.Add(copyRow);
   var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,14,0,0)};Button apply=null;
   apply=Button(L("保存更改","Save changes"),()=>Run(async()=>{if(settings.Test)return;if(updateBusy){Notify(L("请先完成或取消更新操作。","Finish or cancel the update first."));return;}
    string selected=mode.SelectedIndex==0?"portable":mode.SelectedIndex==2?"default":"custom",path=destination.Text;bool migrate=copy.IsChecked==true;
    if(!migrate&&MessageBox.Show(Window,L("使用目标目录已有数据，不合并当前配置？原目录会保留。","Use the destination data without merging? The original folder is retained."),"AirBridge",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
    apply.IsEnabled=false;try{await Task.Run(()=>DataLocation.Request(ApplicationLaunchPath.Current,settings.Root,DataLocation.LegacyRoot,selected,path,migrate));bool hasPending=DataLocation.HasPending(ApplicationLaunchPath.Current);pending.Text=L("退出并重开后生效，原目录保留。","Quit and reopen to apply. Original data is retained.");pending.Visibility=hasPending?Visibility.Visible:Visibility.Collapsed;undo.Visibility=pending.Visibility;details.Visibility=Visibility.Collapsed;change.Content=L("更改","Change");}finally{apply.IsEnabled=true;}
   }));apply.Height=36;apply.Padding=new Thickness(12,0,12,0);apply.Margin=new Thickness(0,0,8,0);actions.Children.Add(apply);var cancel=Button(L("取消","Cancel"),()=>{details.Visibility=Visibility.Collapsed;change.Content=L("更改","Change");});cancel.Height=36;cancel.Padding=new Thickness(12,0,12,0);actions.Children.Add(cancel);details.Children.Add(actions);
   body.Children.Add(details);body.Children.Add(pending);body.Children.Add(undo);
   mode.SelectionChanged+=(s,e)=>{if(mode.SelectedIndex==0)destination.Text=portable;else if(mode.SelectedIndex==2)destination.Text=DataLocation.LegacyRoot;destination.ToolTip=destination.Text;browse.Visibility=mode.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;};
   if(!settings.Test&&DataLocation.HasPending(ApplicationLaunchPath.Current)){pending.Text=L("数据位置待重启生效。","Data location will change after restart.");pending.Visibility=Visibility.Visible;undo.Visibility=Visibility.Visible;}
   parent.Children.Add(new Border{Style=(Style)Window.FindResource("Card"),Padding=new Thickness(22,18,22,18),Child=body});
  }
 }
}
