using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// Per-pixel alpha keeps the floating icon transparent without a white/color matte.
static class LayeredBall {
 [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
 public static void EnableDpi(){SetProcessDPIAware();}
 public static int Pixels(int logical){using(var g=Graphics.FromHwnd(IntPtr.Zero))return Math.Max(1,(int)Math.Round(logical*g.DpiX/96.0));}
 [StructLayout(LayoutKind.Sequential)]struct Point {public int X,Y;public Point(int x,int y){X=x;Y=y;}}
 [StructLayout(LayoutKind.Sequential)]struct Size {public int Width,Height;public Size(int w,int h){Width=w;Height=h;}}
 [StructLayout(LayoutKind.Sequential,Pack=1)]struct Blend {public byte Operation,Flags,Alpha,Format;}
 [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr window);
 [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr window,IntPtr dc);
 [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
 [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
 [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr obj);
 [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
 [DllImport("user32.dll",SetLastError=true)]static extern bool UpdateLayeredWindow(IntPtr window,IntPtr destination,ref Point location,ref Size size,IntPtr source,ref Point origin,int key,ref Blend blend,int flags);
 public static Bitmap Render(Image image,int width,int height){var bitmap=new Bitmap(width,height,PixelFormat.Format32bppPArgb);using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.Transparent);g.CompositingMode=CompositingMode.SourceCopy;g.CompositingQuality=CompositingQuality.HighQuality;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;double ratio=Math.Min((double)width/image.Width,(double)height/image.Height);int w=(int)Math.Round(image.Width*ratio),h=(int)Math.Round(image.Height*ratio);using(var attrs=new ImageAttributes()){attrs.SetWrapMode(WrapMode.TileFlipXY);g.DrawImage(image,new Rectangle((width-w)/2,(height-h)/2,w,h),0,0,image.Width,image.Height,GraphicsUnit.Pixel,attrs);}}return bitmap;}
 public static void Draw(IntPtr window,Image image,int x,int y,int width,int height){using(var bitmap=Render(image,width,height)){IntPtr screen=GetDC(IntPtr.Zero),memory=IntPtr.Zero,hbitmap=IntPtr.Zero,old=IntPtr.Zero;try{memory=CreateCompatibleDC(screen);hbitmap=bitmap.GetHbitmap(Color.FromArgb(0));old=SelectObject(memory,hbitmap);var location=new Point(x,y);var size=new Size(width,height);var origin=new Point(0,0);var blend=new Blend{Operation=0,Flags=0,Alpha=255,Format=1};if(!UpdateLayeredWindow(window,screen,ref location,ref size,memory,ref origin,0,ref blend,2))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}finally{if(old!=IntPtr.Zero)SelectObject(memory,old);if(hbitmap!=IntPtr.Zero)DeleteObject(hbitmap);if(memory!=IntPtr.Zero)DeleteDC(memory);if(screen!=IntPtr.Zero)ReleaseDC(IntPtr.Zero,screen);}}}
}
