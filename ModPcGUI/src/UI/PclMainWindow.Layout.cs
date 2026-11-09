using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf = System.Windows;
using WpfC = System.Windows.Controls;

namespace ModPcGUI
{
    /// <summary>
    /// ES Tool 主界面 —— PCL2 主界面布局：
    /// 蓝色标题栏（PCL 原版图标按钮 + PCL English 字体 Logo）
    /// + 左侧白色启动面板（MyComboBox / MyButton / MyIconButton 全原生）
    /// + 右侧浅蓝内容区（MyCard / MyHint / MyListItem / MyTextBox 全原生）。
    /// 控件的悬停动画、按下质感、下拉弹层全部由 PCL2 程序集内置，1:1 同款。
    /// </summary>
    public sealed partial class PclMainWindow
    {
        private sealed class NavEntry
        {
            public PCL.MyRadioButton Rb;
            public int Index;
        }

        private readonly List<NavEntry> _navs = new List<NavEntry>();
        private readonly Dictionary<int, Wpf.FrameworkElement> _pages = new Dictionary<int, Wpf.FrameworkElement>();
        private WpfC.Grid _contentHost;
        private WpfC.TextBlock _launchSub;
        private WpfC.TextBlock _pageHint;
        private WpfC.TextBlock _statePill;
        private WpfC.Grid _btnLoginRow;
        private WpfC.StackPanel _modeRow;
        private Wpf.FrameworkElement _addrRow;
        private int _navIndex;
        private bool _navGuard;

        private static readonly string[] NavNames = { "启动", "下载", "联机大厅", "设置", "更多" };

        // PCL FormMain.xaml 标题栏 RadioButton 的原版图标路径与 LogoScale，1:1
        private static readonly string[] NavLogos =
        {
            "M52.1,164.5c-1.4,0-3.1-0.5-4.2-1.3c-2.6-1.7-4-4.2-4-7V43.8c0-2.9,1.6-5.8,4.1-7c1.2-0.8,2.7-1.2,4.1-1.2c1.5,0,2.9,0.4,4.2,1.2L153.1,93c0,0,0.1,0,0.1,0.1c2.6,1.7,4,4.2,4,7c0,3-1.7,5.8-4.2,7.1l-96.8,56.2C55.1,164,53.5,164.5,52.1,164.5z M60.4,142.1l72.1-42.1L60.4,58.2V142.1z",
            // 下载：PCL FormMain BtnTitleSelect1 原版图标与 LogoScale
            "M955 610h-59c-15 0-29 13-29 29v196c0 15-13 29-29 29h-649c-15 0-29-13-29-29v-196c0-15-13-29-29-29h-59c-15 0-29 13-29 29V905c0 43 35 78 78 78h787c43 0 78-35 78-78V640c0-15-13-29-29-29zM492 740c11 11 29 11 41 0l265-265c11-11 11-29 0-41l-41-41c-11-11-29-11-41 0l-110 110c-11 11-33 3-33-13V68C571 53 555 39 541 39h-59c-15 0-29 13-29 29v417c0 17-21 25-33 13l-110-110c-11-11-29-11-41 0L226 433c-11 11-11 29 0 41L492 740z",
            // 联机大厅：三层同心（大厅/房间/玩家）
            "M10,0 L20,10 L10,20 L0,10 Z M10,4.6 L15.4,10 L10,15.4 L4.6,10 Z",
            "M940.4 463.7L773.3 174.2c-17.3-30-49.2-48.4-83.8-48.4H340.2c-34.6 0-66.5 18.5-83.8 48.4L89.2 463.7c-17.3 30-17.3 66.9 0 96.8L256.4 850c17.3 30 49.2 48.4 83.8 48.4h349.2c34.6 0 66.5-18.5 83.8-48.4l167.2-289.5c17.3-29.9 17.3-66.8 0-96.8z m-94.6 96.8L725.9 768.1c-17.3 30-49.2 48.4-83.8 48.4H387.5c-34.6 0-66.5-18.5-83.8-48.4L183.9 560.5c-17.3-30-17.3-66.9 0-96.8l119.8-207.5c17.3-30 49.2-48.4 83.8-48.4h254.6c34.6 0 66.5 18.5 83.8 48.4l119.8 207.5c17.3 30 17.3 66.9 0.1 96.8z M522.3 321.2c-2.5-0.1-5-0.2-7.5-0.2-119.9 0-214 110.3-186.3 235 15.8 70.9 71.5 126.6 142.4 142.4 17.5 3.9 34.7 5.4 51.4 4.7 102.1-3.9 183.6-87.9 183.6-191 0.1-103-81.5-187-183.6-190.9z m68.6 269.1c-18.5 18-43 28.9-68.6 30.7l-6 0.3c-30.2 0.4-58.6-11.4-79.7-33-19.5-20.1-30.7-47-30.9-75-0.3-29.6 11.1-57.4 32-78.3 20.6-20.6 48-32 77.2-32 2.5 0 5 0.1 7.5 0.3 26.7 1.8 51.5 13.2 70.5 32.5 19.6 20 30.8 46.9 31.2 74.9 0.2 30.2-11.5 58.6-33.2 79.6z",
            "M364 0h-273C40 0 0 40 0 91v273C0 414 40 455 91 455h273C414 455 455 414 455 364V91C455 40 414 0 364 0zM341 341H113V113h227v227zM933 0h-273C609 0 568 40 568 91v273c0 50 40 91 91 91h273C983 455 1024 414 1024 364V91c0-50-40-91-90-91zM910 341h-227V113h227v227zM364 568h-273C40 568 0 609 0 659v273c0 50 40 91 91 91h273C414 1024 455 983 455 932v-273C455 609 414 568 364 568zM341 910H113v-227h227v227zM933 568h-273c-50 0-91 40-91 91v273c0 50 40 91 91 91h273c50 0 90-40 90-91v-273c0-50-40-90-90-90zM910 910h-227v-227h227v227z"
        };
        private static readonly double[] NavLogoScales = { 0.9, 0.9, 1.0, 1.1, 0.93 };

