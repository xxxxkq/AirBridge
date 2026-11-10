using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AirBridge.Desktop {
 // Borderless windows must use the current monitor's work area, not its full bounds.
 // All coordinates here remain in native window units; no primary-screen/DIP mixing.
 internal sealed class WindowWorkArea : IDisposable {
  [StructLayout(LayoutKind.Sequential)]struct Point {public int X,Y;}
  [StructLayout(LayoutKind.Sequential)]struct Rect {public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)]struct WindowPosition {public IntPtr Hwnd,After;public int X,Y,Width,Height;public uint Flags;}
  [StructLayout(LayoutKind.Sequential)]struct MonitorInfo {public int Size;public Rect Monitor,Work;public uint Flags;}
  [DllImport("user32.dll")]static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
  [DllImport("user32.dll",CharSet=CharSet.Auto)]static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
  [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
  [DllImport("user32.dll")]static extern bool IsZoomed(IntPtr hwnd);
  [DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);
  [DllImport("user32.dll")]static extern IntPtr MonitorFromPoint(Point point,uint flags);
  internal static System.Drawing.Rectangle PopupArea(Window owner){var info=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};Point point;GetCursorPos(out point);var monitor=owner.IsVisible?MonitorFromWindow(new WindowInteropHelper(owner).Handle,2):MonitorFromPoint(point,2);GetMonitorInfo(monitor,ref info);return System.Drawing.Rectangle.FromLTRB(info.Work.Left,info.Work.Top,info.Work.Right,info.Work.Bottom);}
  internal static void PositionPopup(Window popup,System.Drawing.Rectangle area,bool center,bool measured){var hwnd=new WindowInteropHelper(popup).EnsureHandle();if(!measured){SetWindowPos(hwnd,IntPtr.Zero,area.Left+20,area.Top+20,0,0,0x15);return;}Rect bounds;GetWindowRect(hwnd,out bounds);var scale=PresentationSource.FromVisual(popup).CompositionTarget.TransformToDevice;int gap=(int)Math.Round(12*scale.M11),width=bounds.Right-bounds.Left,height=bounds.Bottom-bounds.Top;int x=center?area.Left+(area.Width-width)/2:area.Right-width-gap;SetWindowPos(hwnd,IntPtr.Zero,Math.Max(area.Left,x),Math.Max(area.Top,area.Bottom-height-gap),0,0,0x15);}
  internal static void PositionTray(Window window,System.Drawing.Point anchor,bool measured){var info=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};GetMonitorInfo(MonitorFromPoint(new Point{X=anchor.X,Y=anchor.Y},2),ref info);var hwnd=new WindowInteropHelper(window).EnsureHandle();if(!measured){SetWindowPos(hwnd,IntPtr.Zero,info.Work.Left+20,info.Work.Top+20,0,0,0x15);return;}var bounds=NativeBounds(hwnd);int x=Math.Max(info.Work.Left,Math.Min(anchor.X-bounds.Width/2,info.Work.Right-bounds.Width));int y=Math.Max(info.Work.Top,Math.Min(anchor.Y-bounds.Height,info.Work.Bottom-bounds.Height));SetWindowPos(hwnd,IntPtr.Zero,x,y,0,0,0x15);}  [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
  [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
  internal static System.Drawing.Rectangle NativeBounds(IntPtr hwnd){Rect rect;GetWindowRect(hwnd,out rect);return System.Drawing.Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom);}
  [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr hwnd,out Rect rect);
  [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
  internal static void MoveTo(Window window,System.Drawing.Rectangle area){SetWindowPos(new WindowInteropHelper(window).Handle,IntPtr.Zero,area.Left+40,area.Top+40,Math.Min(1080,area.Width-80),Math.Min(820,area.Height-80),0x14);}
  internal static string Geometry(Window window){IntPtr hwnd=new WindowInteropHelper(window).Handle;Rect r,c;GetWindowRect(hwnd,out r);GetClientRect(hwnd,out c);var scale=HwndSource.FromHwnd(hwnd).CompositionTarget.TransformToDevice;return string.Format("window={0},{1},{2},{3}; client={4}x{5}; WPF={6}x{7}; scale={8},{9}",r.Left,r.Top,r.Right,r.Bottom,c.Right,c.Bottom,window.ActualWidth,window.ActualHeight,scale.M11,scale.M22);}
  internal static bool ClientFits(Window window){Rect c;GetClientRect(new WindowInteropHelper(window).Handle,out c);var scale=PresentationSource.FromVisual(window).CompositionTarget.TransformToDevice;return Math.Abs(window.ActualWidth*scale.M11-c.Right)<=2&&Math.Abs(window.ActualHeight*scale.M22-c.Bottom)<=2;}
  readonly Window window;HwndSource source;bool maximizing;
  public WindowWorkArea(Window window){this.window=window;window.SourceInitialized+=Attach;window.Loaded+=ReorderHook;window.SizeChanged+=UpdateInsets;window.StateChanged+=UpdateInsets;}
  void UpdateInsets(object sender,EventArgs args){var content=window.Content as FrameworkElement;if(content==null)return;MonitorInfo info;Rect rect;var hwnd=new WindowInteropHelper(window).Handle;if(window.WindowState!=WindowState.Maximized||!WorkArea(hwnd,out info)||!GetWindowRect(hwnd,out rect)){content.Margin=new Thickness(0);return;}var transform=source.CompositionTarget.TransformFromDevice;content.Margin=new Thickness(Math.Max(0,info.Work.Left-rect.Left)*transform.M11,Math.Max(0,info.Work.Top-rect.Top)*transform.M22,Math.Max(0,rect.Right-info.Work.Right)*transform.M11,Math.Max(0,rect.Bottom-info.Work.Bottom)*transform.M22);}
  void ReorderHook(object sender,RoutedEventArgs args){if(source!=null){source.RemoveHook(Hook);source.AddHook(Hook);}}
  void Attach(object sender,EventArgs args){var hwnd=new WindowInteropHelper(window).Handle;source=HwndSource.FromHwnd(hwnd);if(source!=null)source.AddHook(Hook);
   // Preserve WPF's borderless client layout while opting into native caption
   // transitions. WindowChrome still paints/hit-tests our custom title bar.
   SetWindowLongPtr(hwnd,-16,new IntPtr(GetWindowLongPtr(hwnd,-16).ToInt64()|0x00C00000L));
  }
  static bool WorkArea(IntPtr hwnd,out MonitorInfo info){info=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};return GetMonitorInfo(MonitorFromWindow(hwnd,2),ref info);}
  IntPtr Hook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled){
   if(message==0x112){long command=wParam.ToInt64()&0xFFF0;maximizing=command==0xF030;}
   // MINMAXINFO is primary-monitor-relative: supplying the secondary dimensions
   // there makes USER32 add the monitor-size difference a second time.
   // Constrain the final proposed native bounds instead, after that adjustment.
   if(message==0x46&&lParam!=IntPtr.Zero&&(IsZoomed(hwnd)||maximizing)){MonitorInfo info;if(WorkArea(hwnd,out info)){
    var bounds=(WindowPosition)Marshal.PtrToStructure(lParam,typeof(WindowPosition));
    if((bounds.Flags&3)!=3){
     bounds.X=info.Work.Left;bounds.Y=info.Work.Top;
     bounds.Width=info.Work.Right-info.Work.Left;bounds.Height=info.Work.Bottom-info.Work.Top;
     bounds.Flags&=~3u;Marshal.StructureToPtr(bounds,lParam,false);handled=true;
    }
   }}return IntPtr.Zero;
  }
  internal static bool FitsWorkArea(Window window){IntPtr hwnd=new WindowInteropHelper(window).Handle;MonitorInfo info;Rect actual;if(!WorkArea(hwnd,out info)||!GetWindowRect(hwnd,out actual))return false;var margin=((FrameworkElement)window.Content).Margin;var scale=PresentationSource.FromVisual(window).CompositionTarget.TransformToDevice;return Math.Abs(actual.Left+margin.Left*scale.M11-info.Work.Left)<=2&&Math.Abs(actual.Top+margin.Top*scale.M22-info.Work.Top)<=2&&Math.Abs(actual.Right-margin.Right*scale.M11-info.Work.Right)<=2&&Math.Abs(actual.Bottom-margin.Bottom*scale.M22-info.Work.Bottom)<=2;}
  public void Dispose(){window.SourceInitialized-=Attach;window.Loaded-=ReorderHook;window.SizeChanged-=UpdateInsets;window.StateChanged-=UpdateInsets;if(source!=null){source.RemoveHook(Hook);source=null;}}
 }
}
