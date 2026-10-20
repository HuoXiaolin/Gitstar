using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Gitstar.Desktop;

public partial class MainWindow
{
    private HwndSource? activationSource;
    private bool reopeningWindow,nativeRestoreQueued;
    private int activationSignalCount;
    internal void ReceiveActivation(){activationSignalCount++;Reopen();}
    private void ConfigureNativeActivation()
    {
        activationSource=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        activationSource?.AddHook(NativeActivationMessage);
        Closed+=(_,_)=>{activationSource?.RemoveHook(NativeActivationMessage);activationSource=null;};
    }
    private IntPtr NativeActivationMessage(IntPtr handle,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        // Launchers may call ShowWindow/SetWindowPos directly on a hidden WPF HWND.
        // Its native visibility then differs from Window.IsVisible; re-enter Window.Show outside the hook.
        var showing=message==0x0018&&wParam!=IntPtr.Zero; // WM_SHOWWINDOW
        var activating=message==0x0006&&(wParam.ToInt64()&0xFFFF)!=0; // WM_ACTIVATE
        var positioning=message is 0x0047 or 0x0005 or 0x000F; // WM_WINDOWPOSCHANGED / SIZE / PAINT
        if((showing||activating||positioning)&&!reopeningWindow&&!nativeRestoreQueued&&!exit&&!quitting&&(!IsVisible||backgroundViewsReleased||backgroundDisplayPending)){
            nativeRestoreQueued=true;
            _=Dispatcher.BeginInvoke(()=>{
                nativeRestoreQueued=false;if(exit||quitting||reopeningWindow||!NativeWindowVisible(handle)||NativeWindowMinimized(handle))return;
                if(!IsVisible||backgroundViewsReleased||backgroundDisplayPending)Reopen();
            },DispatcherPriority.Normal);
        }
        return IntPtr.Zero;
    }
    public void Reopen()
    {
        if(exit||quitting||reopeningWindow)return;reopeningWindow=true;
        try{
            ShowInTaskbar=true;Show();if(WindowState!=WindowState.Maximized)WindowState=WindowState.Normal;
            ResumeBackgroundViews();InvalidateVisual();UpdateLayout();
            var target=OwnedWindows.Cast<Window>().LastOrDefault(window=>window.IsVisible)??this;target.Activate();
        }finally{reopeningWindow=false;}
    }
    [DllImport("user32.dll",EntryPoint="IsWindowVisible")]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool NativeWindowVisible(IntPtr handle);
    [DllImport("user32.dll",EntryPoint="IsIconic")]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool NativeWindowMinimized(IntPtr handle);
}