        public PclMainWindow()
        {
            Title = "ES Tool";
            Width = 850;                 // PCL FormMain 原版 850×500
            Height = 500;
            MinWidth = 810;              // PCL 原版 MinHeight=470 MinWidth=810
            MinHeight = 470;
            WindowStyle = Wpf.WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = Wpf.ResizeMode.CanMinimize;
            Background = Brushes.Transparent;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            // PCL2 的正文就是微软雅黑，这里全局锁定
            FontFamily = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, 微软雅黑");

            var app = Wpf.Application.Current;
            if (app != null && app.Resources["PclStylesLoaded"] == null)
            {
                // 走和 Boot 同一条加载路径：读 pcl\PclStyles.xaml 再 XamlReader.Parse。
                // 不能再用 `Source = new Uri("PclStyles.xaml", UriKind.Relative)`——
                // 那份资源已经改成运行时文件（csproj 里从 Page 移除），程序集里没有它，
                // 这条 URI 会直接抛 IOException，把 PCL UI 顶回经典界面。
                LoadPclStyles(app);
            }

            var blue = Brush("#1370f3");       // PCL ColorBrush3
            var text = Brush("#343d4a");       // PCL ColorBrush1
            var sub = Brush("#8c8c8c");        // PCL ColorBrushGray3
            var rightBg = Brush("#d5e6fd");    // PCL ColorBrush6

            // 窗口外框（圆角）
            var frame = new WpfC.Border
            {
                CornerRadius = new Wpf.CornerRadius(8),
                Background = Brushes.White,
                BorderBrush = Brush("#8899aa"),
                BorderThickness = new Wpf.Thickness(1),
                ClipToBounds = true
            };
            var main = new WpfC.Grid();
            main.RowDefinitions.Add(new WpfC.RowDefinition { Height = new Wpf.GridLength(48) });
            main.RowDefinitions.Add(new WpfC.RowDefinition { Height = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
            frame.Child = main;

            // ---------- 顶栏：PCL FormMain 同款（左 Logo / 中 MyRadioButton 导航 / 右 min·close） ----------
            var head = new WpfC.Grid { Background = blue };
            head.ColumnDefinitions.Add(new WpfC.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
            head.ColumnDefinitions.Add(new WpfC.ColumnDefinition { Width = Wpf.GridLength.Auto });
            head.ColumnDefinitions.Add(new WpfC.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });

            // 左：Logo（PCL LabTitleLogo 同款：PCL English 字体、白色 17px）
            var leftHead = new WpfC.TextBlock
            {
                Text = "ES Tool",
                FontFamily = LogoFont(),       // PCL2 内嵌的 PCL English 字体
                FontSize = 17,
                Foreground = Brushes.White,
                VerticalAlignment = Wpf.VerticalAlignment.Center,
                Margin = new Wpf.Thickness(18, 0, 0, 0)
            };
            WpfC.Grid.SetColumn(leftHead, 0);
            head.Children.Add(leftHead);

            // 中：PCL PanTitleSelect 同款 MyRadioButton 导航（单选互斥靠同一父容器）
            var navPanel = new WpfC.StackPanel
            {
                Orientation = WpfC.Orientation.Horizontal,
                Height = 27,
                VerticalAlignment = Wpf.VerticalAlignment.Center,
                Margin = new Wpf.Thickness(13, 0, 0, 0)
            };
            WpfC.Grid.SetColumn(navPanel, 1);
            head.Children.Add(navPanel);
            for (int i = 0; i < NavNames.Length; i++)
            {
                int idx = i;
                var rb = new PCL.MyRadioButton
                {
                    Text = NavNames[idx],
                    Logo = NavLogos[idx],
                    LogoScale = NavLogoScales[idx],
                    Margin = new Wpf.Thickness(5, 0, 5, 0),
                    Padding = new Wpf.Thickness(2, 0, 2, 0)
                };
                if (idx == 0) rb.Checked = true;   // PCL 原版 BtnTitleSelect0 Checked="True"
                rb.Check += (s, raiseByMouse) => NavTo(idx);
                _navs.Add(new NavEntry { Rb = rb, Index = idx });
                navPanel.Children.Add(rb);
            }

            // 右：最小化 / 关闭（PCL FormMain 原版尺寸、边距、图标路径）
            var rightHead = new WpfC.StackPanel
            {
                Orientation = WpfC.Orientation.Horizontal,
                VerticalAlignment = Wpf.VerticalAlignment.Center,
                HorizontalAlignment = Wpf.HorizontalAlignment.Right,
                Margin = new Wpf.Thickness(0, 0, 12, 0)
            };
            WpfC.Grid.SetColumn(rightHead, 2);
            head.Children.Add(rightHead);

            var min = new PCL.MyIconButton
            {
                Logo = "F1 M0,0 h15 v2 h-15 v-2 Z",
                LogoScale = 0.72,
                Theme = PCL.MyIconButton.Themes.White,
                Width = 28,
                Height = 28,
                Margin = new Wpf.Thickness(0, 0, 16, 0),
                ToolTip = "最小化"
            };
            min.Click += (s, e) => { WindowState = Wpf.WindowState.Minimized; };
            PaintIcon(min);
            rightHead.Children.Add(min);

            var close = new PCL.MyIconButton
            {
                Logo = "F1 M2,0 L0,2 8,10 0,18 2,20 10,12 18,20 20,18 12,10 20,2 18,0 10,8 2,0Z",
                LogoScale = 0.72,
                Theme = PCL.MyIconButton.Themes.White,
                Width = 28,
                Height = 28,
                ToolTip = "关闭"
            };
            close.Click += (s, e) => { Close(); };
            PaintIcon(close);
            rightHead.Children.Add(close);

            head.MouseLeftButtonDown += (s, e) =>
            {
                if (InInteractive(e.OriginalSource as Wpf.DependencyObject)) return;
                if (e.ButtonState == Wpf.Input.MouseButtonState.Pressed)
                {
                    try { DragMove(); } catch { }
                }
            };
            WpfC.Grid.SetRow(head, 0);
            main.Children.Add(head);

            // ---------- 主体：左白 / 右浅蓝 ----------
            var body = new WpfC.Grid();
            body.ColumnDefinitions.Add(new WpfC.ColumnDefinition { Width = new Wpf.GridLength(312) });
            body.ColumnDefinitions.Add(new WpfC.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
            WpfC.Grid.SetRow(body, 1);

            var leftPanel = BuildLeftPanel(blue, text, sub);
            WpfC.Grid.SetColumn(leftPanel, 0);
            body.Children.Add(leftPanel);

            var rightPanel = new WpfC.Grid { Background = rightBg };
            WpfC.Grid.SetColumn(rightPanel, 1);
            _contentHost = rightPanel;
            BuildPages(blue, text, sub);
            body.Children.Add(rightPanel);

            main.Children.Add(body);
            Content = frame;

            NavTo(0);

            Closed += (s, e) =>
            {
                try { ConsoleAppLogin.NetEase.Http.CloseAuthserver(); } catch { }
                Environment.Exit(0);
            };
            Term.OnOutput += (t, nl, c) =>
            {
                string chunk = (t ?? "") + (nl ? "\r\n" : "");
                try
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _logBox.AppendText(chunk);
                        _logBox.ScrollToEnd();
                        LogToFile(chunk);   // 与界面日志同源，登录/下载出问题能事后查
                    }));
                }
                catch { }
            };
            AppHost.StateChanged += () =>
            {
                try { Dispatcher.BeginInvoke(new Action(RefreshState)); } catch { }
            };
            Loaded += (s, e) =>
            {
                // PCL 式窗口入场：左右面板错峰上浮淡入（PCL FormMain 在 Loaded 里起 AniStart 同款）
                try
                {
                    PclAnim.SlideFadeIn(leftPanel, 10, PclAnim.Slow, 20);
                    PclAnim.SlideFadeIn(rightPanel, 10, PclAnim.Slow, 70);
                }
                catch { }

                try { AppHost.Init(); } catch (Exception ex) { AppendLog("[init] " + ex.Message + "\r\n"); }
                RefreshAccounts();
                LoadUi();
                RefreshState();
                UpdateLaunchCaption();
                AppendLog("ES Tool 已就绪（PCL2 原生 UI）\r\n");
            };
        }

