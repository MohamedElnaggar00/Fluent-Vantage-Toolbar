using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using FluentLegionToolbar.Hardware;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
namespace FluentLegionToolbar;

public sealed partial class MainWindow : Window
{
    sealed record TileDef(string Id, string LabelKey, string Glyph);
    static readonly TileDef[] Tiles =
    [
        new("fn", "tile.fn", ""), new("mic", "tile.mic", "\uE720"), new("conserve", "tile.conserve", "\uEA93"),
        new("rapid", "tile.rapid", "\uE945"), new("touchpad", "tile.touchpad", "\uEFA5"),
        new("refresh", "tile.refresh", "\uE7F4"), new("usb", "tile.usb", "\uE88E"),
    ];

    readonly LegionDevice device = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(15) };
    readonly TrayIcon tray;
    readonly AppSettings settings;
    readonly string[] args = Environment.GetCommandLineArgs();
    readonly bool preview;
    readonly IntPtr hwnd;
    Grid canvas = null!;
    readonly Grid titleBar = new() { MinHeight = 36 };
    readonly ContentControl body = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    readonly Dictionary<string, ToggleButton> tileButtons = new();
    DeviceState? current;
    DeviceDashboardWindow? dashboard;
    TrayMenuWindow? contextMenu;
    BatteryDetails? batteryDetails;
    WarrantyResult? warranty;
    string? warrantyError;
    bool warrantyLoading;
    bool busy, dialogOpen, exiting, checkingUpdates;
    readonly List<Button> updateButtons = new();
    readonly CancellationTokenSource updateLifetime=new();
    UpdateNoticeWindow? updateNotice;
    int animationGeneration;
    Microsoft.UI.Xaml.Media.Animation.Storyboard? flyoutMotion;
    string view = "main";
    DateTime lastHidden = DateTime.MinValue;
    TextBlock? percentText, chargeText, statusText;
    FontIcon? plugIcon;
    Border? batteryFill; Grid? batteryFillHost;
    Windows.UI.Color batteryColor = Windows.UI.Color.FromArgb(255, 156, 218, 155);

    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr hwnd,IntPtr rect,IntPtr region,uint flags);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);

    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
    [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr hwnd,IntPtr region);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);

    public MainWindow(bool startHidden)
    {
        InitializeComponent();
        preview = args.Contains("--capture");
        if (preview) Hardware.DeviceInfo.Override = "Legion 5 15ITH6H";
        settings = preview ? new AppSettings { Language = args.Contains("--arabic") ? "ar" : "en", Theme = args.Contains("--dark") ? "dark" : "light" } : AppSettings.Load();
        L.Set(settings.Language);
        Title = "Fluent Vantage Toolbar";
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "app.ico")); } catch { }
        if (AppWindow.Presenter is OverlappedPresenter presenter) { presenter.SetBorderAndTitleBar(false, false); presenter.IsResizable = false; presenter.IsMaximizable = false; presenter.IsMinimizable = false; presenter.IsAlwaysOnTop = true; }
        HideFromTaskbar();
        double scale = GetDpiForWindow(hwnd) / 96d;
        AppWindow.ResizeClient(new SizeInt32((int)(520 * scale), (int)(520 * scale)));

        Root.Padding = new Thickness(24, 15, 24, 24);
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        canvas = new Grid { Height = 492 };
        canvas.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        canvas.RowDefinitions.Add(new RowDefinition());
        canvas.Children.Add(titleBar); Grid.SetRow(body, 1); canvas.Children.Add(body);
        Root.RowDefinitions.Clear(); Root.Padding = new Thickness(24, 15, 24, 24);
        canvas.HorizontalAlignment = HorizontalAlignment.Stretch; canvas.VerticalAlignment = VerticalAlignment.Top; Root.Children.Add(canvas);
        // Flyout uses a compact custom caption row; the gear and X share one baseline.
        if (preview)
        {

            AppWindow.ResizeClient(new SizeInt32((int)(520 * scale), (int)(520 * scale)));
            Root.Background = new SolidColorBrush(args.Contains("--dark") ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 243, 243, 243));
        }
        // Paint the entire borderless client, including pixels allocated by resizing.
        Root.Background=new SolidColorBrush(Dark ? Windows.UI.Color.FromArgb(255,32,32,32) : Windows.UI.Color.FromArgb(255,243,243,243));
        ApplyTheme();

        tray = new TrayIcon(hwnd, Path.Combine(AppContext.BaseDirectory, "app.ico"), TrayItems(), () => ShowFlyout(null, true), OnTrayMenu);
        tray.IsDark=()=>Dark;
        tray.NativeTip = true; tray.SetTip("Fluent Vantage Toolbar");
        AppWindow.Closing += (_, e) => { if (!exiting && !preview) { e.Cancel = true; HideFlyout(); } };
        Closed += (_, _) => { updateLifetime.Cancel();updateNotice?.Close();contextMenu?.Close();timer.Stop(); tray.Dispose(); };
        Activated += (_, e) => { if (e.WindowActivationState == WindowActivationState.Deactivated && !preview && !dialogOpen && !pageNavigating) HideFlyout(); };
        Root.ActualThemeChanged += (_, _) => { if (!preview) Render(); };
        timer.Tick += async (_, _) => { if (AppWindow.IsVisible && view == "main") await Refresh(); };
        Root.SizeChanged += (_, _) => DiagnosticLog.Write($"Layout dpi={GetDpiForWindow(hwnd)} outer={AppWindow.Size.Width}x{AppWindow.Size.Height} client={AppWindow.ClientSize.Width}x{AppWindow.ClientSize.Height} root={Root.ActualWidth}x{Root.ActualHeight} canvas={canvas.ActualWidth}x{canvas.ActualHeight}");
        Root.Loaded += async (_, _) => { if (preview) await Capture(); };

        Render();
        if (!preview)
        {
            timer.Start();
            _ = AutomaticUpdatesAsync(updateLifetime.Token);
            _ = Refresh();
            ListenForShowRequests();
            ListenForExitRequests();
            AppWindow.Hide();
        }
    }

    // ---- window behaviour ----
    void HideFromTaskbar()
    {
        try
        {
            AppWindow.IsShownInSwitchers = false;
            long style = GetWindowLongPtr(hwnd, -20).ToInt64();
            style = (style | 0x80L) & ~0x40000L; // WS_EX_TOOLWINDOW on, WS_EX_APPWINDOW off
            SetWindowLongPtr(hwnd, -20, new IntPtr(style));
        }
        catch { }
    }

    async void HideFlyout()
    {
        if(!AppWindow.IsVisible)return;
        int generation=++animationGeneration;lastHidden=DateTime.UtcNow;
        await AnimateFlyout(false);
        if(generation==animationGeneration){AppWindow.Hide();Root.Opacity=1;Root.RenderTransform=new TranslateTransform();}
    }
    Task AnimateFlyout(bool show)
    {
        flyoutMotion?.Stop();
        if(!MotionEnabled){Root.Opacity=1;Root.RenderTransform=new TranslateTransform();return Task.CompletedTask;}
        var transform=new TranslateTransform();Root.RenderTransform=transform;
        var motion=new Microsoft.UI.Xaml.Media.Animation.Storyboard();flyoutMotion=motion;
        double time=show?220:170;
        var easing=new Microsoft.UI.Xaml.Media.Animation.CubicEase{EasingMode=show?Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut:Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseIn};
        var slide=new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation{From=show?42:0,To=show?0:42,Duration=new Duration(TimeSpan.FromMilliseconds(time)),EasingFunction=easing,EnableDependentAnimation=true};
        var fade=new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation{From=show?0:1,To=show?1:0,Duration=new Duration(TimeSpan.FromMilliseconds(time)),EasingFunction=easing};
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(slide,transform);Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(slide,"Y");
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade,Root);Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade,"Opacity");motion.Children.Add(slide);motion.Children.Add(fade);motion.Begin();
        return Task.Delay((int)time);
    }

    async void ShowFlyout(string? target, bool toggle)
    {
        if (toggle && (AppWindow.IsVisible || (DateTime.UtcNow - lastHidden).TotalMilliseconds < 350)) { HideFlyout(); return; }
        ++animationGeneration;
        view = target ?? "main";
        Render();
        await geometryReady;
        if (!AppWindow.IsVisible && view != (target ?? "main")) return;
        if (tray.TryGetAnchor(out int x, out int y))
        {
            var area = DisplayArea.GetFromPoint(new PointInt32(x, y), DisplayAreaFallback.Nearest).WorkArea;
            int left = Math.Clamp(x - AppWindow.Size.Width / 2, area.X, Math.Max(area.X, area.X + area.Width - AppWindow.Size.Width));
            int top = Math.Clamp(y - AppWindow.Size.Height - 16, area.Y, Math.Max(area.Y, area.Y + area.Height - AppWindow.Size.Height));
            AppWindow.Move(new PointInt32(left, top));
        }
        else
        {
            var area = DisplayArea.Primary.WorkArea;
            AppWindow.Move(new PointInt32(area.X + area.Width - AppWindow.Size.Width - 12, area.Y + area.Height - AppWindow.Size.Height - 12));
            DiagnosticLog.Write("Tray anchor not ready; using taskbar work-area fallback.");
        }
        HideFromTaskbar();
        AppWindow.Show(); Activate(); SetForegroundWindow(hwnd);
        _ = AnimateFlyout(true);
        PositionTitleBar();
        _ = Refresh();
    }

    void ListenForShowRequests()
    {
        try
        {
            var signal = new EventWaitHandle(false, EventResetMode.AutoReset, Startup.ShowEventName);
            var queue = DispatcherQueue.GetForCurrentThread();
            var thread = new Thread(() => { while (true) { signal.WaitOne(); queue.TryEnqueue(() => ShowFlyout(null, false)); } }) { IsBackground = true };
            thread.Start();
        }
        catch { }
    }

    (int, string)[] TrayItems() => [(1, L.T("menu.open")), (2, L.T("menu.settings")), (5, UpdateLabel), (3, L.T("menu.about")), (4, L.Ar ? "إغلاق التطبيق" : "Close app")];

    void ExitApp(){++animationGeneration;exiting=true;dashboard=null;contextMenu?.Close();contextMenu=null;Close();}
    void ListenForExitRequests()
    {
        try {var signal=new EventWaitHandle(false,EventResetMode.AutoReset,Startup.ExitEventName);var queue=DispatcherQueue.GetForCurrentThread();new Thread(()=>{while(true){signal.WaitOne();queue.TryEnqueue(ExitApp);}}){IsBackground=true}.Start();}catch{}
    }
    bool OpenTrayContext(int x,int y){contextMenu?.Close();contextMenu=new TrayMenuWindow(settings.Theme,TrayItems(),OnTrayMenu,x,y);contextMenu.Closed+=(_,_)=>contextMenu=null;contextMenu.Activate();return true;}
    void OnTrayMenu(int id){if(id==5){ShowFlyout("about",false);_ = CheckForUpdatesAsync();return;}if(id==4){ExitApp();return;}ShowFlyout(id switch { 2 => "settings", 3 => "about", _ => "main" }, false);}

    public void OpenInitial() => ShowFlyout(null, false);

    void PositionTitleBar() => titleBar.Margin = new Thickness(0);

    void ApplyTheme()
    {
        ApplyAccent();
        Root.Background=new SolidColorBrush(Dark ? Windows.UI.Color.FromArgb(255,32,32,32) : Windows.UI.Color.FromArgb(255,243,243,243));
        Root.RequestedTheme = settings.Theme switch { "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        Root.FlowDirection = L.Ar ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    static Windows.UI.Color? ParseAccent(string? hex)
    {
        if(hex?.Length!=7 || hex[0]!='#' || !uint.TryParse(hex[1..],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out uint rgb))return null;
        return Windows.UI.Color.FromArgb(255,(byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb);
    }
    void ApplyAccent()
    {
        var resources=Application.Current.Resources;
        string[] names={"SystemAccentColor","SystemAccentColorLight1","SystemAccentColorLight2","SystemAccentColorLight3","SystemAccentColorDark1","SystemAccentColorDark2","SystemAccentColorDark3"};
        var color=ParseAccent(settings.AccentColor);
        foreach(string name in names){if(color.HasValue)resources[name]=color.Value;else resources.Remove(name);}
        string[] brushes={"SystemControlHighlightAccentBrush","AccentFillColorDefaultBrush","AccentFillColorSecondaryBrush","AccentFillColorTertiaryBrush","HyperlinkForeground","ToggleSwitchFillOn"};
        foreach(string name in brushes){if(color.HasValue)resources[name]=new SolidColorBrush(color.Value);else resources.Remove(name);}
    }
    UIElement AccentPicker()
    {
        var stack=new StackPanel { Spacing=10 };
        stack.Children.Add(Text(L.Ar ? "لون التطبيق" : "Accent color",14,true));
        var swatches=new StackPanel { Orientation=Orientation.Horizontal,Spacing=8 };
        foreach(string hex in new[]{"#0078D4","#00A6A6","#744DA9","#D83B01","#C239B3","#107C10"}) {
            var button=new Button { Width=40,Height=40,Padding=new Thickness(3),Background=new SolidColorBrush(ParseAccent(hex)!.Value),
                BorderBrush=Primary,BorderThickness=new Thickness(settings.AccentColor==hex?3:0),Content=settings.AccentColor==hex ? "✓" : "",Foreground=new SolidColorBrush(Colors.White) };
            ToolTipService.SetToolTip(button,hex);
            button.Click+=(_,_)=>{settings.AccentColor=hex;settings.Save();dashboard=null;Render();};swatches.Children.Add(button);
        }
        stack.Children.Add(swatches);
        var actions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=8 };
        var custom=new Button { Content=L.Ar ? "لون مخصص" : "Custom color" };
        custom.Click+=async(_,_)=>{
            var picker=new ColorPicker { IsAlphaEnabled=false,IsColorSpectrumVisible=true,IsColorSliderVisible=true,IsHexInputVisible=true,
                Color=ParseAccent(settings.AccentColor) ?? new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent),MaxWidth=380 };
            dialogOpen=true;
            try {
                var dialog=new ContentDialog { XamlRoot=Root.XamlRoot,Title=L.Ar ? "اختر لون التطبيق" : "Choose accent color",Content=picker,
                    PrimaryButtonText=L.Ar ? "تطبيق" : "Apply",CloseButtonText=L.Ar ? "إلغاء" : "Cancel",DefaultButton=ContentDialogButton.Primary };
                if(await dialog.ShowAsync()==ContentDialogResult.Primary){settings.AccentColor=$"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";settings.Save();dashboard=null;Render();}
            } finally {dialogOpen=false;}
        };
        var reset=new Button { Content=L.Ar ? "استعادة الافتراضي" : "Restore default" };
        reset.Click+=(_,_)=>{settings.AccentColor=null;settings.Save();dashboard=null;Render();};
        actions.Children.Add(custom);actions.Children.Add(reset);stack.Children.Add(actions);
        stack.Children.Add(Text(settings.AccentColor ?? (L.Ar ? "لون Windows الافتراضي" : "Windows default accent"),12,false,SecondaryText));
        return Card(stack,new Thickness(14));
    }

    // ---- device ----
    async Task Refresh()
    {
        if (busy || preview) return;
        busy = true;
        try { current = await device.ReadAsync(); Apply(); }
        catch (Exception e) { if (statusText != null) statusText.Text = e.Message; }
        finally { busy = false; }
    }

    async Task Run(Func<Task> action)
    {
        if (busy || preview || current == null) return;
        busy = true;
        foreach (var button in tileButtons.Values) button.IsEnabled = false;
        try { await action(); current = await device.ReadAsync(); Apply(); }
        catch (Exception e) { DiagnosticLog.Write("Control failed: " + e); current = await device.ReadAsync(); Apply(); dialogOpen = true; try { await new ContentDialog { XamlRoot = Root.XamlRoot, Title = L.T("notconfirmed"), Content = (L.Ar ? "لم يتأكد تغيير الإعداد. أعد المحاولة، وإذا استمرت المشكلة افتح سجل التشخيص من الإعدادات وأرسله للدعم." : "The setting change could not be confirmed. Try again. If it continues, open the diagnostic log in Settings and send it to support."), CloseButtonText = "OK" }.ShowAsync(); } finally { dialogOpen = false; } }
        finally { busy = false; }
    }

    static void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }

    async Task ConfirmTouchpadAsync(bool lockNow)
    {
        if (lockNow)
        {
            dialogOpen = true;
            try
            {
                var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = L.T("dlg.touchpad.title"), Content = L.T("dlg.touchpad.body"), PrimaryButtonText = L.T("dlg.lock"), CloseButtonText = L.T("dlg.cancel"), DefaultButton = ContentDialogButton.Close };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) { await Refresh(); return; }
            }
            finally { dialogOpen = false; }
        }
        await Run(() => device.SetTouchpadAsync(lockNow));
    }

    async void OnTile(string id)
    {
        DiagnosticLog.Write($"Tile clicked: {id}, busy={busy}, stateLoaded={current != null}");
        if (busy || current == null) { Apply(); return; }
        var c = current;
        switch (id)
        {
            case "fn": await Run(() => device.SetFnAsync(!(c?.FnLocked ?? false))); break;
            case "usb": await Run(() => device.SetUsbAsync(c?.UsbMode != 2)); break;
            case "refresh":
                var rates = c?.AvailableHz?.OrderBy(x => x).ToArray();
                if (rates is { Length: >= 2 }) await Run(() => device.SetRefreshAsync(c?.RefreshHz == rates[^1] ? rates[0] : rates[^1]));
                break;
            case "mic": await Run(() => device.SetMicrophoneAsync(!(c?.Muted ?? false))); break;
            case "conserve": await Run(() => device.SetModeAsync(c?.Mode == ChargeMode.Conservation ? ChargeMode.Normal : ChargeMode.Conservation)); break;
            case "rapid": await Run(() => device.SetModeAsync(c?.Mode == ChargeMode.Rapid ? ChargeMode.Normal : ChargeMode.Rapid)); break;
            case "touchpad": await ConfirmTouchpadAsync(c?.TouchpadLocked == false); break;
        }
    }

    // ---- colours ----
    bool Dark => Root.ActualTheme == ElementTheme.Dark;
    static SolidColorBrush Rgb(byte r, byte g, byte b, byte a = 255) => new(Windows.UI.Color.FromArgb(a, r, g, b));
    Brush SecondaryText => Dark ? Rgb(255, 255, 255, 0xC5) : Rgb(0, 0, 0, 0x9E);
    Brush CardBackground => Dark ? Rgb(255, 255, 255, 0x0D) : Rgb(255, 255, 255, 0xB3);
    Brush CardStroke => Dark ? Rgb(255, 255, 255, 0x29) : Rgb(0, 0, 0, 0x0F);
    Brush Primary => Dark ? Rgb(255, 255, 255) : Rgb(0, 0, 0, 0xE4);
    static Windows.UI.Color BatteryColorFor(int? percent) => percent switch
    {
        null => Windows.UI.Color.FromArgb(255, 156, 218, 155),
        <= 5 => Windows.UI.Color.FromArgb(255, 255, 153, 164),   // Windows 11 critical
        <= 20 => Windows.UI.Color.FromArgb(255, 252, 225, 0),    // Windows 11 caution
        _ => Windows.UI.Color.FromArgb(255, 156, 218, 155),      // green sampled from the supplied Windows 11 tray battery
    };

    // ---- view building ----
    static FontIcon Glyph(string glyph, double size) => new() { Glyph = glyph, FontSize = size, FontFamily = new FontFamily("Segoe Fluent Icons") };

    TextBlock Text(string text, double size = 14, bool bold = false, Brush? brush = null) =>
        new() { Text = text, FontSize = size, FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, Foreground = brush ?? Primary, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };

    Border Card(UIElement child, Thickness padding) =>
        new() { Child = child, Padding = padding, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = CardStroke, Background = CardBackground };

    Button IconButton(string glyph, string tip, Action click)
    {
        var icon = Glyph(glyph, 18); icon.MirroredWhenRightToLeft = glyph == "\uE72B";
        var button = new Button { Content = icon, Width = 36, Height = 36, Padding = new Thickness(0), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    bool pageNavigating;
    Image? outgoingPage;

    Brush? navigationBackground;
    bool MotionEnabled => preview ? args.Contains("--interactive") : new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

    async void Navigate(string target) => await NavigateAsync(target);

    async Task NavigateAsync(string target)
    {
        if(pageNavigating || target==view)return;
        pageNavigating=true;
        bool animate=AppWindow.IsVisible && MotionEnabled;
        try {
            await geometryReady;
            if(animate) {
                navigationBackground=Root.Background;
                Root.Background=new SolidColorBrush(Dark ? Windows.UI.Color.FromArgb(255,32,32,32) : Windows.UI.Color.FromArgb(255,243,243,243));
                // Keep the outgoing page painted while the incoming page is arranged.
                var snapshot=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
                await snapshot.RenderAsync(canvas);
                outgoingPage=new Image { Source=snapshot,Width=canvas.ActualWidth,Height=canvas.ActualHeight,
                    Stretch=Stretch.Fill,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false };
                Root.Children.Add(outgoingPage);
            }
            view=target;
            Render(animate);
            await geometryReady;
            if(target=="battery" && !preview)_ = LoadBatteryAsync();
            if(target=="warranty" && !preview)_ = LoadWarrantyAsync(false);
        } catch(Exception error) { DiagnosticLog.Write("Page navigation failed: "+error); if(preview)throw; }
        finally {
            if(outgoingPage!=null){Root.Children.Remove(outgoingPage);outgoingPage=null;}
            canvas.Opacity=1;canvas.RenderTransform=new TranslateTransform();canvas.Clip=null;
            if(animate){await NextFrameAsync();Root.Background=navigationBackground;}
            canvas.Margin=new Thickness(0);SetWindowRgn(hwnd,IntPtr.Zero,true);
            pageNavigating=false;
        }
    }

    void Render(bool animatePage=false)
    {
        canvas.Margin=new Thickness(0);SetWindowRgn(hwnd,IntPtr.Zero,true);
        L.Set(settings.Language);
        ApplyTheme();
        // Keep an expanding HWND off screen until its new XAML surface is painted.
        restoreAfterGeometry=!animatePage && AppWindow.IsVisible && (!preview || args.Contains("--interactive"));
        if(restoreAfterGeometry)AppWindow.Hide();
        if (tray != null) tray.SetItems(TrayItems());
        titleBar.Children.Clear(); titleBar.ColumnDefinitions.Clear();
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        string titleText = view switch { "settings" => L.T("settings.title"), "about" => L.T("about.title"), "battery" => L.T("bat.title"), "warranty" => L.T("war.title"), "device" => L.Ar ? "عن جهازك" : "About your device", _ => L.T("title") };
        if (view != "main")
        {
            var back = IconButton("\uE72B", L.T("back"), () => Navigate("main"));
            titleBar.Children.Add(back);
        }
        var title = Text(titleText, 20, true);
        title.Margin = new Thickness(view == "main" ? 0 : 4, 0, 0, 0);
        Grid.SetColumn(title, 1); titleBar.Children.Add(title);
        if (view == "main")
        {
            var gear = IconButton("\uE713", L.T("settings.gear"), () => Navigate("settings"));
            Grid.SetColumn(gear, 2); titleBar.Children.Add(gear);
        }
        var close = IconButton("\uE8BB", L.Ar ? "إغلاق النافذة" : "Close window", HideFlyout);
        Grid.SetColumn(close, 3); titleBar.Children.Add(close);
        PositionTitleBar();
        tileButtons.Clear();updateButtons.Clear();
        body.Content = view switch { "settings" => BuildSettings(), "about" => BuildAbout(), "battery" => BuildBattery(), "warranty" => BuildWarranty(), "device" => dashboard ??= new DeviceDashboardWindow(settings.Theme,current,preview), _ => BuildMain() };
        if (view == "main") Apply();
        if(animatePage){canvas.Opacity=0;canvas.RenderTransform=new TranslateTransform();}
        ResizeForContent(animatePage);
    }

    bool restoreAfterGeometry;
    Task geometryReady=Task.CompletedTask;
    readonly SemaphoreSlim geometryLock=new(1,1);
    int geometryGeneration;
    int nativeResizeCount;
    double frameWidthPixels=double.NaN,frameHeightPixels=double.NaN;

    void ResizeForContent(bool animatePage=false) => geometryReady=FitGeometryAsync(++geometryGeneration,animatePage);

    static Task NextFrameAsync()
    {
        var done=new TaskCompletionSource<bool>();
        EventHandler<object>? handler=null;
        handler=(_,_)=>{CompositionTarget.Rendering-=handler;done.TrySetResult(true);};
        CompositionTarget.Rendering+=handler;
        return WaitFrameAsync(done.Task,()=>CompositionTarget.Rendering-=handler);
    }
    static async Task WaitFrameAsync(Task frame,Action cleanup)
    {
        // Hidden windows can stop compositor ticks. Do not leave geometry blocked.
        await Task.WhenAny(frame,Task.Delay(34));cleanup();
    }

    async Task FitGeometryAsync(int generation,bool animatePage=false)
    {
        await geometryLock.WaitAsync();
        try {
            if(generation!=geometryGeneration)return;
            // Finish templates and arrange natural content before touching the HWND.
            await NextFrameAsync();
            if(generation!=geometryGeneration)return;
            Root.UpdateLayout();
            double scale=Math.Max(1,GetDpiForWindow(hwnd)/96d);
            if(double.IsNaN(frameHeightPixels)) {
                frameWidthPixels=AppWindow.Size.Width-Root.ActualWidth*scale;
                frameHeightPixels=AppWindow.Size.Height-Root.ActualHeight*scale;
            }
            canvas.Height=double.NaN;
            canvas.RowDefinitions[1].Height=view=="main"?GridLength.Auto:new GridLength(1,GridUnitType.Star);
            canvas.Measure(new Windows.Foundation.Size(472,double.PositiveInfinity));
            double naturalHeight=view=="main"?canvas.DesiredSize.Height:520+Math.Max(36,titleBar.DesiredSize.Height);
            canvas.Arrange(new Windows.Foundation.Rect(24,15,472,naturalHeight));
            double wanted=naturalHeight+Root.Padding.Top+Root.Padding.Bottom;
            if(view=="main" && body.Content is StackPanel main) {
                var last=(FrameworkElement)main.Children.Last();
                double ink=await PaintedHeightAsync(last);
                wanted=last.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0,ink)).Y-canvas.Margin.Top+24;
            }
            if(generation!=geometryGeneration)return;
            var work=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Nearest).WorkArea;
            wanted=Math.Min(wanted,work.Height/scale-16);
            var desired=new SizeInt32((int)Math.Round(520*scale+frameWidthPixels),(int)Math.Round(wanted*scale+frameHeightPixels));
            if(animatePage) {
                // Anchor the bottom edge. One atomic rect update per frame avoids
                // a resize/move mismatch; the snapshot covers layout preparation.
                var pos=AppWindow.Position;var actual=AppWindow.Size;
                var visible=new RectInt32(pos.X,pos.Y,actual.Width,actual.Height);
                var start=new PointInt32(visible.X,visible.Y);var size=new SizeInt32(visible.Width,visible.Height);
                int bottom=start.Y+size.Height;
                int left=Math.Clamp(start.X+(size.Width-desired.Width)/2,work.X,Math.Max(work.X,work.X+work.Width-desired.Width));
                int top=Math.Clamp(bottom-desired.Height,work.Y,Math.Max(work.Y,work.Y+work.Height-desired.Height));
                await AnimatePageGeometryAsync(generation,new RectInt32(left,top,desired.Width,desired.Height));
            } else {
                if(AppWindow.Size.Width!=desired.Width || AppWindow.Size.Height!=desired.Height) {
                    AppWindow.Resize(desired);nativeResizeCount++;
                }
                await NextFrameAsync();Root.UpdateLayout();await NextFrameAsync();
                if(!preview && tray!=null && tray.TryGetAnchor(out int x,out int y)) {
                    var area=DisplayArea.GetFromPoint(new PointInt32(x,y),DisplayAreaFallback.Nearest).WorkArea;
                    int left=Math.Clamp(x-AppWindow.Size.Width/2,area.X,Math.Max(area.X,area.X+area.Width-AppWindow.Size.Width));
                    int top=Math.Clamp(y-AppWindow.Size.Height-16,area.Y,Math.Max(area.Y,area.Y+area.Height-AppWindow.Size.Height));
                    if(AppWindow.Position.X!=left || AppWindow.Position.Y!=top)AppWindow.Move(new PointInt32(left,top));
                }
            }
            if(restoreAfterGeometry && generation==geometryGeneration){restoreAfterGeometry=false;AppWindow.Show();}
        } finally {geometryLock.Release();}
    }

    async Task AnimatePageGeometryAsync(int generation,RectInt32 target)
    {
        var position=AppWindow.Position;var size=AppWindow.Size;
        var start=new RectInt32(position.X,position.Y,size.Width,size.Height);
        const int duration=300;
        var transform=new TranslateTransform { Y=96 };canvas.RenderTransform=transform;
        var motion=new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var ease=new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode=Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut };
        void Add(DependencyObject element,string property,double from,double to) {
            var animation=new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { From=from,To=to,Duration=new Duration(TimeSpan.FromMilliseconds(duration)),EasingFunction=ease,EnableDependentAnimation=true };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation,element);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation,property);motion.Children.Add(animation);
        }
        Add(transform,"Y",96,0);Add(canvas,"Opacity",0,1);
        if(outgoingPage!=null)Add(outgoingPage,"Opacity",1,0);
        canvas.Margin=new Thickness(0);SetWindowRgn(hwnd,IntPtr.Zero,true);
        motion.Begin();
        var clock=Stopwatch.StartNew();int steps=0;
        var trace=new List<string>();
        while(generation==geometryGeneration && AppWindow.IsVisible && !exiting) {
            double t=Math.Clamp(clock.Elapsed.TotalMilliseconds/duration,0,1);
            double eased=1-Math.Pow(1-t,3);
            int Mix(int a,int b)=>(int)Math.Round(a+(b-a)*eased);
            var rect=new RectInt32(Mix(start.X,target.X),Mix(start.Y,target.Y),Mix(start.Width,target.Width),Mix(start.Height,target.Height));
            if(t>0) {
                AppWindow.MoveAndResize(rect);nativeResizeCount++;steps++;
            }
            Root.UpdateLayout();
            RedrawWindow(hwnd,IntPtr.Zero,IntPtr.Zero,0x0185);
            canvas.Clip=new RectangleGeometry { Rect=new Windows.Foundation.Rect(0,-96,canvas.ActualWidth,Math.Max(canvas.ActualHeight,Root.ActualHeight)) };
            trace.Add($"{clock.Elapsed.TotalMilliseconds:F1},{rect.X},{rect.Y},{rect.Width},{rect.Height},{rect.Y+rect.Height}");
            if(t>=1)break;
            await Task.Delay(10);await NextFrameAsync();
        }
        if(generation==geometryGeneration && AppWindow.IsVisible) {
            AppWindow.MoveAndResize(target);canvas.Margin=new Thickness(0);SetWindowRgn(hwnd,IntPtr.Zero,true);
            Root.UpdateLayout();await NextFrameAsync();
        }
        motion.Stop();canvas.Opacity=1;transform.Y=0;

        DiagnosticLog.Write($"Page motion {view}: {start.Height}->{target.Height}; {steps} atomic geometry steps; {clock.ElapsedMilliseconds}ms; dpi={GetDpiForWindow(hwnd)}");
        if(preview && args.Contains("--interactive")) {
            var dir=Path.GetDirectoryName(args[Array.IndexOf(args,"--capture")+1])!;
            File.AppendAllLines(Path.Combine(dir,"navigation-geometry.csv"),new[]{$"PAGE,{view},{start.Height},{target.Height},{steps},{clock.ElapsedMilliseconds}"}.Concat(trace));
            if(start.Height!=target.Height && steps<3)throw new InvalidOperationException("Page size changed without intermediate animation frames");
            if(AppWindow.IsVisible && (AppWindow.Size.Height!=target.Height || AppWindow.Position.Y!=target.Y))throw new InvalidOperationException("Page animation missed its final bounds");
            if(!GetWindowRect(hwnd,out var actual) || actual.Left!=target.X || actual.Top!=target.Y || actual.Right-actual.Left!=target.Width || actual.Bottom-actual.Top!=target.Height)throw new InvalidOperationException("Native HWND retained larger bounds");
            var region=CreateRectRgn(0,0,0,0);int regionKind=GetWindowRgn(hwnd,region);DeleteObject(region);
            if(regionKind!=0 || canvas.Margin.Top!=0 || canvas.Margin.Bottom!=0)throw new InvalidOperationException("Navigation retained region or canvas offset");
            File.AppendAllText(Path.Combine(dir,"navigation-native-bounds.csv"),$"{view},{actual.Left},{actual.Top},{actual.Right},{actual.Bottom},region={regionKind},offset={canvas.Margin.Top},dpi={GetDpiForWindow(hwnd)}\n");
        }
    }

    async Task<double> PaintedHeightAsync(FrameworkElement element)
    {
        var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var buffer=await bitmap.GetPixelsAsync();
        using var reader=Windows.Storage.Streams.DataReader.FromBuffer(buffer);
        var pixels=new byte[buffer.Length];reader.ReadBytes(pixels);
        for(int y=bitmap.PixelHeight-1;y>=0;y--)for(int x=0;x<bitmap.PixelWidth;x++)
            if(pixels[(y*bitmap.PixelWidth+x)*4+3]>16)
                return (y+1)*element.ActualHeight/bitmap.PixelHeight;
        return element.ActualHeight;
    }

    UIElement HeaderRow(string header, string? linkText, Action? click)
    {
        var grid = new Grid();
        grid.Children.Add(Text(header, 14, true));
        if (linkText != null && click != null)
        {
            var link = new HyperlinkButton { Content = linkText, HorizontalAlignment = HorizontalAlignment.Right };
            link.Click += (_, _) => click();
            grid.Children.Add(link);
        }
        return grid;
    }

    UIElement BuildMain()
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(HeaderRow(L.T("battery.header"), settings.ShowBatteryDetails ? L.T("battery.link") : null, () => Navigate("battery")));

        // Battery graphic: always laid out left to right so the fill grows from the left in both languages.
        var graphic = new Grid { Height = 100, Margin = new Thickness(12, 0, 12, 0), FlowDirection = FlowDirection.LeftToRight };
        graphic.ColumnDefinitions.Add(new ColumnDefinition());
        graphic.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        var outline = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8), BorderBrush = SecondaryText, Padding = new Thickness(5) };
        var inner = new Grid();
        batteryFill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        inner.Children.Add(batteryFill);
        plugIcon = Glyph("\uE945", 28);
        percentText = Text("--%", 44, true);
        percentText.TextWrapping = TextWrapping.NoWrap;
        var centre = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        centre.Children.Add(plugIcon); centre.Children.Add(percentText);
        inner.Children.Add(centre);
        inner.SizeChanged += (_, _) => UpdateBatteryFill();
        outline.Child = inner;
        graphic.Children.Add(outline);
        var cap = new Border { Background = SecondaryText, Height = 30, CornerRadius = new CornerRadius(0, 3, 3, 0) };
        Grid.SetColumn(cap, 1); graphic.Children.Add(cap);
        batteryFillHost = inner;
        stack.Children.Add(graphic);
        chargeText = Text("", 12, false, SecondaryText);
        stack.Children.Add(chargeText);

        stack.Children.Add(HeaderRow(L.T("quick.header"), L.Ar ? "عن جهازك" : "About your device", OpenDashboard));
        var visible = Tiles.Where(t => settings.IsTileVisible(t.Id)).ToList();
        var rows = new StackPanel { Spacing = 14 };
        for (int i = 0; i < visible.Count; i += 5)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var tile in visible.Skip(i).Take(5)) row.Children.Add(MakeTile(tile));
            rows.Children.Add(row);
        }
        if (visible.Count > 0) stack.Children.Add(Card(rows, new Thickness(8, 16, 8, 16)));

        statusText = Text("", 12, false, SecondaryText);
        statusText.MaxHeight = 60;
        // Availability details belong in Settings, not the main flyout.
        if (settings.ShowWarranty)
        {
            var warrantyLink = new HyperlinkButton { Content = L.T("warranty.link"), HorizontalAlignment = HorizontalAlignment.Right, Padding=new Thickness(0), MinHeight=0, VerticalAlignment=VerticalAlignment.Top };
            warrantyLink.Click += (_, _) => Navigate("warranty");
            stack.Children.Add(warrantyLink);
        }
        return stack;
    }

    FrameworkElement MakeTile(TileDef tile)
    {
        var button = new ToggleButton { Width = 56, Height = 56, CornerRadius = new CornerRadius(28), HorizontalAlignment = HorizontalAlignment.Center, IsEnabled = false, Padding = new Thickness(0) };
        button.Resources["ToggleButtonBackgroundChecked"] = ParseAccent(settings.AccentColor) is {} accent ? new SolidColorBrush(accent) : Rgb(156, 218, 155);
        button.Resources["ToggleButtonBackgroundCheckedPointerOver"] = Rgb(140, 205, 139);
        button.Resources["ToggleButtonBackgroundCheckedPressed"] = Rgb(123, 190, 123);
        button.Resources["ToggleButtonForegroundChecked"] = Rgb(20, 55, 27);
        button.Resources["ToggleButtonForegroundCheckedPointerOver"] = Rgb(20, 55, 27);
        button.Resources["ToggleButtonForegroundCheckedPressed"] = Rgb(20, 55, 27);
        if (tile.Id == "fn") button.Content = MakeFnIcon();
        else if(tile.Id=="touchpad")button.Content=TouchpadIcon(false,Primary);
        else button.Content = Glyph(tile.Glyph, 24);
        button.Click += (_, _) => OnTile(tile.Id);
        tileButtons[tile.Id] = button;
        var label = Text(L.T(tile.LabelKey), 10.5);
        label.TextAlignment = TextAlignment.Center; label.HorizontalAlignment = HorizontalAlignment.Center; label.MaxLines = 2;
        var panel = new StackPanel { Width = 86, Spacing = 8 };
        panel.Children.Add(button); panel.Children.Add(label);
        return panel;
    }

    UIElement MicOffIcon(Brush ink)
    {
        var icon=(Microsoft.UI.Xaml.Shapes.Path)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='24' Height='24' Stretch='Uniform' Data='M3.28034 2.21968C2.98745 1.92678 2.51257 1.92677 2.21968 2.21966C1.92678 2.51255 1.92677 2.98743 2.21966 3.28032L8 9.06078V12C8 14.2091 9.79086 16 12 16C12.8335 16 13.6074 15.7451 14.2481 15.309L15.394 16.4549C14.5176 17.1112 13.4292 17.5 12.25 17.5H11.75L11.5336 17.4956C8.73445 17.3821 6.5 15.077 6.5 12.25V11.75L6.49315 11.6482C6.44349 11.2822 6.1297 11 5.75 11C5.33579 11 5 11.3358 5 11.75V12.25L5.00406 12.4863C5.12283 15.938 7.83323 18.7316 11.25 18.9818L11.25 21.25L11.2568 21.3518C11.3065 21.7178 11.6203 22 12 22C12.4142 22 12.75 21.6642 12.75 21.25L12.751 18.9817C14.15 18.8791 15.4305 18.35 16.4631 17.5241L20.7194 21.7805C21.0123 22.0734 21.4872 22.0734 21.7801 21.7805C22.073 21.4876 22.073 21.0127 21.7801 20.7198L3.28034 2.21968ZM13.1562 14.2171C12.8105 14.3978 12.4172 14.5 12 14.5C10.6193 14.5 9.5 13.3807 9.5 12V10.5608L13.1562 14.2171ZM14.5 6V11.3182L15.9301 12.7483C15.976 12.5059 16 12.2558 16 12V6C16 3.79086 14.2091 2 12 2C10.1521 2 8.59692 3.25302 8.13768 4.95575L9.5 6.3181V6C9.5 4.61929 10.6193 3.5 12 3.5C13.3807 3.5 14.5 4.61929 14.5 6ZM17.1962 14.0144L18.3421 15.1604C18.7638 14.2791 19 13.2921 19 12.25V11.75L18.9932 11.6482C18.9435 11.2822 18.6297 11 18.25 11C17.8358 11 17.5 11.3358 17.5 11.75V12.25L17.4956 12.4664C17.4737 13.0075 17.3698 13.5276 17.1962 14.0144Z' />");
        icon.Fill=ink;return icon;
    }
    UIElement TouchpadIcon(bool off,Brush ink)
    {
        var grid=new Grid { Width=26,Height=26,FlowDirection=FlowDirection.LeftToRight };
        var icon=Glyph("\uEFA5",24);icon.Foreground=ink;grid.Children.Add(icon);
        if(off)grid.Children.Add(new Microsoft.UI.Xaml.Shapes.Line { X1=3,Y1=3,X2=23,Y2=23,Stroke=ink,StrokeThickness=2,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round });
        return grid;
    }

    Microsoft.UI.Xaml.Shapes.Path? fnShackle;
    UIElement MakeFnIcon()
    {
        var grid = new Grid { Width = 34, Height = 36, IsHitTestVisible = false, FlowDirection = FlowDirection.LeftToRight };
        fnShackle = new Microsoft.UI.Xaml.Shapes.Path { StrokeThickness = 2, Stroke = Primary, Data = LockShackle(false) };
        grid.Children.Add(fnShackle);
        grid.Children.Add(new Border { Width = 25, Height = 21, BorderThickness = new Thickness(2), BorderBrush = Primary, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom });
        grid.Children.Add(new TextBlock { Text = "Fn", FontFamily = new FontFamily("Segoe UI"), FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Primary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3), TextWrapping = TextWrapping.NoWrap });
        return grid;
    }
    static PathGeometry LockShackle(bool locked)
    {
        var figure = new PathFigure { StartPoint = new Windows.Foundation.Point(9, 16), IsClosed = false };
        figure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(9, 10) });
        figure.Segments.Add(new BezierSegment { Point1 = new Windows.Foundation.Point(9, 1), Point2 = new Windows.Foundation.Point(25, 1), Point3 = new Windows.Foundation.Point(25, 10) });
        if (locked) figure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(25, 16) });
        var geometry = new PathGeometry(); geometry.Figures.Add(figure); return geometry;
    }

    void UpdateBatteryFill()
    {
        if (batteryFill == null || batteryFillHost == null) return;
        int? percent = current?.Percent;
        batteryColor = BatteryColorFor(percent);
        batteryFill.Background = new SolidColorBrush(batteryColor);
        batteryFill.Width = percent.HasValue ? Math.Max(0, batteryFillHost.ActualWidth * percent.Value / 100d) : 0;
        var onFill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
        bool centreOnFill = (percent ?? 0) >= 50;
        if (percentText != null) percentText.Foreground = onFill;
        if (plugIcon != null) plugIcon.Foreground = onFill;
    }

    void Apply()
    {
        if (view != "main" || percentText == null) return;
        var value = current;
        if (value == null) { if (statusText != null) statusText.Text = preview ? L.T("preview") : ""; UpdateBatteryFill(); return; }
        percentText.Text = value.Percent.HasValue ? $"{value.Percent}%" : "--%";
        plugIcon!.Visibility = value.Plugged ? Visibility.Visible : Visibility.Collapsed;
        chargeText!.Text = value.Charging ? L.T("charging") : value.Plugged ? L.T("plugged") : L.T("onbattery");
        UpdateBatteryFill();
        void Set(string id, bool? state, bool supported, string? tip)
        {
            if (!tileButtons.TryGetValue(id, out var button)) return;
            button.IsEnabled = supported && !preview;
            button.IsChecked = state ?? false;
            if (button.Content is FontIcon glyph) glyph.Foreground = state == true ? (ParseAccent(settings.AccentColor).HasValue ? Rgb(255,255,255) : Rgb(20,55,27)) : Primary;
            if (id == "fn" && button.Content is Grid lockGrid) foreach (var child in lockGrid.Children) {
                Brush ink = state == true ? (ParseAccent(settings.AccentColor).HasValue ? Rgb(255,255,255) : Rgb(20,55,27)) : Primary;
                if (child is Microsoft.UI.Xaml.Shapes.Path path) path.Stroke = ink;
                if (child is Border outline) outline.BorderBrush = ink;
                if (child is TextBlock fnLabel) fnLabel.Foreground = ink;
            }
            if (tip != null) ToolTipService.SetToolTip(button, tip);
        }
        Set("fn", value.FnLocked, value.FnLocked.HasValue, L.T("tip.fn"));
        if (fnShackle != null) fnShackle.Data = LockShackle(value.FnLocked == true);
        Set("usb", value.UsbMode == 2, value.UsbMode.HasValue, L.T("tip.usb"));
        var panelRates = value.AvailableHz?.OrderBy(x => x).ToArray() ?? [];
        bool exactRates = panelRates.Length >= 2;
        Set("refresh", exactRates && value.RefreshHz == panelRates[^1], exactRates, exactRates ? L.T("tip.refresh.ok") : L.T("tip.refresh.no"));
        Set("mic", value.Muted, value.Muted.HasValue, L.T("tip.mic"));
        Set("conserve", value.Mode == ChargeMode.Conservation, value.Mode.HasValue, L.T("tip.conserve"));
        Set("rapid", value.Mode == ChargeMode.Rapid, value.Mode.HasValue, L.T("tip.rapid"));
        Set("touchpad", value.TouchpadLocked.HasValue ? !value.TouchpadLocked.Value : null, value.TouchpadLocked.HasValue, L.T("tip.touchpad"));
        var checkedInk=ParseAccent(settings.AccentColor).HasValue ? Rgb(255,255,255) : Rgb(20,55,27);
        if(tileButtons.TryGetValue("mic",out var microphone))microphone.Content=value.Muted==true ? MicOffIcon(checkedInk) : Glyph("\uE720",24);
        if(tileButtons.TryGetValue("touchpad",out var pad))pad.Content=TouchpadIcon(value.TouchpadLocked==true,value.TouchpadLocked==false ? checkedInk : Primary);
        if (tileButtons.TryGetValue("refresh", out var refreshButton) && refreshButton.Parent is StackPanel panel && panel.Children.Count > 1 && panel.Children[1] is TextBlock label)
            label.Text = exactRates ? $"{panelRates[0]} / {panelRates[^1]} Hz" : value.RefreshHz.HasValue ? $"{value.RefreshHz} Hz" : "-- Hz";
        if (statusText != null) statusText.Text = preview ? L.T("preview") : value.Problems.Length > 0 ? L.T("status.some") : L.T("status.ok");
    }

    void OpenDashboard(){dashboard=new DeviceDashboardWindow(settings.Theme,current,preview);Navigate("device");}

    string UpdateLabel => L.Ar ? "التحقق من التحديثات" : "Check for updates";
    Button UpdateButton()
    {
        var button=new Button { Content=checkingUpdates ? (L.Ar ? "جارٍ التحقق..." : "Checking...") : UpdateLabel, IsEnabled=!checkingUpdates, HorizontalAlignment=HorizontalAlignment.Stretch };
        button.Click+=async (_,_)=>await CheckForUpdatesAsync();updateButtons.Add(button);return button;
    }
    static Version? ReleaseVersion(string? tag) => Version.TryParse(tag?.TrimStart('v','V'),out var version) ? version : null;
    static string? UpdateDownload(System.Text.Json.JsonElement release, Version installed,bool bundled=true)
    {
        if(release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())return null;
        var next=ReleaseVersion(release.GetProperty("tag_name").GetString());
        if(next==null || next<=installed)return null;
        foreach(var asset in release.GetProperty("assets").EnumerateArray()) {
            var name=asset.GetProperty("name").GetString();
            if(name!=$"FluentVantageToolbar-{next.ToString(3)}-Setup{(bundled ? "-with-dotnet" : "")}.exe")continue;
            var url=asset.GetProperty("browser_download_url").GetString();
            if(Uri.TryCreate(url,UriKind.Absolute,out var uri) && uri.Scheme=="https" && uri.Host=="github.com" && uri.AbsolutePath.StartsWith("/MohamedElnaggar00/Fluent-Vantage-Toolbar/releases/download/",StringComparison.Ordinal))return url;
        }
        throw new InvalidOperationException("New release has no installer matching this build flavor.");
    }
    // Self-contained .NET builds carry coreclr.dll beside the executable;
    // framework-dependent builds load it from the installed shared runtime.
    bool InstalledBundled => File.Exists(Path.Combine(AppContext.BaseDirectory,"coreclr.dll"));
    async Task AutomaticUpdatesAsync(CancellationToken token)
    {
        try {
            await Task.Delay(TimeSpan.FromSeconds(45),token);
            while(!token.IsCancellationRequested) {
                if(settings.AutoCheckUpdates)await CheckAutomaticUpdateAsync();
                await Task.Delay(TimeSpan.FromHours(6),token);
            }
        } catch(OperationCanceledException) { }
    }
    async Task CheckAutomaticUpdateAsync()
    {
        if(preview || exiting || checkingUpdates || !settings.AutoCheckUpdates)return;
        checkingUpdates=true;
        try {
            using var http=new System.Net.Http.HttpClient { Timeout=TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FluentVantageToolbar/"+(typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"));
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var response=await http.GetAsync("https://api.github.com/repos/MohamedElnaggar00/Fluent-Vantage-Toolbar/releases/latest",updateLifetime.Token);
            response.EnsureSuccessStatusCode();
            using var json=System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(updateLifetime.Token));
            var installed=ReleaseVersion(typeof(MainWindow).Assembly.GetName().Version?.ToString(3)) ?? new Version(0,0,0);
            var url=UpdateDownload(json.RootElement,installed,InstalledBundled);
            var tag=json.RootElement.GetProperty("tag_name").GetString();
            if(url==null || !settings.AutoCheckUpdates || exiting || tag==settings.LastNotifiedUpdate)return;
            ShowUpdateNotice(tag ?? "",url);
            settings.LastNotifiedUpdate=tag;settings.Save();
        } catch(OperationCanceledException) { }
        catch(Exception error) { DiagnosticLog.Write("Automatic update check failed: "+error.Message); }
        finally {checkingUpdates=false;}
    }
    void ShowUpdateNotice(string version,string url)
    {
        updateNotice?.Close();
        updateNotice=new UpdateNoticeWindow(version,url,L.Ar,Dark,preview);
        if(!preview) {
            tray.Notify(L.Ar ? "يتوفر تحديث جديد" : "Update available",$"Fluent Vantage Toolbar {version}");
            updateNotice.ShowNotice();
        }
    }

    async Task CheckForUpdatesAsync()
    {
        if(checkingUpdates || preview)return;
        checkingUpdates=true;
        foreach(var button in updateButtons){button.IsEnabled=false;button.Content=L.Ar ? "جارٍ التحقق..." : "Checking...";}
        string message;
        try {
            using var http=new System.Net.Http.HttpClient { Timeout=TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FluentVantageToolbar/"+(typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"));
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var response=await http.GetAsync("https://api.github.com/repos/MohamedElnaggar00/Fluent-Vantage-Toolbar/releases/latest");
            response.EnsureSuccessStatusCode();
            using var json=System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var installed=ReleaseVersion(typeof(MainWindow).Assembly.GetName().Version?.ToString(3)) ?? new Version(0,0,0);
            var url=UpdateDownload(json.RootElement,installed,InstalledBundled);
            if(url==null)message=L.Ar ? "أنت تستخدم أحدث إصدار." : "You're using the latest release.";
            else {
                Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
                message=L.Ar ? "تم فتح رابط تنزيل التحديث في المتصفح. بعد اكتمال التنزيل، شغّل المثبّت للتحديث." : "Opened the update download in your browser. When the download finishes, run the installer to update.";
            }
        } catch(Exception error) {
            DiagnosticLog.Write("Update check failed: "+error.Message);
            message=L.Ar ? "تعذّر التحقق من التحديثات أو فتح التنزيل. تحقق من اتصال الإنترنت وحاول مجدداً." : "Couldn't check for updates or open the download. Check your internet connection and try again.";
        } finally {
            checkingUpdates=false;foreach(var button in updateButtons){button.IsEnabled=true;button.Content=UpdateLabel;}
        }
        if(exiting)return;
        dialogOpen=true;
        try { await new ContentDialog { XamlRoot=Root.XamlRoot,Title=UpdateLabel,Content=message,CloseButtonText=L.Ar ? "إغلاق" : "Close" }.ShowAsync(); }
        finally {dialogOpen=false;}
    }

    // ---- settings ----
    UIElement BuildSettings()
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(Text(L.T("settings.language"), 14, true));
        var language = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var lg in Extra.Languages) language.Items.Add(lg.name);
        language.SelectedIndex = Math.Max(0, Array.FindIndex(Extra.Languages, x => x.code == settings.Language));
        language.SelectionChanged += (_, _) => { if (language.SelectedIndex < 0) return; string chosen = Extra.Languages[language.SelectedIndex].code; if (chosen == settings.Language) return; settings.Language = chosen; settings.Save(); Render(); };
        stack.Children.Add(language);

        stack.Children.Add(new TextBlock { Height = 4 });
        stack.Children.Add(Text(L.T("settings.theme"), 14, true));
        var theme = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        theme.Items.Add(L.T("theme.system")); theme.Items.Add(L.T("theme.light")); theme.Items.Add(L.T("theme.dark"));
        theme.SelectedIndex = settings.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
        theme.SelectionChanged += (_, _) => { string chosen = theme.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" }; if (chosen == settings.Theme) return; settings.Theme = chosen; settings.Save(); Render(); };
        stack.Children.Add(theme);
        stack.Children.Add(AccentPicker());


        stack.Children.Add(new TextBlock { Height = 4 });
        stack.Children.Add(Text(L.T("settings.buttons"), 14, true));
        var buttons = new StackPanel { Spacing = 2 };
        foreach (var tile in Tiles) buttons.Children.Add(ToggleRow(L.T(tile.LabelKey), settings.IsTileVisible(tile.Id), on => { settings.SetTileVisible(tile.Id, on); settings.Save(); }));
        stack.Children.Add(Card(buttons, new Thickness(14, 6, 14, 6)));

        stack.Children.Add(new TextBlock { Height = 4 });
        stack.Children.Add(Text(L.T("settings.links"), 14, true));
        var links = new StackPanel { Spacing = 2 };
        links.Children.Add(ToggleRow(L.T("warranty.link"), settings.ShowWarranty, on => { settings.ShowWarranty = on; settings.Save(); }));
        links.Children.Add(ToggleRow(L.T("battery.link"), settings.ShowBatteryDetails, on => { settings.ShowBatteryDetails = on; settings.Save(); }));
        stack.Children.Add(Card(links, new Thickness(14, 6, 14, 6)));

        stack.Children.Add(Text(L.Ar ? "قفل Fn يبدأ مفعّلاً في الواجهة. القراءة الحالية من النظام غير موثوقة؛ هذه ليست إشارة إلى الحالة الفعلية، ولا يغيّر البرنامج قفل Fn عند بدء التشغيل." : "Fn Lock starts ON in the UI. The current system reading is unreliable; this is an assumption, not a verified hardware state. Startup does not change Fn Lock.", 12, false, SecondaryText));
        stack.Children.Add(Text(L.T("status.some"), 12, false, SecondaryText));
        stack.Children.Add(Text(current?.Problems.Length > 0 ? string.Join("\n", current.Problems) : L.T("status.ok"), 12, false, SecondaryText));
        stack.Children.Add(Text(DiagnosticLog.Path, 11, false, SecondaryText));
        var logs = new Button { Content = L.Ar ? "فتح سجل التشخيص" : "Open diagnostic log", HorizontalAlignment = HorizontalAlignment.Stretch };
        logs.Click += (_, _) => OpenUrl(DiagnosticLog.Path);
        stack.Children.Add(logs);
        var exit = new Button { Content = L.T("settings.exit"), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 6, 0, 0) };
        exit.Click += (_, _) => ExitApp();
        stack.Children.Add(exit);
        stack.Children.Add(Text(L.Ar ? "التحديثات" : "Updates",14,true));
        stack.Children.Add(ToggleRow(L.Ar ? "البحث التلقائي عن التحديثات" : "Automatically check for updates",settings.AutoCheckUpdates,on=>{settings.AutoCheckUpdates=on;settings.Save();}));
        stack.Children.Add(Text(L.Ar ? "بعد 45 ثانية من التشغيل، ثم كل 6 ساعات. التنزيل والتثبيت بقرارك." : "45 seconds after launch, then every 6 hours. Download and install only when you choose.",12,false,SecondaryText));
        stack.Children.Add(UpdateButton());
        return new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 12, 0) };
    }

    UIElement ToggleRow(string label, bool isOn, Action<bool> changed)
    {
        var grid = new Grid { MinHeight = 40 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Text(label, 14));
        var toggle = new ToggleSwitch { IsOn = isOn, OnContent = "", OffContent = "", MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Right };
        toggle.Toggled += (_, _) => changed(toggle.IsOn);
        Grid.SetColumn(toggle, 1); grid.Children.Add(toggle);
        return grid;
    }

    UIElement BuildAbout()
    {
        var stack = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
        try
        {
            var image = new Image { Width = 96, Height = 96, Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "legion-logo.png"))) };
            // The supplied logo is white; give it a dark rounded tile so it stays visible in the light theme.
            stack.Children.Add(new Border { Width = 120, Height = 120, CornerRadius = new CornerRadius(28), Background = Rgb(32, 32, 32), Child = image, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(12) });
        }
        catch { }
        var name = Text("Fluent Vantage Toolbar", 22, true); name.HorizontalAlignment = HorizontalAlignment.Center; stack.Children.Add(name);
        var version = Text(L.T("about.version") + " " + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.2.0"), 13, false, SecondaryText); version.HorizontalAlignment = HorizontalAlignment.Center; stack.Children.Add(version);
        var dev = Text(L.T("about.developer"), 14); dev.HorizontalAlignment = HorizontalAlignment.Center; stack.Children.Add(dev);
        var contrib = Text(L.T("about.contrib"), 13, false); contrib.HorizontalAlignment = HorizontalAlignment.Center; contrib.TextAlignment = TextAlignment.Center; contrib.TextWrapping = TextWrapping.Wrap; stack.Children.Add(contrib);
        var credit = Text(L.T("about.credit"), 13, false, SecondaryText); credit.HorizontalAlignment = HorizontalAlignment.Center; stack.Children.Add(credit);
        var note = Text(L.T("about.note"), 12, false, SecondaryText); note.TextAlignment = TextAlignment.Center; note.Margin = new Thickness(8, 12, 8, 0); stack.Children.Add(note);
        var repo=new HyperlinkButton { Content="GitHub Repo Link",NavigateUri=new Uri("https://github.com/MohamedElnaggar00/Fluent-Vantage-Toolbar"),HorizontalAlignment=HorizontalAlignment.Center };
        stack.Children.Add(repo);
        stack.Children.Add(UpdateButton());
        return new ScrollViewer { Content=stack, VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    }

    // ---- battery details ----
    async Task LoadBatteryAsync()
    {
        batteryDetails = null;
        try { batteryDetails = await Details.ReadBatteryAsync(); } catch { }
        if (view == "battery") body.Content = BuildBattery();
    }

    Grid DetailRow(string label, string value, Brush? valueBrush = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Text(label, 14, false, Primary));
        var v = Text(value, 14, true, valueBrush); v.HorizontalAlignment = HorizontalAlignment.Right; v.TextWrapping = TextWrapping.NoWrap;
        Grid.SetColumn(v, 1); grid.Children.Add(v);
        return grid;
    }

    UIElement ProgressBarFor(double percent, Brush fill, Brush track)
    {
        var host = new Grid { Height = 8, FlowDirection = FlowDirection.LeftToRight };
        host.Children.Add(new Border { Background = track, CornerRadius = new CornerRadius(4) });
        var bar = new Border { Background = fill, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        host.Children.Add(bar);
        host.SizeChanged += (_, _) => bar.Width = host.ActualWidth * Math.Clamp(percent, 0, 100) / 100d;
        return host;
    }

    UIElement BuildBattery()
    {
        var d = batteryDetails;
        string na = L.T("bat.unavailable");
        string Number(double? v, string unit) => v.HasValue ? v.Value.ToString("0.0", CultureInfo.InvariantCulture) + unit : na;
        var stack = new StackPanel { Spacing = 14 };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var headerIcon = DashboardIcons.Create("Battery");
        header.Children.Add(headerIcon); header.Children.Add(Text(L.T("bat.header"), 20, true));
        stack.Children.Add(header);
        double? charge = current?.Percent;
        stack.Children.Add(DetailRow(L.T("bat.charge"), charge.HasValue ? charge.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%" : na));
        stack.Children.Add(DetailRow(L.T("bat.health"), Number(d?.HealthPercent, "%")));
        var gradient = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 0) };
        gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 76, 175, 80), Offset = 0 });
        gradient.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0, 150, 136), Offset = 1 });
        stack.Children.Add(ProgressBarFor(d?.HealthPercent ?? 0, gradient, Dark ? Rgb(255, 255, 255, 0x22) : Rgb(0, 0, 0, 0x18)));
        stack.Children.Add(DetailRow(L.T("bat.design"), Number(d?.DesignWh, " Wh")));
        stack.Children.Add(DetailRow(L.T("bat.full"), Number(d?.FullWh, " Wh")));
        stack.Children.Add(DetailRow(L.T("bat.cycles"), d?.Cycles?.ToString(CultureInfo.InvariantCulture) ?? na));
        stack.Children.Add(DetailRow(L.T("bat.date"), d?.Manufactured?.ToString("M/d/yyyy", CultureInfo.InvariantCulture) ?? na));
        if (d != null && d.Problems.Length > 0 && !preview) stack.Children.Add(Text(string.Join("\n", d.Problems), 11, false, SecondaryText));
        return new ScrollViewer { Content = Card(stack, new Thickness(20, 18, 20, 20)), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalAlignment = VerticalAlignment.Top };
    }

    // ---- warranty ----
    async Task LoadWarrantyAsync(bool force)
    {
        if (preview) return;
        if (!force && warranty == null) warranty = Details.ReadCachedWarranty();
        if (!force && warranty != null) { if (view == "warranty") body.Content = BuildWarranty(); return; }
        warrantyLoading = true; warrantyError = null;
        if (view == "warranty") body.Content = BuildWarranty();
        try { var fresh = await Details.FetchWarrantyAsync(); if (fresh != null) warranty = fresh; else warrantyError = L.T("war.failed"); }
        catch { warrantyError = L.T("war.failed"); }
        warrantyLoading = false;
        if (view == "warranty") body.Content = BuildWarranty();
    }

    UIElement BuildWarranty()
    {
        var green = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 175, 80));
        var red = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 232, 86, 86));
        var stack = new StackPanel { Spacing = 14 };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        bool? active = warranty?.End == null ? null : (warranty.End.Value.Date - DateTime.Today).Days > 0;
        var statusIcon = Glyph(active == true ? "\uE73E" : "\uE711", 20);
        statusIcon.Foreground = active == true ? green : active == false ? red : SecondaryText;
        if (active == false) statusIcon.Glyph = "\uEA39";
        left.Children.Add(statusIcon); left.Children.Add(Text(L.T("war.title"), 20, true));
        header.Children.Add(left);
        var refresh = IconButton("\uE72C", L.T("war.refresh"), () => _ = LoadWarrantyAsync(true));
        refresh.Width = 32; refresh.Height = 32; refresh.IsEnabled = !warrantyLoading;
        Grid.SetColumn(refresh, 1); header.Children.Add(refresh);
        stack.Children.Add(header);

        if (warrantyLoading) { stack.Children.Add(new ProgressRing { IsActive = true, Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center }); stack.Children.Add(Text(L.T("war.loading"), 12, false, SecondaryText)); }
        else if (warranty == null) stack.Children.Add(Text(warrantyError ?? L.T("war.failed"), 13, false, SecondaryText));
        else
        {
            int days = warranty.End.HasValue ? (warranty.End.Value.Date - DateTime.Today).Days : 0;
            stack.Children.Add(Text(days > 0 ? L.T("war.active") : L.T("war.expired"), 15, true, days > 0 ? green : red));
            double remaining = 0;
            if (days > 0 && warranty.Start.HasValue && warranty.End.HasValue)
            {
                double total = (warranty.End.Value - warranty.Start.Value).TotalDays, elapsed = (DateTime.Today - warranty.Start.Value).TotalDays;
                remaining = total > 0 ? Math.Clamp((1 - elapsed / total) * 100, 0, 100) : 100;
            }
            stack.Children.Add(ProgressBarFor(remaining, green, Dark ? Rgb(255, 255, 255, 0x22) : Rgb(0, 0, 0, 0x18)));
            string Date(DateTime? v) => v?.ToString("M/d/yyyy", CultureInfo.InvariantCulture) ?? "-";
            stack.Children.Add(DetailRow(L.T("war.start"), Date(warranty.Start)));
            stack.Children.Add(DetailRow(L.T("war.end"), Date(warranty.End)));
            stack.Children.Add(DetailRow(L.T("war.days"), days > 0 ? days.ToString(CultureInfo.InvariantCulture) : L.T("war.expired"), green));
            var support = new HyperlinkButton { Padding = new Thickness(0, 4, 0, 4) };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(Glyph("\uE8A7", 14)); content.Children.Add(new TextBlock { Text = L.T("war.support"), FontSize = 14 });
            support.Content = content;
            string url = warranty.Link ?? "https://pcsupport.lenovo.com/us/en/products/laptops-and-netbooks/legion-series/legion-5-15ith6h/82jh";
            support.Click += (_, _) => OpenUrl(url);
            stack.Children.Add(support);
        }
        return new ScrollViewer { Content = Card(stack, new Thickness(20, 18, 20, 18)), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalAlignment = VerticalAlignment.Top };
    }

    // ---- capture (CI previews with fixture data) ----
    async Task SaveImage(string path)
    {
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await bitmap.RenderAsync(Root);
        var buffer = await bitmap.GetPixelsAsync();
        using var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer);
        var pixels = new byte[buffer.Length]; reader.ReadBytes(pixels);
        using var stream = File.Open(path, FileMode.Create).AsRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
    }

    async Task Capture()
    {
        // Offline decision tests never download or launch an installer during captures.
        string fixture="""
        {"draft":false,"prerelease":false,"tag_name":"v9.0.0","assets":[{"name":"FluentVantageToolbar-9.0.0-Setup.exe","browser_download_url":"https://github.com/MohamedElnaggar00/Fluent-Vantage-Toolbar/releases/download/v9.0.0/FluentVantageToolbar-9.0.0-Setup.exe"},{"name":"FluentVantageToolbar-9.0.0-Setup-with-dotnet.exe","browser_download_url":"https://github.com/MohamedElnaggar00/Fluent-Vantage-Toolbar/releases/download/v9.0.0/FluentVantageToolbar-9.0.0-Setup-with-dotnet.exe"}]}
        """;
        using(var test=System.Text.Json.JsonDocument.Parse(fixture)) {
            if(UpdateDownload(test.RootElement,new Version(1,0,0))==null || UpdateDownload(test.RootElement,new Version(9,0,0))!=null || UpdateDownload(test.RootElement,new Version(10,0,0))!=null)throw new InvalidOperationException("Update version comparison failed");
            if(!UpdateDownload(test.RootElement,new Version(1,0,0),false)!.EndsWith("-Setup.exe") || !UpdateDownload(test.RootElement,new Version(1,0,0),true)!.EndsWith("-Setup-with-dotnet.exe"))throw new InvalidOperationException("Installed update flavor mismatch");
            if(!new AppSettings().AutoCheckUpdates)throw new InvalidOperationException("Automatic updates must default on");
        }
        foreach(var replacement in new[]{fixture.Replace("\"draft\":false","\"draft\":true"),fixture.Replace("\"prerelease\":false","\"prerelease\":true")}) {
            using var test=System.Text.Json.JsonDocument.Parse(replacement);
            if(UpdateDownload(test.RootElement,new Version(1,0,0))!=null)throw new InvalidOperationException("Non-stable update accepted");
        }
        // Deterministic render fixture, clearly labelled as preview. Hardware services and the network are never called.
        int index = Array.IndexOf(args, "--capture"); string path = args[index + 1];
        string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", name = Path.GetFileName(path);
        current = new DeviceState(60, true, false, ChargeMode.Conservation, false, false, "Visual fixture", [], true, 2, 165, [60, 165]);
        batteryDetails = new BatteryDetails(99.9, 60.0, 59.9, 36, new DateTime(2022, 1, 22), []);
        warranty = new WarrantyResult(new DateTime(2022, 7, 29), new DateTime(2023, 7, 28), null);
        foreach (var target in new[] { "main", "settings", "battery", "warranty", "about" })
        {
            view = target; Render(); await geometryReady;
            foreach (var button in tileButtons.Values) button.IsEnabled = true;
            await Task.Delay(target == "main" ? 2500 : 1200);
            await SaveImage(target == "main" ? path : Path.Combine(dir, target + "-" + name));
        }
        // Verify dynamic height with a single tile row and then with no quick tiles.
        foreach (var count in new[] { 5, 0 }) {
            settings.HiddenTiles = Tiles.Skip(count).Select(t => t.Id).ToList();
            view = "main"; Render(); await geometryReady;
            foreach (var button in tileButtons.Values) button.IsEnabled = true;
            await Task.Delay(1200);
            await SaveImage(Path.Combine(dir, "tiles" + count + "-" + name));
        }
        // Every tile-row/link combination must fit without a main-page scroll host.
        foreach(var count in new[]{7,5,0}) foreach(var warrantyVisible in new[]{true,false}) foreach(var detailsVisible in new[]{true,false}) {
            settings.HiddenTiles=Tiles.Skip(count).Select(t=>t.Id).ToList();settings.ShowWarranty=warrantyVisible;settings.ShowBatteryDetails=detailsVisible;
            view="main";Render();await geometryReady;await Task.Delay(400);
            Root.UpdateLayout();
            if(body.Content is not StackPanel main)throw new InvalidOperationException("Main page must not scroll");
            foreach(FrameworkElement child in main.Children) {
                var bottom=child.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0,child.ActualHeight)).Y;
                if(bottom>Root.ActualHeight+1 || child.ActualHeight+1<child.DesiredSize.Height)
                    throw new InvalidOperationException($"Main child clipped: tiles={count}, warranty={warrantyVisible}, details={detailsVisible}, bottom={bottom}, root={Root.ActualHeight}");
            }
            var lastChild=(FrameworkElement)main.Children.Last();
            var paintedHeight=await PaintedHeightAsync(lastChild);
            var paintedBottom=lastChild.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0,paintedHeight)).Y;
            if(Math.Abs(Root.ActualHeight-paintedBottom-24)>1)throw new InvalidOperationException($"Main painted gap mismatch: {Root.ActualHeight-paintedBottom}");
            var pt=lastChild.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0,0));
            File.AppendAllText(Path.Combine(dir,"layout-"+name+".txt"),$"tiles={count} warranty={warrantyVisible} details={detailsVisible} root={Root.ActualWidth}x{Root.ActualHeight} client={AppWindow.ClientSize.Width}x{AppWindow.ClientSize.Height} canvas={canvas.ActualHeight}/{canvas.DesiredSize.Height} title={titleBar.ActualHeight} body={body.ActualHeight} main={main.ActualHeight}/{main.DesiredSize.Height} inkH={paintedHeight} lastY={pt.Y} lastH={lastChild.ActualHeight}/{lastChild.DesiredSize.Height}\n");
            await SaveImage(Path.Combine(dir,$"main-{count}-warranty{warrantyVisible}-details{detailsVisible}-"+name));
        }
        // Model names are machine-specific and long names must fit the caption too.
        foreach(var model in new[]{"LOQ 15IRX9","Legion Pro 7 16IRX10H Long Device Model Name"}) {
            DeviceInfo.Override=model;settings.HiddenTiles=Tiles.Skip(5).Select(t=>t.Id).ToList();settings.ShowWarranty=false;view="main";Render();await geometryReady;await Task.Delay(400);
            if(titleBar.ActualHeight+1<titleBar.DesiredSize.Height)throw new InvalidOperationException("Dynamic model header clipped");
            await SaveImage(Path.Combine(dir,"model-"+(model.StartsWith("LOQ")?"loq":"long")+"-"+name));
        }
        DeviceInfo.Override="Legion 5 15ITH6H";
        settings.ShowWarranty=true;settings.ShowBatteryDetails=true;
        settings.HiddenTiles.Clear();
        // Low and critical battery colours.
        foreach (var level in new[] { 20, 5 })
        {
            view = "main"; current = current with { Percent = level, Plugged = false, Charging = false }; Render(); await geometryReady;
            foreach (var button in tileButtons.Values) button.IsEnabled = true;
            await Task.Delay(1200);
            await SaveImage(Path.Combine(dir, "level" + level + "-" + name));
        }
        var devicePreview=new DeviceDashboardWindow(settings.Theme,current,true);
        dashboard=devicePreview;view="device";Render();await geometryReady;
        await Task.Delay(1500);
        devicePreview.AssertPreviewReady();
        await SaveImage(Path.Combine(dir,"device-"+name));
        await devicePreview.PreviewPositionAsync(false,true);
        await SaveImage(Path.Combine(dir,"device-revealed-"+name));
        await devicePreview.PreviewPositionAsync(true);
        await SaveImage(Path.Combine(dir,"device-bottom-"+name));

        view="main";settings.HiddenTiles=Tiles.Skip(5).Select(t=>t.Id).ToList();
        foreach(bool off in new[]{false,true}) {
            current=current! with { Muted=off,TouchpadLocked=off };Render();await geometryReady;
            foreach(var button in tileButtons.Values)button.IsEnabled=true;
            await SaveImage(Path.Combine(dir,$"icons-{(off ? "off" : "on")}-{name}"));
        }
        foreach(string accent in new[]{"#0078D4","#744DA9"}) {
            settings.AccentColor=accent;view="settings";Render();await geometryReady;await Task.Delay(250);
            await SaveImage(Path.Combine(dir,$"accent-{accent[1..]}-{name}"));
        }
        if(body.Content is ScrollViewer settingsScroll) {
            settingsScroll.ChangeView(null,settingsScroll.ScrollableHeight,null,true);await Task.Delay(250);
            await SaveImage(Path.Combine(dir,"settings-bottom-"+name));
        }
        settings.AccentColor=null;ApplyAccent();
        if(ParseAccent("#12ABEF") is not {} parsed || parsed.R!=0x12 || ParseAccent("invalid")!=null)throw new InvalidOperationException("Accent parsing failed");
        using(var notice=new UpdateNoticeCaptureScope(new UpdateNoticeWindow("v9.0.0","https://github.com/MohamedElnaggar00/Fluent-Vantage-Toolbar/releases/download/v9.0.0/FluentVantageToolbar-9.0.0-Setup.exe",L.Ar,Dark,true))) {
            notice.Window.ShowNotice();await Task.Delay(400);
            await notice.Window.CaptureAsync(Path.Combine(dir,"update-notice-"+name));
        }
        if(args.Contains("--interactive")) {
            settings.HiddenTiles=Tiles.Skip(5).Select(t=>t.Id).ToList();settings.ShowWarranty=true;
            view="main";Render();await geometryReady;
            var phases=Path.Combine(dir,"interactive-phases.txt");
            AppWindow.Hide();
            for(int cycle=0;cycle<3;cycle++) {
                File.AppendAllText(phases,$"{DateTime.UtcNow:O} open {cycle}\n");
                ShowFlyout("main",false);await geometryReady;await Task.Delay(500);
                int before=nativeResizeCount;
                for(int refresh=0;refresh<5;refresh++){Apply();await NextFrameAsync();}
                if(nativeResizeCount!=before)throw new InvalidOperationException("Unchanged refresh resized the window");
                File.AppendAllText(phases,$"{DateTime.UtcNow:O} settings {cycle}\n");
                await NavigateAsync("settings");await Task.Delay(350);
                File.AppendAllText(phases,$"{DateTime.UtcNow:O} main {cycle}\n");
                await NavigateAsync("main");await Task.Delay(350);
                foreach(string page in new[]{"battery","device","warranty","about"}) {
                    File.AppendAllText(phases,$"{DateTime.UtcNow:O} {page} {cycle}\n");
                    await NavigateAsync(page);await Task.Delay(350);
                    File.AppendAllText(phases,$"{DateTime.UtcNow:O} main-from-{page} {cycle}\n");
                    await NavigateAsync("main");await Task.Delay(350);
                }
                File.AppendAllText(phases,$"{DateTime.UtcNow:O} hide {cycle}\n");
                HideFlyout();await Task.Delay(250);
            }
            File.AppendAllText(phases,$"PASS serialized geometry, three open/settings/main/hide cycles; unchanged refresh native resize count unchanged. Actual DPI={GetDpiForWindow(hwnd)}\n");
        }
        Close();
    }
}


