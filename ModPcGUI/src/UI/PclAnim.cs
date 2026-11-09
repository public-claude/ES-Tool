using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ModPcGUI
{
    /// <summary>
    /// ES Tool 的动画层 —— 时长、曲线、缓动全部照抄 PCL2 的 ModAnimation
    /// （`PCL NEMC/Plain Craft Launcher 2/Modules/Base/ModAnimation.vb`）。
    ///
    /// 两条执行路径：
    ///   1. 宿主有 pcl\PCL.Core.dll 时，直接调 PCL 自己的引擎
    ///      （PCL.ModAnimation.AaOpacity / AaTranslateY / AniStart）。
    ///      这样曲线、时序、帧循环和 MyCard 的悬停变色跑在同一套 AniGroups 里，
    ///      不会出现两套动画互相抢属性的情况。
    ///      这里走反射而不是强类型引用：PCL.Core.dll 缺失、或版本里的方法签名变了，
    ///      都只会退化成路径 2，不会让整个项目编译不过。
    ///   2. 引擎不可用时退回 WPF 原生 Storyboard，曲线取 PCL 公式的等价缓动
    ///      （AniEaseOutFluent(p=3) 就是 1-(1-t)^3，与 CubicEase/EaseOut 完全一致）。
    ///
    /// 约定：只做“加法”动画 —— 先把初始状态直接写死（Opacity / RenderTransform），
    /// 再让动画推到目标值，不用 Storyboard 的 From，避免首帧闪一下。
    /// </summary>
    internal static class PclAnim
    {
        // PCL 常用时长（毫秒），取自 MyCard.vb / MyListItem.xaml.vb 的实参
        public const int Fast = 90;      // 悬停变色
        public const int Normal = 200;   // 位移、淡入
        public const int Slow = 260;     // 面板整体入场
        public const int Step = 25;      // 列表逐项出现的间隔（AaStack 默认值）

        // ---------------- PCL 引擎反射句柄 ----------------
        private static bool _probed;
        private static bool _engineReady;
        private static Type _mod;             // PCL.ModAnimation
        private static MethodInfo _aaOpacity, _aaTranslateY, _aaTranslateX, _aniStart, _aniStartLoop;
        private static PropertyInfo _aniRunning;
        private static object _easeOutStrong; // PCL.ModAnimation+AniEaseOutFluent(ExtraStrong)

        private static void Probe()
        {
            if (_probed) return;
            _probed = true;
            try
            {
                // PCL.MyButton 是本项目已经强类型引用的类型，拿它的程序集最稳
                var asm = typeof(PCL.MyButton).Assembly;
                _mod = asm.GetType("PCL.ModAnimation", false);
                if (_mod == null) return;

                _aaOpacity = FindAa("AaOpacity");
                _aaTranslateY = FindAa("AaTranslateY");
                _aaTranslateX = FindAa("AaTranslateX");

                foreach (var m in _mod.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "AniStart") continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 0) _aniStartLoop = m;                       // AniStart()：拉起动画线程
                    else if (ps.Length == 3 && ps[0].ParameterType == typeof(IList))
                        _aniStart = m;                                           // AniStart(IList, String, Boolean)
                }
                _aniRunning = _mod.GetProperty("AniRunning", BindingFlags.Public | BindingFlags.Static);

                // 缓动：AniEasePower.ExtraStrong = 5
                var powerEnum = asm.GetType("PCL.ModAnimation+AniEasePower", false);
                var easeType = asm.GetType("PCL.ModAnimation+AniEaseOutFluent", false);
                if (powerEnum != null && easeType != null)
                {
                    var strong = Enum.ToObject(powerEnum, 5);
                    _easeOutStrong = Activator.CreateInstance(easeType, new[] { strong });
                }

                _engineReady = _aaOpacity != null && _aniStart != null;
            }
            catch
            {
                _engineReady = false;
            }
        }

        private static MethodInfo FindAa(string name)
        {
            foreach (var m in _mod.GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (m.Name == name) return m;
            return null;
        }

        /// <summary>
        /// PCL 的 Aa* 只把动画注册进 AniGroups，真正逐帧执行靠常驻动画线程。
        /// 线程没起来的话动画永远停在注册那一刻 —— 元素会卡在 Opacity=0 不显示。
        /// 这里读 PCL 自己的 AniRunning 标记，没跑才拉起，避免重复开线程。
        /// </summary>
        private static void EnsureLoop()
        {
            try
            {
                bool running = false;
                if (_aniRunning != null)
                {
                    var v = _aniRunning.GetValue(null, null);
                    running = v is bool && (bool)v;
                }
                if (!running && _aniStartLoop != null) _aniStartLoop.Invoke(null, null);
            }
            catch { }
        }

        /// <summary>把一组 AniData 交给 PCL 引擎执行；失败返回 false 让调用方降级。</summary>
        private static bool Run(params object[] anis)
        {
            Probe();
            if (!_engineReady) return false;
            try
            {
                var list = new ArrayList();
                foreach (var a in anis)
                    if (a != null) list.Add(a);
                if (list.Count == 0) return false;
                EnsureLoop();
                _aniStart.Invoke(null, new object[] { list, "", false });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object Aa(MethodInfo mi, object obj, double value, int ms, int delay)
        {
            if (mi == null) return null;
            try
            {
                // (Obj, Value, Time, Delay, Ease, After)
                return mi.Invoke(null, new object[] { obj, value, ms, delay, _easeOutStrong, false });
            }
            catch
            {
                try { return mi.Invoke(null, new object[] { obj, value, ms, delay, null, false }); }
                catch { return null; }
            }
        }

        /// <summary>
        /// 保证元素上的 RenderTransform 就是一个直接的 TranslateTransform。
        /// 不能套 TransformGroup：PCL 的 AaTranslateY 只判断「RenderTransform 是不是
        /// TranslateTransform」，遇到别的类型会直接把 RenderTransform 重置成新的
        /// TranslateTransform(0,0)，包在 Group 里的原变换会整个丢掉。
        /// </summary>
        private static TranslateTransform EnsureTranslate(FrameworkElement e)
        {
            var tt = e.RenderTransform as TranslateTransform;
            if (tt != null) return tt;
            tt = new TranslateTransform();
            e.RenderTransform = tt;
            return tt;
        }

        // ---------------- 对外 API ----------------

        /// <summary>淡入：元素透明度 from → 1。</summary>
        public static void FadeIn(UIElement e, int ms = Normal, int delay = 0, double from = 0)
        {
            if (e == null) return;
            e.Opacity = from;
            if (Run(Aa(_aaOpacity, e, 1 - from, ms, delay))) return;
            e.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(ms))
                {
                    BeginTime = TimeSpan.FromMilliseconds(delay),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.HoldEnd
                });
        }

        /// <summary>上浮淡入：从下方 dy 像素处移到原位并淡入。PCL 页面/面板入场的常用手感。</summary>
        public static void SlideFadeIn(FrameworkElement e, double dy = 12, int ms = Slow, int delay = 0, double from = 0)
        {
            if (e == null) return;
            e.Opacity = from;
            var tt = EnsureTranslate(e);
            tt.Y = dy;

            var anis = new List<object>();
            var a1 = Aa(_aaOpacity, e, 1 - from, ms, delay);
            if (a1 != null) anis.Add(a1);
            var a2 = Aa(_aaTranslateY, e, -dy, ms, delay);
            if (a2 != null) anis.Add(a2);
            if (anis.Count > 0 && Run(anis.ToArray())) return;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            e.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(ms))
                { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = ease });
            tt.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(ms))
                { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = ease });
        }

        /// <summary>横向滑入：从 dx 像素处滑到原位，dx 的符号决定方向（切换页面用）。</summary>
        public static void SlideX(FrameworkElement e, double dx = 16, int ms = Normal, int delay = 0)
        {
            if (e == null) return;
            var tt = EnsureTranslate(e);
            tt.X = dx;

            if (Run(Aa(_aaTranslateX, e, -dx, ms, delay))) return;
            tt.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(ms))
                {
                    BeginTime = TimeSpan.FromMilliseconds(delay),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }

        /// <summary>按下回弹：按下缩到 scale，松手自动弹回。对应 MyCard 的点击反馈。</summary>
        public static void Press(FrameworkElement e, double scale = 0.97, int ms = 110)
        {
            if (e == null) return;
            if (e.RenderTransformOrigin == new Point(0, 0))
                e.RenderTransformOrigin = new Point(0.5, 0.5);

            var st = FindScale(e);
            if (st == null)
            {
                st = new ScaleTransform(1, 1);
                var group = e.RenderTransform as TransformGroup;
                if (group == null)
                {
                    group = new TransformGroup();
                    if (e.RenderTransform != null && !e.RenderTransform.Value.IsIdentity)
                        group.Children.Add(e.RenderTransform);
                    group.Children.Add(st);
                    e.RenderTransform = group;
                }
                else
                {
                    group.Children.Add(st);
                }
            }

            var a = new DoubleAnimation(scale, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                AutoReverse = true,
                FillBehavior = FillBehavior.Stop
            };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, a.Clone());
        }

        private static ScaleTransform FindScale(FrameworkElement e)
        {
            if (e.RenderTransform is ScaleTransform s) return s;
            if (e.RenderTransform is TransformGroup g)
                foreach (var t in g.Children)
                    if (t is ScaleTransform ss) return ss;
            return null;
        }

        /// <summary>十六进制重载：HoverBackground(row, "#1B1F23", "#232A31")。</summary>
        public static void HoverBackground(Border border, string normalHex, string hoverHex, int ms = Fast)
        {
            if (border == null) return;
            HoverBackground(border, ParseColor(normalHex), ParseColor(hoverHex), ms);
        }

        private static Color ParseColor(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Colors.Transparent; }
        }

        /// <summary>
        /// 给 Border 挂 PCL 式悬停变色：90ms 过渡到 hover 色，离开再过渡回来。
        /// MyCard 用的就是 AaColor + 90ms，这里保持同一时长与曲线。
        /// </summary>
        public static void HoverBackground(Border border, Color normal, Color hover, int ms = Fast)
        {
            if (border == null) return;

            // 每个 Border 必须独立画刷，否则动画会串到共享画刷上
            var brush = new SolidColorBrush(normal);
            border.Background = brush;

            border.MouseEnter += (s, e) => AnimateBrush(brush, hover, ms);
            border.MouseLeave += (s, e) => AnimateBrush(brush, normal, ms);
        }

        private static void AnimateBrush(SolidColorBrush brush, Color to, int ms)
        {
            if (brush == null || brush.IsFrozen) return;
            brush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(to, TimeSpan.FromMilliseconds(ms))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.HoldEnd
                });
        }

        /// <summary>列表逐项入场：第 i 项延迟 i*Step 毫秒，与 PCL 的 AaStack 一致。</summary>
        public static void Cascade(IEnumerable<FrameworkElement> items, double dy = 10, int ms = Normal, int step = Step)
        {
            if (items == null) return;
            int i = 0;
            foreach (var it in items)
            {
                if (it != null) SlideFadeIn(it, dy, ms, i * step);
                i++;
            }
        }
    }
}
