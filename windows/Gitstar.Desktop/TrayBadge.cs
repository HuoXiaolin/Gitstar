using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
namespace Gitstar.Desktop;
public static class TrayBadge
{
    [DllImport("user32")]private static extern bool DestroyIcon(IntPtr handle);
    public static Icon Create(bool hasUpdates)
    {
        using var resource=Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Gitstar.png"))!.Stream;
        using var image=Image.FromStream(resource);using var bitmap=new Bitmap(32,32);using var g=Graphics.FromImage(bitmap);
        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;g.DrawImage(image,0,0,32,32);
        if(hasUpdates){using var fill=new SolidBrush(Color.FromArgb(233,66,94));using var outline=new Pen(Color.White,1.5f);g.FillEllipse(fill,22,1,9,9);g.DrawEllipse(outline,22,1,9,9);}
        var handle=bitmap.GetHicon();try{using var temp=Icon.FromHandle(handle);return (Icon)temp.Clone();}finally{DestroyIcon(handle);}
    }
}