internal sealed class UpdateNoticeCaptureScope : IDisposable
{
    public UpdateNoticeWindow Window {get;}
    public UpdateNoticeCaptureScope(UpdateNoticeWindow window){Window=window;}
    public void Dispose()=>Window.Close();
}
internal sealed class UpdateNoticeWindow : Window
{
    readonly Grid root=new();
    public UpdateNoticeWindow(string version,string url,bool arabic,bool dark,bool fixture)
    {
        Title="Fluent Vantage Toolbar";ExtendsContentIntoTitleBar=true;
        root.Background=new SolidColorBrush(dark?Windows.UI.Color.FromArgb(255,32,32,32):Windows.UI.Color.FromArgb(255,243,243,243));
        root.RequestedTheme=dark?ElementTheme.Dark:ElementTheme.Light;root.FlowDirection=arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        root.Padding=new Thickness(20);Content=root;
        var stack=new StackPanel{Spacing=12};root.Children.Add(stack);
        stack.Children.Add(new TextBlock{Text=arabic?"يتوفر تحديث جديد":"Update available",FontSize=18,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});
        stack.Children.Add(new TextBlock{Text=$"Fluent Vantage Toolbar {version}",TextWrapping=TextWrapping.Wrap,FontSize=14});
        stack.Children.Add(new TextBlock{Text=arabic?"التنزيل المباشر للنسخة المطابقة لتثبيتك. لن يتم التثبيت تلقائياً.":"Direct download for your installed build. Nothing installs automatically.",TextWrapping=TextWrapping.Wrap,FontSize=12});
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};
        var download=new Button{Content=arabic?"تحميل":"Download"};download.Click+=(_,_)=>{if(!fixture){try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});Close();}catch(Exception error){DiagnosticLog.Write("Update link failed: "+error.Message);}}};
        var later=new Button{Content=arabic?"لاحقاً":"Later"};later.Click+=(_,_)=>Close();buttons.Children.Add(download);buttons.Children.Add(later);stack.Children.Add(buttons);
        if(AppWindow.Presenter is OverlappedPresenter presenter){presenter.SetBorderAndTitleBar(true,false);presenter.IsResizable=false;presenter.IsMaximizable=false;presenter.IsMinimizable=false;presenter.IsAlwaysOnTop=true;}
        double scale=GetScale();AppWindow.ResizeClient(new SizeInt32((int)(370*scale),(int)(230*scale)));AppWindow.IsShownInSwitchers=false;
    }
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    double GetScale()=>Math.Max(1,GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this))/96d);
    public void ShowNotice(){var work=DisplayArea.Primary.WorkArea;AppWindow.Move(new PointInt32(work.X+work.Width-AppWindow.Size.Width-16,work.Y+work.Height-AppWindow.Size.Height-16));AppWindow.Show();}
    public async Task CaptureAsync(string path) {
        var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(root);
        var buffer=await bitmap.GetPixelsAsync();using var reader=Windows.Storage.Streams.DataReader.FromBuffer(buffer);var pixels=new byte[buffer.Length];reader.ReadBytes(pixels);
        using var stream=File.Open(path,FileMode.Create).AsRandomAccessStream();var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels);await encoder.FlushAsync();
    }
}
