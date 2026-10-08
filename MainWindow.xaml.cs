using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentLegionToolbar.Hardware;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
namespace FluentLegionToolbar;
public sealed partial class MainWindow : Window
{
    readonly LegionDevice device = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(15) };
    readonly TrayIcon tray;
    Hardware.DeviceState? current;
    bool busy, arabic, preview;
    bool dialogOpen;
    readonly string[] args = Environment.GetCommandLineArgs();
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    public MainWindow()
    {
        InitializeComponent();
        Title="Fluent Legion Toolbar";
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true; SetTitleBar(TitleBar);
        if (AppWindow.Presenter is OverlappedPresenter presenter) { presenter.IsResizable=false; presenter.IsMaximizable=false; presenter.IsMinimizable=false; presenter.IsAlwaysOnTop=true; }
        var hwnd=WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale=GetDpiForWindow(hwnd)/96d;
        AppWindow.Resize(new SizeInt32((int)(520*scale),(int)(645*scale)));
        preview=args.Contains("--capture");
        if(preview) {
            Root.Width=472;Root.Height=610;
            AppWindow.Resize(new SizeInt32((int)(580*scale),(int)(730*scale)));
            Root.Background=new SolidColorBrush(args.Contains("--dark")?Windows.UI.Color.FromArgb(255,32,32,32):Windows.UI.Color.FromArgb(255,243,243,243));
        }
        arabic=args.Contains("--arabic");
        if(args.Contains("--dark")) Root.RequestedTheme=ElementTheme.Dark;
        if(args.Contains("--light")) Root.RequestedTheme=ElementTheme.Light;
        tray=new TrayIcon(hwnd, Path.Combine(AppContext.BaseDirectory,"app.ico"), [(1,"Open / فتح"),(2,"العربية / English"),(3,"Exit / خروج")], ShowFlyout, id=> { if(id==1) ShowFlyout(); else if(id==2) {arabic=!arabic;Localize();} else Close(); });
        tray.NativeTip=true; tray.SetTip("Fluent Legion Toolbar");
        Closed+=(_,_)=>{ timer.Stop();tray.Dispose();};
        Activated+=(_,e)=>{if(e.WindowActivationState==WindowActivationState.Deactivated && !preview && !dialogOpen) AppWindow.Hide();};
        timer.Tick+=async(_,_)=>{if(AppWindow.IsVisible) await Refresh();};
        Root.Loaded+=async(_,_)=> {
            Localize();
            if(preview) { await Capture(); return; }
            timer.Start(); await Refresh();
        };
    }
    void ShowFlyout()
    {
        if(AppWindow.IsVisible) {AppWindow.Hide();return;}
        var hwnd=WinRT.Interop.WindowNative.GetWindowHandle(this);
        if(tray.TryGetAnchor(out int x,out int y)) {
            var area=DisplayArea.GetFromPoint(new PointInt32(x,y),DisplayAreaFallback.Nearest).WorkArea;
            int left=Math.Clamp(x-AppWindow.Size.Width/2,area.X,Math.Max(area.X,area.X+area.Width-AppWindow.Size.Width));
            int top=Math.Clamp(y-AppWindow.Size.Height-16,area.Y,Math.Max(area.Y,area.Y+area.Height-AppWindow.Size.Height));
            AppWindow.Move(new PointInt32(left,top));
        }
        AppWindow.Show(); Activate();SetForegroundWindow(hwnd); _=Refresh();
    }
    string T(string en,string ar)=>arabic?ar:en;
    void Localize()
    {
        Root.FlowDirection=arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        BatteryHeader.Text=T("MY BATTERY","البطارية");SettingsHeader.Text=T("QUICK SETTINGS","الإعدادات السريعة");
        BatteryLink.Content=T("Battery details","تفاصيل البطارية"); AllLink.Content=T("All settings","جميع الإعدادات");
        FnLabel.Text=T("Fn Lock","قفل Fn"); UsbLabel.Text=T("Always-on USB","طاقة USB الدائمة"); MicLabel.Text=T("Mute","كتم"); ConservationLabel.Text=T("Conserve","حفاظ");RapidLabel.Text=T("Rapid","سريع");TouchpadLabel.Text=T("Lock","قفل");
        WarrantyLink.Content=T("Warranty options","خيارات الضمان");LanguageButton.Content=arabic?"English":"العربية";
        ToolTipService.SetToolTip(Microphone,T("Mute all active recording endpoints, including external microphones.","كتم جميع أجهزة التسجيل النشطة، بما فيها الميكروفونات الخارجية."));
        ToolTipService.SetToolTip(Conservation,T("Battery conservation. Mutually exclusive with rapid charge.","الحفاظ على البطارية. لا يعمل مع الشحن السريع في الوقت نفسه."));
        ToolTipService.SetToolTip(Rapid,T("Rapid charging. Mutually exclusive with conservation.","الشحن السريع. لا يعمل مع وضع الحفاظ في الوقت نفسه."));
        ToolTipService.SetToolTip(Touchpad,T("Lock touchpad. Use Fn+F10 or an external mouse to recover.","قفل لوحة اللمس. استخدم Fn+F10 أو فأرة خارجية للاستعادة."));
        if(current!=null) Apply(current);
    }
    async Task Refresh() {if(busy || preview)return;busy=true;try{Apply(await device.ReadAsync());}catch(Exception e){Status.Text=e.Message;}finally{busy=false;}}
    void Apply(Hardware.DeviceState value)
    {
        current=value;
        DeviceTitle.Text=T("My Legion 5 15ITH6H","جهازي Legion 5 15ITH6H");
        PercentText.Text=value.Percent.HasValue?$"{value.Percent}%":"--%";
        BatteryFill.Width=value.Percent.HasValue?Math.Max(0,390*value.Percent.Value/100d):0;
        PlugIcon.Visibility=value.Plugged?Visibility.Visible:Visibility.Collapsed;
        ChargeText.Text=T(value.Charging?"Charging":value.Plugged?"Plugged in, not charging":"On battery",value.Charging?"جارٍ الشحن":value.Plugged?"متصل بالطاقة، لا يشحن":"يعمل بالبطارية");
        FnLock.IsEnabled=value.FnLocked.HasValue&&!preview;FnLock.IsChecked=value.FnLocked;
        Usb.IsEnabled=value.UsbMode.HasValue&&!preview;Usb.IsChecked=value.UsbMode==2;
        bool exactRates=value.AvailableHz?.Contains(60)==true&&value.AvailableHz.Contains(144);
        RefreshRate.IsEnabled=exactRates&&!preview;RefreshRate.IsChecked=value.RefreshHz==144;
        RefreshLabel.Text=value.RefreshHz.HasValue?$"{value.RefreshHz} Hz":"-- Hz";
        ToolTipService.SetToolTip(RefreshRate,exactRates?T("Switch internal display between 60 and 144 Hz.","تبديل الشاشة الداخلية بين 60 و144 هرتز."):T("60 and 144 Hz are not both available. No unsupported display mode will be applied.","لا يتوفر الترددان 60 و144 هرتز معاً. لن يُفرض وضع شاشة غير مدعوم."));
        ToolTipService.SetToolTip(Usb,T("Always-on USB can drain the battery. BIOS and firmware policies still apply.","قد تستنزف طاقة USB الدائمة البطارية. تظل سياسات BIOS والفيرموير سارية."));
        Microphone.IsEnabled=value.Muted.HasValue&&!preview;Microphone.IsChecked=value.Muted;
        Conservation.IsEnabled=Rapid.IsEnabled=value.Mode.HasValue&&!preview;
        Conservation.IsChecked=value.Mode==ChargeMode.Conservation;Rapid.IsChecked=value.Mode==ChargeMode.Rapid;
        Touchpad.IsEnabled=value.TouchpadLocked.HasValue&&!preview;Touchpad.IsChecked=value.TouchpadLocked;
        Status.Text=preview?T("Visual preview only. No hardware commands are sent.","معاينة بصرية فقط. لا تُرسل أوامر إلى الجهاز."):value.Problems.Length>0?T("Some controls unavailable. Open battery details for diagnostics.","بعض العناصر غير متاحة. افتح تفاصيل البطارية لعرض التشخيص."):T("Hardware states read successfully.","تمت قراءة حالات الجهاز بنجاح.");
    }
    async Task Run(Func<Task> action)
    {
        if(busy||preview||current==null)return;busy=true;
        FnLock.IsEnabled=Usb.IsEnabled=RefreshRate.IsEnabled=Microphone.IsEnabled=Conservation.IsEnabled=Rapid.IsEnabled=Touchpad.IsEnabled=false;
        try {await action();Apply(await device.ReadAsync());}
        catch(Exception e){Apply(await device.ReadAsync());Status.Text=T("Not confirmed: ","لم يتم التأكيد: ")+e.Message;}
        finally{busy=false;}
    }
    async void FnClick(object sender,RoutedEventArgs e)=>await Run(()=>device.SetFnAsync(!(current?.FnLocked??false)));
    async void UsbClick(object sender,RoutedEventArgs e)=>await Run(()=>device.SetUsbAsync(current?.UsbMode!=2));
    async void RefreshClick(object sender,RoutedEventArgs e)=>await Run(()=>device.SetRefreshAsync(current?.RefreshHz==144?60:144));
    async void MicClick(object sender,RoutedEventArgs e)=>await Run(()=>device.SetMicrophoneAsync(!(current?.Muted??false)));
    async void ConservationClick(object sender,RoutedEventArgs e)=>await Run(()=>device.SetModeAsync(current?.Mode==ChargeMode.Conservation?ChargeMode.Normal:ChargeMode.Conservation));
    async void RapidClick(object sender,RoutedEventArgs e)=>await Run(()=>device.SetModeAsync(current?.Mode==ChargeMode.Rapid?ChargeMode.Normal:ChargeMode.Rapid));
    async void TouchpadClick(object sender,RoutedEventArgs e)
    {
        if(current?.TouchpadLocked==false) {
            dialogOpen=true;
            try {
                var dialog=new ContentDialog{XamlRoot=Root.XamlRoot,Title=T("Lock the touchpad?","قفل لوحة اللمس؟"),Content=T("Make sure you have an external mouse or can use Fn+F10 to unlock it.","تأكد من توفر فأرة خارجية أو إمكانية استخدام Fn+F10 لفتح القفل."),PrimaryButtonText=T("Lock","قفل"),CloseButtonText=T("Cancel","إلغاء"),DefaultButton=ContentDialogButton.Close};
                if(await dialog.ShowAsync()!=ContentDialogResult.Primary){await Refresh();return;}
            }finally{dialogOpen=false;}
        }
        await Run(()=>device.SetTouchpadAsync(!(current?.TouchpadLocked??false)));
    }
    static void Open(string url)=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
    void AllSettings(object sender,RoutedEventArgs e)=>Open("ms-settings:");
    void Warranty(object sender,RoutedEventArgs e)=>Open("https://pcsupport.lenovo.com/us/en/products/laptops-and-netbooks/legion-series/legion-5-15ith6h/82jh");
    async void BatteryDetails(object sender,RoutedEventArgs e)
    {
        dialogOpen=true;
        try {var d=new ContentDialog{XamlRoot=Root.XamlRoot,Title=T("Battery and device details","تفاصيل البطارية والجهاز"),Content=$"{current?.Model}\n{PercentText.Text}\n{ChargeText.Text}\n{string.Join("\n",current?.Problems??[])}\n\nbrought to you by app.instinct AI",CloseButtonText=T("Close","إغلاق")};await d.ShowAsync();}finally{dialogOpen=false;}
    }
    void ChangeLanguage(object sender,RoutedEventArgs e){arabic=!arabic;Localize();}
    void ChangeTheme(object sender,RoutedEventArgs e){Root.RequestedTheme=Root.ActualTheme==ElementTheme.Dark?ElementTheme.Light:ElementTheme.Dark;}
    async Task Capture()
    {
        // Deterministic render fixture, clearly labelled as preview. Hardware services are never called.
        Apply(new Hardware.DeviceState(60,true,false,ChargeMode.Conservation,false,false,"Visual fixture",[],true,2,144,[60,144]));
        FnLock.IsEnabled=Usb.IsEnabled=RefreshRate.IsEnabled=Microphone.IsEnabled=Conservation.IsEnabled=Rapid.IsEnabled=Touchpad.IsEnabled=true;
        await Task.Delay(2500);
        int index=Array.IndexOf(args,"--capture");string path=args[index+1];
        var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(Root);
        var buffer=await bitmap.GetPixelsAsync();
        using var reader=Windows.Storage.Streams.DataReader.FromBuffer(buffer);var pixels=new byte[buffer.Length];reader.ReadBytes(pixels);
        using var stream=File.Open(path,FileMode.Create).AsRandomAccessStream();
        var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels);await encoder.FlushAsync();
        Close();
    }
}