        // ---------- 左侧启动面板 ----------
        private WpfC.Panel BuildLeftPanel(SolidColorBrush blue, SolidColorBrush text, SolidColorBrush sub)
        {
            var left = new WpfC.Grid { Background = Brushes.White };
            var dock = new WpfC.DockPanel { Margin = new Wpf.Thickness(24, 18, 24, 18) };

            // ===== 顶部：头像 + 账号（PCL 启动页同款结构） =====
            var accCard = new WpfC.StackPanel();
            WpfC.DockPanel.SetDock(accCard, WpfC.Dock.Top);

            // 顶部状态胶囊（PCL PageLaunchLeft PanTypeOne 同款：圆角 13、半透明底、ColorBrush3 字色）
            var pillWrap = new WpfC.Grid { Margin = new Wpf.Thickness(0, 0, 0, 12) };
            var pill = new WpfC.Border
            {
                HorizontalAlignment = Wpf.HorizontalAlignment.Center,
                VerticalAlignment = Wpf.VerticalAlignment.Center,
                CornerRadius = new Wpf.CornerRadius(13),
                MinHeight = 27,
                MaxHeight = 27
            };
            pill.SetResourceReference(WpfC.Border.BackgroundProperty, "ColorBrushSemiTransparent");
            _statePill = new WpfC.TextBlock
            {
                Text = "已就绪 · 网易渠道",
                FontSize = 14,
                VerticalAlignment = Wpf.VerticalAlignment.Center,
                Margin = new Wpf.Thickness(12, 0, 12, 0)
            };
            _statePill.SetResourceReference(WpfC.TextBlock.ForegroundProperty, "ColorBrush3");
            pill.Child = _statePill;
            pillWrap.Children.Add(pill);
            accCard.Children.Add(pillWrap);

            // PCL 原版：头像居中，账号名只出现在下拉框里
            var avatar = new WpfC.Image
            {
                Width = 48,
                Height = 48,
                Stretch = Wpf.Media.Stretch.UniformToFill,
                HorizontalAlignment = Wpf.HorizontalAlignment.Center,
                Margin = new Wpf.Thickness(0, 4, 0, 12)
            };
            avatar.Source = LoadHead();
            avatar.Clip = new Wpf.Media.RectangleGeometry(new Wpf.Rect(0, 0, 48, 48), 8, 8);
            accCard.Children.Add(avatar);

            // 账号下拉（PCL 原生 MyComboBox）+ 删除
            var comboRow = new WpfC.Grid { Margin = new Wpf.Thickness(0, 0, 0, 8) };
            comboRow.ColumnDefinitions.Add(new WpfC.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
            comboRow.ColumnDefinitions.Add(new WpfC.ColumnDefinition { Width = Wpf.GridLength.Auto });
            _accBox.Height = 32;
            _accBox.FontSize = 12;
            _accBox.HintText = "选择账号";
            _accBox.SelectionChanged += (s, e) => UpdateLaunchCaption();
            WpfC.Grid.SetColumn(_accBox, 0);
            comboRow.Children.Add(_accBox);
            var delBtn = new PCL.MyIconButton
            {
                Logo = Icon("IconButtonDelete"),
                LogoScale = 0.62,
                Theme = PCL.MyIconButton.Themes.Color,
                Width = 30,
                Height = 30,
                ToolTip = "删除当前账号",
                VerticalAlignment = Wpf.VerticalAlignment.Center,
                Margin = new Wpf.Thickness(6, 0, 0, 0)
            };
            delBtn.Click += (s, e) => DoDeleteAccount();
            PaintIcon(delBtn);
            WpfC.Grid.SetColumn(delBtn, 1);
            comboRow.Children.Add(delBtn);
            accCard.Children.Add(comboRow);

            // 登录键（PCL MyButton：Highlight 深蓝质感 + 自带悬停/按下动画）
            // 登录成功后整行隐藏（RefreshState 里处理），把空间让给账号区
            var btnRow = new WpfC.Grid { Margin = new Wpf.Thickness(0, 0, 0, 4) };
            _btnLoginRow = btnRow;
            btnRow.ColumnDefinitions.Add(new WpfC.ColumnDefinition());
            btnRow.ColumnDefinitions.Add(new WpfC.ColumnDefinition());
            var bLogin = new PCL.MyButton
            {
                Text = "登录",
                Height = 34,
                ColorType = PCL.MyButton.ColorState.Highlight,
                Margin = new Wpf.Thickness(0, 0, 5, 0)
            };
            WpfC.TextBlock.SetFontSize(bLogin, 13);
            // Click + PreviewMouseLeftButtonUp 双通道：PCL 的 MyButton 模板在少数情况下
            // 会把 Click 吃掉（SetNameDialog.cs 里踩过同一个坑），这里用 400ms 防抖保证
            // 一次点击只跑一次 DoLogin，但绝不会「点了没反应」。
            DateTime lastLogin = DateTime.MinValue;
            Action fireLogin = () =>
            {
                if ((DateTime.UtcNow - lastLogin).TotalMilliseconds < 400) return;
                lastLogin = DateTime.UtcNow;
                DoLogin();
            };
            bLogin.Click += (s, e) => fireLogin();
            bLogin.PreviewMouseLeftButtonUp += (s, e) => fireLogin();
            var bAdd = new PCL.MyButton
            {
                Text = "添加账号",
                Height = 34,
                Margin = new Wpf.Thickness(5, 0, 0, 0)
            };
            WpfC.TextBlock.SetFontSize(bAdd, 13);
            bAdd.Click += (s, e) => DoAddAccount();
            WpfC.Grid.SetColumn(bLogin, 0);
            WpfC.Grid.SetColumn(bAdd, 1);
            btnRow.Children.Add(bLogin);
            btnRow.Children.Add(bAdd);
            accCard.Children.Add(btnRow);
            dock.Children.Add(accCard);

            // ===== 底部：版本选择 + 启动 + 快捷键 =====
            var bottom = new WpfC.StackPanel();
            WpfC.DockPanel.SetDock(bottom, WpfC.Dock.Bottom);

            // 「启动模式」行（登录成功后随登录按钮一起隐藏，RefreshState 里处理）
            var modeRow = new WpfC.StackPanel();
            _modeRow = modeRow;
            modeRow.Children.Add(new WpfC.TextBlock
            {
                Text = "启动模式",
                FontSize = 11,
                Foreground = sub,
                Margin = new Wpf.Thickness(2, 0, 0, 5)
            });
            _modeBox.Height = 30;
            _modeBox.FontSize = 12;
            _modeBox.Margin = new Wpf.Thickness(0, 0, 0, 12);
            _modeBox.Items.Add(new PCL.MyComboBoxItem { Content = "自动识别" });
            _modeBox.Items.Add(new PCL.MyComboBoxItem { Content = "电脑版" });
            _modeBox.Items.Add(new PCL.MyComboBoxItem { Content = "手机版" });
            _modeBox.SelectedIndex = 0;
            modeRow.Children.Add(_modeBox);
            bottom.Children.Add(modeRow);

            // 大启动键（PCL PageLaunchLeft BtnLaunch 同款：54 高、Highlight、Padding 30/15，
            // 版本小字 LabVersion 叠在按钮底部中央）
            var launchWrap = new WpfC.Grid { Margin = new Wpf.Thickness(0, 0, 0, 10) };
            var launch = new PCL.MyButton
            {
                Text = "启动游戏",
                Height = 54,
                ColorType = PCL.MyButton.ColorState.Highlight,
                Padding = new Wpf.Thickness(30, 0, 30, 15)
            };
            WpfC.TextBlock.SetFontSize(launch, 16);
            launch.Click += (s, e) => DoLaunch();
            launchWrap.Children.Add(launch);
            _launchSub = new WpfC.TextBlock
            {
                Text = "1.21.120  ·  官方大厅  ·  未登录",
                FontSize = 10.5,
                IsHitTestVisible = false,
                TextAlignment = Wpf.TextAlignment.Center,
                VerticalAlignment = Wpf.VerticalAlignment.Bottom,
                HorizontalAlignment = Wpf.HorizontalAlignment.Center,
                Margin = new Wpf.Thickness(35, 0, 35, 9),
                TextTrimming = Wpf.TextTrimming.CharacterEllipsis
            };
            _launchSub.SetResourceReference(WpfC.TextBlock.ForegroundProperty, "ColorBrushGray3");
            launchWrap.Children.Add(_launchSub);
            bottom.Children.Add(launchWrap);

            var row = new WpfC.Grid();
            row.ColumnDefinitions.Add(new WpfC.ColumnDefinition());
            row.ColumnDefinitions.Add(new WpfC.ColumnDefinition());
            var bBjd = new PCL.MyButton { Text = "一键布吉岛", Height = 35, Margin = new Wpf.Thickness(0, 0, 5, 0) };
            WpfC.TextBlock.SetFontSize(bBjd, 12.5);
            bBjd.Click += (s, e) => DoQuick("fastbjd", "一键布吉岛");
            var bEc = new PCL.MyButton { Text = "一键 EC", Height = 35, Margin = new Wpf.Thickness(5, 0, 0, 0) };
            WpfC.TextBlock.SetFontSize(bEc, 12.5);
            bEc.Click += (s, e) => DoQuick("fastec", "一键 EC");
            WpfC.Grid.SetColumn(bBjd, 0);
            WpfC.Grid.SetColumn(bEc, 1);
            row.Children.Add(bBjd);
            row.Children.Add(bEc);
            bottom.Children.Add(row);

            dock.Children.Add(bottom);

            // ===== 中部占位 =====
            // 必须最后添加：DockPanel 默认 LastChildFill，最后加入的子元素填充剩余空间，
            // 顺序反了会把 bottom 当填充格吞掉，启动键就悬在中间、底下空一坨
            dock.Children.Add(new WpfC.Border());

            left.Children.Add(dock);
            return left;
        }

        // ---------- 右侧分页（PCL MyCard 卡片式） ----------
        private void BuildPages(SolidColorBrush blue, SolidColorBrush text, SolidColorBrush sub)
        {
            _pages[0] = BuildLaunchPage(blue, text, sub);
            _pages[1] = BuildDownloadPage(blue, text, sub);
            _pages[2] = BuildLobbyPage(blue, text, sub);
            _pages[3] = BuildSetupPage(blue, text, sub);
            _pages[4] = BuildMorePage(blue, text, sub);
            foreach (var kv in _pages)
            {
                kv.Value.Visibility = Wpf.Visibility.Collapsed;
                _contentHost.Children.Add(kv.Value);
            }
        }

        private Wpf.FrameworkElement BuildLaunchPage(SolidColorBrush blue, SolidColorBrush text, SolidColorBrush sub)
        {
            var sv = new WpfC.ScrollViewer { VerticalScrollBarVisibility = WpfC.ScrollBarVisibility.Auto };
            var root = new WpfC.StackPanel { Margin = new Wpf.Thickness(20, 16, 20, 16) };
            sv.Content = root;

            _pageHint = new WpfC.TextBlock
            {
                Text = "选择账号并登录，然后启动客户端",
                FontSize = 11,
                Foreground = sub,
                Margin = new Wpf.Thickness(2, 0, 0, 12)
            };
            root.Children.Add(_pageHint);

            // ---- 卡片：启动设置 ----
            var card = new PCL.MyCard { Title = "启动设置", Margin = new Wpf.Thickness(0, 0, 0, 14) };
            var sp = new WpfC.StackPanel { Margin = new Wpf.Thickness(15, 35, 15, 15) };

            sp.Children.Add(Label("服务器", sub));
            // PCL 设置页原版列表行（MyListItem Clickable，自带悬停动画）；
            // 官方联机大厅入口已按需求移除，仅保留自定义地址进服
            _rbCustom.Title = "自定义服务器地址";
            _rbCustom.Info = "连接指定 IP 与端口进服";
            _rbCustom.Logo = Icon("IconButtonOffline");
            _rbCustom.LogoScale = 0.8;
            _rbCustom.Type = PCL.MyListItem.CheckType.Clickable;
            _rbCustom.PaddingLeft = 14;
            _rbCustom.Margin = new Wpf.Thickness(0, 2, 0, 2);
            sp.Children.Add(_rbCustom);

            // 联机大厅入口：点一下切到「联机大厅」页（浏览地图 → 选房间 → 一键进服）
            _rbLobby.Title = "联机大厅";
            _rbLobby.Info = "按地图浏览在线房间，一键查 IP 并直接进服";
            _rbLobby.Logo = Icon("IconButtonSetup");
            _rbLobby.LogoScale = 0.8;
            _rbLobby.Type = PCL.MyListItem.CheckType.Clickable;
            _rbLobby.PaddingLeft = 14;
            var lobbyHit = new WpfC.Border
            {
                Background = Wpf.Media.Brushes.Transparent,
                Cursor = Wpf.Input.Cursors.Hand,
                Margin = new Wpf.Thickness(0, 2, 0, 2),
                Child = _rbLobby
            };
            lobbyHit.MouseLeftButtonUp += (s, e) => NavTo(2);
            sp.Children.Add(lobbyHit);

            var addrRow = new WpfC.Grid { Margin = new Wpf.Thickness(0, 8, 0, 4) };
            addrRow.ColumnDefinitions.Add(new WpfC.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
            addrRow.ColumnDefinitions.Add(new WpfC.ColumnDefinition { Width = Wpf.GridLength.Auto });
            _addrBox.Height = 30;
            _addrBox.FontSize = 12;
            _addrBox.HintText = "127.0.0.1:1000";
            WpfC.Grid.SetColumn(_addrBox, 0);
            addrRow.Children.Add(_addrBox);
            _typeBox.Height = 30;
            _typeBox.FontSize = 12;
            _typeBox.Width = 160;
            _typeBox.Margin = new Wpf.Thickness(8, 0, 0, 0);
            _typeBox.Items.Add(new PCL.MyComboBoxItem { Content = "第三方服务器 (100)" });
            _typeBox.Items.Add(new PCL.MyComboBoxItem { Content = "官方大厅 (1000)" });
            _typeBox.Items.Add(new PCL.MyComboBoxItem { Content = "局域网 (1)" });
            _typeBox.SelectedIndex = 0;
            WpfC.Grid.SetColumn(_typeBox, 1);
            addrRow.Children.Add(_typeBox);
            _addrRow = addrRow;
            sp.Children.Add(addrRow);

            var hint = new PCL.MyHint
            {
                Text = "地址格式为 IP:端口，例：127.0.0.1:1000 或 play.bjdmc.net:19132",
                Theme = PCL.MyHint.Themes.Blue,
                Margin = new Wpf.Thickness(0, 10, 0, 0)
            };
            sp.Children.Add(hint);

            // ---- 房间号查 IP（用当前登录账号调 lobbyip，拿地址自动填入上面的地址框） ----
            sp.Children.Add(Label("房间号查 IP（当前账号）", sub));
            var roomRow = new WpfC.Grid { Margin = new Wpf.Thickness(0, 2, 0, 4) };
            roomRow.ColumnDefinitions.Add(new WpfC.ColumnDefinition { Width = new Wpf.GridLength(150) });
            roomRow.ColumnDefinitions.Add(new WpfC.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
            roomRow.ColumnDefinitions.Add(new WpfC.ColumnDefinition { Width = Wpf.GridLength.Auto });
            _roomKindBox.Height = 30;
            _roomKindBox.FontSize = 12;
            _roomKindBox.Margin = new Wpf.Thickness(0, 0, 8, 0);
            _roomKindBox.Items.Add(new PCL.MyComboBoxItem { Content = "大厅房间" });
            _roomKindBox.Items.Add(new PCL.MyComboBoxItem { Content = "租赁服" });
            _roomKindBox.Items.Add(new PCL.MyComboBoxItem { Content = "山头" });
            _roomKindBox.SelectedIndex = 0;
            WpfC.Grid.SetColumn(_roomKindBox, 0);
            roomRow.Children.Add(_roomKindBox);
            _roomIdBox.Height = 30;
            _roomIdBox.FontSize = 12;
            _roomIdBox.HintText = "输入房间号，例 123456789";
            WpfC.Grid.SetColumn(_roomIdBox, 1);
            roomRow.Children.Add(_roomIdBox);
            var bRoomIp = new PCL.MyButton
            {
                Text = "查 IP",
                Width = 80,
                Height = 30,
                ColorType = PCL.MyButton.ColorState.Highlight,
                Margin = new Wpf.Thickness(8, 0, 0, 0)
            };
            WpfC.TextBlock.SetFontSize(bRoomIp, 12);
            bRoomIp.Click += (s, e) => DoRoomIp();
            WpfC.Grid.SetColumn(bRoomIp, 2);
            roomRow.Children.Add(bRoomIp);
            _btnRoomIp = bRoomIp;
            sp.Children.Add(roomRow);

            // 状态反馈：获取中… / 获取成功 ip:port / 获取失败：原因
            _roomStatusTb.FontSize = 11.5;
            _roomStatusTb.Margin = new Wpf.Thickness(2, 8, 0, 0);
            _roomStatusTb.TextWrapping = Wpf.TextWrapping.Wrap;
            _roomStatusTb.Visibility = Wpf.Visibility.Collapsed;
            sp.Children.Add(_roomStatusTb);

            // 候选房间列表（同名房可能同时存在于 PC / PE 两个大厅）
            _roomCandList.Margin = new Wpf.Thickness(0);
            _roomCandBox = new WpfC.Border
            {
                Background = Brush("#141719"),
                BorderBrush = Brush("#2B2F34"),
                BorderThickness = new Wpf.Thickness(1),
                CornerRadius = new Wpf.CornerRadius(4),
                Padding = new Wpf.Thickness(6),
                Margin = new Wpf.Thickness(0, 8, 0, 0),
                Visibility = Wpf.Visibility.Collapsed,
                Child = _roomCandList
            };
            sp.Children.Add(_roomCandBox);

            var roomHint = new PCL.MyHint
            {
                Text = "输入房间号点「查 IP」：会同时搜 PC / PE 两个大厅；有多个同名房间时在下方列表里选一个再查。",
                Theme = PCL.MyHint.Themes.Yellow,
                Margin = new Wpf.Thickness(0, 6, 0, 0)
            };
            sp.Children.Add(roomHint);

            card.Children.Add(sp);
            root.Children.Add(card);

            // 运行日志卡片在「设置」页（BuildSetupPage）

            return sv;
        }

        private Wpf.FrameworkElement BuildSetupPage(SolidColorBrush blue, SolidColorBrush text, SolidColorBrush sub)
        {
            var sv = new WpfC.ScrollViewer { VerticalScrollBarVisibility = WpfC.ScrollBarVisibility.Auto };
            var root = new WpfC.StackPanel { Margin = new Wpf.Thickness(20, 16, 20, 16) };
            sv.Content = root;

            root.Children.Add(new WpfC.TextBlock
            {
                Text = "配置文件、诊断与运行状态",
                FontSize = 11,
                Foreground = sub,
                Margin = new Wpf.Thickness(2, 0, 0, 12)
            });

            var card = new PCL.MyCard { Title = "配置与诊断", Margin = new Wpf.Thickness(0, 0, 0, 14) };
            var sp = new WpfC.StackPanel { Margin = new Wpf.Thickness(15, 35, 15, 15) };

            // 游戏路径（自启动页移入设置页）
            sp.Children.Add(Label("游戏路径（x64_mc 目录）", sub));
            _pathBox.Height = 30;
            _pathBox.FontSize = 12;
            _pathBox.HintText = "例如 D:\\MinecraftPE\\x64_mc";
            sp.Children.Add(_pathBox);
            var browRow = new WpfC.StackPanel { Orientation = WpfC.Orientation.Horizontal, Margin = new Wpf.Thickness(0, 8, 0, 14) };
            var bBrowse = new PCL.MyButton { Text = "浏览", Width = 76, Height = 28, Margin = new Wpf.Thickness(0, 0, 8, 0) };
            bBrowse.Click += (s, e) => BrowsePath();
            var bSave = new PCL.MyButton { Text = "保存路径", Width = 90, Height = 28 };
            bSave.Click += (s, e) => SavePath();
            browRow.Children.Add(bBrowse);
            browRow.Children.Add(bSave);
            sp.Children.Add(browRow);

            var row = new WpfC.StackPanel { Orientation = WpfC.Orientation.Horizontal };
            var bCfg = new PCL.MyButton { Text = "编辑 config.json", Width = 160, Height = 30, Margin = new Wpf.Thickness(0, 0, 10, 0) };
            bCfg.Click += (s, e) =>
            {
                using (var dlg = new ConfigDialog())
                {
                    dlg.ShowDialog();
                    if (dlg.Saved) AppendLog("config.json 已保存，重启后生效\r\n");
                }
            };
            var bDiag = new PCL.MyButton { Text = "输出状态到日志", Width = 160, Height = 30 };
            bDiag.Click += (s, e) =>
            {
                AppendLog("› ES Tool 状态：命令 " + Commands.All.Count + " 条\r\n");
                AppendLog("  控制台=" + (AppHost.Started ? "已连接" : "未登录")
                          + "   UID=" + (string.IsNullOrEmpty(ConsoleAppLogin.NetEase.Http.UID) ? "-" : ConsoleAppLogin.NetEase.Http.UID) + "\r\n");
            };
            row.Children.Add(bCfg);
            row.Children.Add(bDiag);
            sp.Children.Add(row);

            _status.FontSize = 12;
            _status.Foreground = text;
            _status.Margin = new Wpf.Thickness(2, 14, 0, 0);
            sp.Children.Add(_status);

            var hint = new PCL.MyHint
            {
                Text = "游戏路径、服务器地址、版本号、ItemID 列表等都在 config.json；保存时自动备份。",
                Theme = PCL.MyHint.Themes.Yellow,
                Margin = new Wpf.Thickness(0, 12, 0, 0)
            };
            sp.Children.Add(hint);
            card.Children.Add(sp);
            root.Children.Add(card);

            // ---- 卡片：运行日志（原「功能」页已删除，日志随诊断一起放在设置页） ----
            var logCard = new PCL.MyCard { Title = "运行日志", Margin = new Wpf.Thickness(0, 0, 0, 14) };
            var lsp = new WpfC.StackPanel { Margin = new Wpf.Thickness(15, 35, 15, 15) };
            _logBox.Height = 150;
            _logBox.IsReadOnly = true;
            _logBox.AcceptsReturn = true;
            _logBox.TextWrapping = Wpf.TextWrapping.NoWrap;
            _logBox.VerticalScrollBarVisibility = WpfC.ScrollBarVisibility.Auto;
            _logBox.FontSize = 11;
            lsp.Children.Add(_logBox);
            _uid.FontSize = 10.5;
            _uid.Foreground = sub;
            _uid.Margin = new Wpf.Thickness(2, 8, 0, 0);
            lsp.Children.Add(_uid);
            logCard.Children.Add(lsp);
            root.Children.Add(logCard);

            return sv;
        }

        private Wpf.FrameworkElement BuildMorePage(SolidColorBrush blue, SolidColorBrush text, SolidColorBrush sub)
        {
            var sv = new WpfC.ScrollViewer { VerticalScrollBarVisibility = WpfC.ScrollBarVisibility.Auto };
            var root = new WpfC.StackPanel { Margin = new Wpf.Thickness(20, 16, 20, 16) };
            sv.Content = root;

            var card = new PCL.MyCard { Title = "关于 ES Tool", Margin = new Wpf.Thickness(0, 0, 0, 14) };
            var sp = new WpfC.StackPanel { Margin = new Wpf.Thickness(15, 35, 15, 15) };
            sp.Children.Add(new WpfC.TextBlock
            {
                Text = "ES Tool · Minecraft 中国版第三方启动器",
                FontSize = 13,
                FontWeight = Wpf.FontWeights.Bold,
                Foreground = text,
                Margin = new Wpf.Thickness(2, 0, 0, 8)
            });
            sp.Children.Add(new WpfC.TextBlock
            {
                Text = "基于原版挂服机（modpc）二次开发的图形界面版本插件，协议与功能保持一致。\r\n" +
                       "界面直接使用 PCL2（Plain Craft Launcher 2）程序集里的原生控件与 UI 动画。",
                FontSize = 11.5,
                Foreground = sub,
                TextWrapping = Wpf.TextWrapping.Wrap,
                LineHeight = 20,
                Margin = new Wpf.Thickness(2, 0, 0, 10)
            });
            sp.Children.Add(new WpfC.TextBlock
            {
                Text = "本项目纯公益开源项目，禁止倒卖",
                FontSize = 11.5,
                FontWeight = Wpf.FontWeights.Bold,
                Foreground = Brush("#E05A5A"),
                Margin = new Wpf.Thickness(2, 0, 0, 10)
            });

            // 项目地址（可点开浏览器）
            var urlRow = new WpfC.StackPanel
            {
                Orientation = WpfC.Orientation.Horizontal,
                Margin = new Wpf.Thickness(2, 0, 0, 10)
            };
            urlRow.Children.Add(new WpfC.TextBlock
            {
                Text = "项目地址  ",
                FontSize = 11.5,
                Foreground = sub,
                VerticalAlignment = Wpf.VerticalAlignment.Center
            });
            var urlLink = new WpfC.TextBlock
            {
                Text = "https://github.com/ESCc13/ES-Tool",
                FontSize = 11.5,
                Foreground = blue,
                Cursor = Wpf.Input.Cursors.Hand,
                VerticalAlignment = Wpf.VerticalAlignment.Center
            };
            urlLink.MouseLeftButtonUp += (s, e) =>
            {
                try { System.Diagnostics.Process.Start("https://github.com/ESCc13/ES-Tool"); } catch { }
            };
            urlRow.Children.Add(urlLink);
            sp.Children.Add(urlRow);

            // 开发者名单
            sp.Children.Add(new WpfC.TextBlock
            {
                Text = "开发者名单",
                FontSize = 11.5,
                Foreground = sub,
                Margin = new Wpf.Thickness(2, 0, 0, 4)
            });
            foreach (var dev in new[] { "kaka", "Publish VarInt", "BivUraMLP" })
            {
                sp.Children.Add(new WpfC.TextBlock
                {
                    Text = "· " + dev,
                    FontSize = 11.5,
                    Foreground = text,
                    Margin = new Wpf.Thickness(2, 0, 0, 2)
                });
            }

            card.Children.Add(sp);
            root.Children.Add(card);

            return sv;
        }

        // ---------- 导航 ----------
        private void NavTo(int idx)
        {
            int prev = _navIndex;
            _navIndex = idx;
            if (!_navGuard)
            {
                _navGuard = true;
                try
                {
                    foreach (var n in _navs)
                        n.Rb.SetChecked(n.Index == idx, false, false);
                }
                catch { }
                finally { _navGuard = false; }
            }
            // 先收弹层再切可见性，避免 Popup 在布局更新期间被销毁（见 CloseOpenDropDowns 注释）
            if (prev != idx) CloseOpenDropDowns();
            foreach (var kv in _pages)
                kv.Value.Visibility = kv.Key == idx ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;

            // 页面切换：新页面淡入 + 轻微上浮，曲线/时长与 PCL 的 MyCard 页面切换同源
            if (prev != idx)
            {
                Wpf.FrameworkElement page;
                if (_pages.TryGetValue(idx, out page) && page != null)
                {
                    try { PclAnim.SlideFadeIn(page, 10, PclAnim.Normal); } catch { }
                }
            }

            // 下载页：首次进入补默认路径并按需拉版本列表（对应 PCL 的 PageLoaded）
            if (idx == DownloadPageIndex) EnsureDownloadPageReady();

            // 联机大厅：第一次进去且已登录，自动拉一次全服在线地图池，不用先点搜索
            if (idx == 2 && !_lbLoaded && AppHost.Started)
            {
                _lbLoaded = true;
                LobbySearch();
            }
        }

        private void UpdateAddrRow()
        {
            // 官方联机大厅入口已移除，自定义地址行常显
            if (_addrRow == null) return;
            _addrRow.Visibility = Wpf.Visibility.Visible;
        }

        // ---------- 小工具 ----------
        private static WpfC.TextBlock Label(string text, SolidColorBrush fg)
        {
            return new WpfC.TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = fg,
                Margin = new Wpf.Thickness(2, 0, 0, 5)
            };
        }

        /// <summary>
        /// 合并 PCL2 的完整前端资源（颜色表 + 全部控件样式/模板）。
        /// 原样取自 PCL2 的 Application.xaml，存于 pcl\PclStyles.xaml；
        /// 因 PCL 程序集名带空格无法过编译期 clr-namespace 校验，用 XamlReader.Parse 运行时加载。
        /// 失败则回退最小颜色表 PclColors.xaml。
        /// </summary>
        internal static void LoadPclStyles(Wpf.Application app)
        {
            EnsurePclRuntime();
            if (app == null || app.Resources["PclStylesLoaded"] != null) return;
            try
            {
                string p = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pcl", "PclStyles.xaml");
                if (System.IO.File.Exists(p))
                {
                    string x = System.IO.File.ReadAllText(p);
                    // 文件根是 <Grid><Grid.Resources>（ResourceDictionary 不能做解析根：
                    // 模板内 x:Name 无法注册到 RD 的名字范围），解析后取 Resources 合并
                    var grid = (WpfC.Grid)System.Windows.Markup.XamlReader.Parse(x);
                    var dict = grid.Resources;
                    grid.Resources = new Wpf.ResourceDictionary();
                    app.Resources.MergedDictionaries.Add(dict);
                    app.Resources["PclStylesLoaded"] = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                try
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                        "[" + DateTime.Now.ToString("HH:mm:ss") + "] PclStyles parse failed\r\n" + ex + "\r\n\r\n",
                        System.Text.Encoding.UTF8);
                }
                catch { }
            }
            // 回退：最小颜色表（保证 PCL 控件的 StaticResource 基本可解析）
            try
            {
                app.Resources.MergedDictionaries.Add(new Wpf.ResourceDictionary
                {
                    Source = new Uri("PclColors.xaml", UriKind.Relative)
                });
                app.Resources["PclStylesLoaded"] = true;
            }
            catch { }
        }

        private static bool _pclRuntimeReady;

        /// <summary>
        /// 启动 PCL 的常驻动画线程（PCL 原版在 FormMain.Loaded 里调用 AniStart()）。
        /// AniStart(AniData) 只是把动画注册进 AniGroups，真正逐帧执行靠这条线程；
        /// 宿主进程不启动它，所有 AaColor/AaScale 动画只注册不执行——
        /// MyIconButton 的 Path.Fill、按钮悬停变色、卡片展开等全部失效。
        /// </summary>
        internal static void EnsurePclRuntime()
        {
            if (_pclRuntimeReady) return;
            _pclRuntimeReady = true;
            try
            {
                // PCL 原版在 Application_Startup 里做的事，宿主进程必须补齐：
                // 1) MeloongCore 核心 / WPF 前端初始化
                MeloongCore.Main.Init("PCL");
                MeloongCore.Wpf.Main.Init();
                // 2) 日志器实例。不初始化它，MyIconButton 点击链里的 Logger.Info
                //    会因 Logger.Instance 为 Nothing 抛 NRE——点任何图标必崩。
                var logger = new PCL.PclLogger { logFolder = MeloongCore.Paths.BaseThenName };
                try
                {
                    typeof(MeloongCore.BaseLogger)
                        .GetProperty("MinLevel")
                        ?.SetValue(logger, MeloongCore.LogLevel.Info, null);
                }
                catch { }
                MeloongCore.Logger.Instance = logger;
                // 3) 常驻动画线程（AaColor/AaScale 只注册，逐帧执行靠这条线程）
                PCL.ModAnimation.AniStart();
            }
            catch (Exception ex)
            {
                try
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                        "[" + DateTime.Now.ToString("HH:mm:ss") + "] PCL runtime init failed\r\n" + ex + "\r\n\r\n",
                        System.Text.Encoding.UTF8);
                }
                catch { }
            }
        }

        /// <summary>
        /// 构造后立即给图标上色：临时拉高 AniControlEnabled 走 PCL 的无动画分支，
        /// RefreshAnim 会 SetResourceReference(Fill, "ColorBrush8") 直接上色，
        /// 不依赖动画线程的首次帧时序（与 MyIconButton.Foreground setter 内部的做法一致）。
        /// </summary>
        private static void PaintIcon(PCL.MyIconButton icon)
        {
            if (icon == null) return;
            try
            {
                PCL.ModAnimation.AniControlEnabled += 1;
                icon.RefreshAnim();
            }
            catch { }
            finally
            {
                try { PCL.ModAnimation.AniControlEnabled -= 1; } catch { }
            }
        }

        /// <summary>PCL2 的图标矢量路径常量（PCL.ModBase.Logo），原版图标 1:1</summary>
        private static new string Icon(string name)
        {
            try
            {
                var t = typeof(PCL.MyButton).Assembly.GetType("PCL.ModBase+Logo");
                if (t == null) return null;
                // VB Module 的图标路径常量是 Public Shared 字段
                var f = t.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                return f == null ? null : (string)f.GetValue(null);
            }
            catch { return null; }
        }

        /// <summary>PCL2 内嵌的英文字体（标题 Logo 用）</summary>
        private static FontFamily LogoFont()
        {
            try
            {
                return new FontFamily(new Uri("pack://application:,,,/ModPcGUI;component/"), "./PclFont.ttf#PCL English");
            }
            catch { return new FontFamily("Microsoft YaHei UI, Microsoft YaHei"); }
        }

        /// <summary>判断命中点是否在可点击控件上（是则不触发窗口拖动）</summary>
        private static bool InInteractive(Wpf.DependencyObject d)
        {
            while (d != null)
            {
                if (d is PCL.MyIconButton || d is PCL.MyRadioButton || d is System.Windows.Controls.Primitives.ButtonBase || d is PCL.MyButton) return true;
                d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        /// <summary>
        /// 收掉所有还开着的下拉弹层。
        /// WPF for .NET Framework 有个已知缺陷：Popup 在布局更新期间被销毁会在
        /// Popup.OnWindowResize 里抛 NullReferenceException，而且销毁流程被打断后会
        /// 由 DispatcherTimer 反复重试，日志里就是每秒重复的两条 NRE。
        /// 切页会触发整棵树的可见性级联 + UpdateLayout，正好撞上，所以切页前先主动关闭。
        /// </summary>
        private void CloseOpenDropDowns()
        {
            try
            {
                var list = new System.Collections.Generic.List<System.Windows.Controls.ComboBox>();
                CollectVisuals(this, list);
                for (int i = 0; i < list.Count; i++)
                {
                    try { if (list[i].IsDropDownOpen) list[i].IsDropDownOpen = false; }
                    catch { }
                }
            }
            catch { }
        }

        private static void CollectVisuals<T>(Wpf.DependencyObject root, System.Collections.Generic.List<T> sink)
            where T : Wpf.DependencyObject
        {
            int n;
            try { n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); }
            catch { return; }
            for (int i = 0; i < n; i++)
            {
                Wpf.DependencyObject c;
                try { c = System.Windows.Media.VisualTreeHelper.GetChild(root, i); }
                catch { continue; }
                var hit = c as T;
                if (hit != null) sink.Add(hit);
                CollectVisuals(c, sink);
            }
        }

        private static SolidColorBrush Brush(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(c);
        }

        /// <summary>头像：优先用 PCL 资源里的史蒂夫头，缺失则空</summary>
        private static ImageSource LoadHead()
        {
            try
            {
                string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pcl", "head.png");
                if (File.Exists(p))
                    return new BitmapImage(new Uri(p, UriKind.Absolute));
            }
            catch { }
            return null;
        }

        private void UpdateLaunchCaption()
        {
            if (_launchSub == null) return;
            string v = "1.21.120";
            try { if (text.app.Temp != null && !string.IsNullOrEmpty(text.app.Temp.Version)) v = text.app.Temp.Version; }
            catch { }
            string mode = "自定义地址";
            string acc = "未登录";
            try
            {
                int i = _accBox.SelectedIndex;
                if (i >= 0 && i < _accItems.Count) acc = _accItems[i].Name;
            }
            catch { }
            _launchSub.Text = v + "  ·  " + mode + "  ·  " + acc;
        }
    }
}
