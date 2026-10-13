using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
namespace Gitstar.Desktop;
public static class TrayBadge
{
    [DllImport("user32")]private static extern bool DestroyIcon(IntPtr handle);
    public static Icon Create(int count)
    {
        using var resource=Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Gitstar.png"))!.Stream;
        using var image=Image.FromStream(resource);using var bitmap=new Bitmap(32,32);using var g=Graphics.FromImage(bitmap);
        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;g.DrawImage(image,0,0,32,32);
        if(count>0){using var fill=new SolidBrush(Color.FromArgb(233,66,94));g.FillEllipse(fill,13,13,19,19);using var font=new Font("Segoe UI",9,System.Drawing.FontStyle.Bold,GraphicsUnit.Pixel);using var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};g.DrawString(count>9?"9+":count.ToString(),font,Brushes.White,new RectangleF(13,13,19,19),format);}
        var handle=bitmap.GetHicon();try{using var temp=Icon.FromHandle(handle);return (Icon)temp.Clone();}finally{DestroyIcon(handle);}
    }
}
