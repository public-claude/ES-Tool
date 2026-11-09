using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ModPcGUI
{
    internal static class Boot
    {
        private static Mutex _single;
        private const string MutexName = "ESTool_SingleInstance_v1";

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr h);

        [STAThread]
        private static void Main(string[] args)
        {
            // PCL2 的原生控件依赖（MeloongCore / PCLCS / Newtonsoft.Json …）
            // 全部放在 pcl\ 子目录，不污染 ModPc 主目录。
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                try
                {
                    string name = new System.Reflection.AssemblyName(e.Name).Name;
                    string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pcl");

                    // PCL2 程序集已从「Plain Craft Launcher 2.exe」改名为 PCL.Core.dll
                    // （避免项目里躺着一个能双击直接打开 PCL2 的 exe）。
                    // 程序集内部标识仍是「Plain Craft Launcher 2」，按文件名找不到，这里手动对上。
                    if (string.Equals(name, "Plain Craft Launcher 2", StringComparison.OrdinalIgnoreCase))
                    {
                        string renamed = System.IO.Path.Combine(dir, "PCL.Core.dll");
                        if (System.IO.File.Exists(renamed))
                            return System.Reflection.Assembly.LoadFrom(renamed);
                    }

                    foreach (var ext in new[] { ".dll", ".exe" })
                    {
                        string p = System.IO.Path.Combine(dir, name + ext);
                        if (System.IO.File.Exists(p))
                            return System.Reflection.Assembly.LoadFrom(p);
                    }
                }
                catch { }
                return null;
            };

            // 自检模式：不开窗体，跑一遍初始化并写日志
            if (args != null && args.Length > 0 && args[0] == "--selftest")
            {
                SelfTest();
                return;
            }

            // 房间查询无头自测：--roomtest <房间号> [room|rental]
            // 用 user\ 第一个账号真实登录 → 查地址 → 写 roomtest.log → 退出
            if (args != null && args.Length > 0 && args[0] == "--roomtest")
            {
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                    Log("roomtest", e.ExceptionObject.ToString());
                try { ModPcGUI.RoomTest.Run(args); } catch (Exception ex) { Log("roomtest", ex.ToString()); }
                return;
            }

            // 单实例保护。原程序有 40+ 个前台线程，关窗口后进程可能残留，
            // 光靠 Mutex 会把「残留但没窗口」误判成「已有一个窗口」。
            bool multi = args != null && Array.IndexOf(args, "--multi") >= 0;
            bool force = args != null && Array.IndexOf(args, "--force") >= 0;
            if (!multi && !force)
            {
                int killed = CleanupZombies();
                bool created;
                _single = new Mutex(true, MutexName, out created);
                if (!created && HasVisibleWindow())
                {
                    ActivateExisting();
                    MessageBox.Show(
                        "已经有一个 ES Tool 窗口在运行了，已帮你切过去。\r\n\r\n" +
                        "同时开多个会抢同一个本地认证端口（11451），" +
                        "「一键布吉岛 / 一键 EC」会报 HttpListener 冲突。\r\n\r\n" +
                        "确实要多开就在快捷方式后加 --multi。",
                        "ES Tool", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                // Mutex 被占但没有可见窗口 = 上次没退干净的僵尸，直接继续启动
                if (!created && killed == 0) Log("Boot", "检测到残留 Mutex 但无可见窗口，已忽略");
            }

            // 兼容原有命令行用法：ModPcGUI.exe --legacy <原参数...>
            if (args != null && args.Length > 0 && args[0] == "--legacy")
            {
                var rest = new string[args.Length - 1];
                Array.Copy(args, 1, rest, 0, rest.Length);
                text.Program.LegacyMain(rest);
                return;
            }

            // 默认启动 PCL2 原生 UI 前端；--classic 回到原来的 WinForms 深色界面。
            bool classic = args != null && Array.IndexOf(args, "--classic") >= 0;
            if (!classic)
            {
                try
                {
                    var wpf = new System.Windows.Application();
                    // PCL2 完整前端资源：颜色表 + 全部控件样式/模板（原样取自其 Application.xaml）
                    ModPcGUI.PclMainWindow.LoadPclStyles(wpf);
                    // 全局兜底：PCL 控件库在宿主进程里可能有低概率暗雷（布局回调、Popup 内部等），
                    // 一旦触发就是整进程闪退；这里记入 crash.log 并继续运行
                    wpf.DispatcherUnhandledException += (s, e) =>
                    {
                        try
                        {
                            var ex = e.Exception;
                            if (IsKnownPclNoise(ex))
                            {
                                // 同一签名 60 秒内只记首次：这类异常由 DispatcherTimer 每秒重试，
                                // 全量记录会把 crash.log 刷到几 MB，反而盖住真正的新问题
                                string key = (ex.StackTrace ?? "").Split('\n')[0].Trim();
                                DateTime last;
                                _noiseStamp.TryGetValue(key, out last);
                                if ((DateTime.Now - last).TotalSeconds < 60)
                                {
                                    e.Handled = true;
                                    return;
                                }
                                _noiseStamp[key] = DateTime.Now;
                                Log("Dispatcher", ex.ToString());
                                e.Handled = true;
                                return;
                            }
                            Log("Dispatcher", ex.ToString());
                        }
                        catch { }
                        e.Handled = true;
                    };
                    wpf.ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
                    var win = new ModPcGUI.PclMainWindow();
                    wpf.MainWindow = win;
                    // --demo：自动进入离线控制台（Started=true 走与登录相同的刷新路径）
                    if (args != null && Array.IndexOf(args, "--demo") >= 0)
                        win.Loaded += (s, e) => ModPcGUI.DemoRunner.Run();
                    wpf.Run(win);
                    return;
                }
                catch (Exception ex)
                {
                    Log("PclUI", ex.ToString());
                    MessageBox.Show("PCL UI 启动失败，已切回经典界面：\r\n" + ex.Message,
                        "ES Tool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Log("UnhandledException", ex == null ? e.ExceptionObject.ToString() : ex.ToString());
                try
                {
                    MessageBox.Show("未处理异常：\r\n" + (ex == null ? e.ExceptionObject.ToString() : ex.Message),
                        "ES Tool", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
            };

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log("ThreadException", e.Exception.ToString());
                var frm = new MainForm();
                bool demo = args != null && Array.IndexOf(args, "--demo") >= 0;
                if (demo) frm.Shown += (s, e) => DemoRunner.Run();
                if (args != null && Array.IndexOf(args, "--features") >= 0)
                    frm.Shown += (s, e) => frm.BeginInvoke(new Action(frm.ShowFeatures));
                Application.Run(frm);
            }
            catch (Exception ex)
            {
                Log("Boot", ex.ToString());
                MessageBox.Show("启动失败：\r\n" + ex, "ES Tool",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>清理「无窗口且已启动超过 10 秒」的残留进程</summary>
        private static int CleanupZombies()
        {
            int n = 0;
            int me;
            try { me = Process.GetCurrentProcess().Id; } catch { return 0; }

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == me) continue;
                    if (!p.ProcessName.StartsWith("ModPcGUI", StringComparison.OrdinalIgnoreCase)) continue;
                    if (p.MainWindowHandle != IntPtr.Zero) continue;
                    if ((DateTime.Now - p.StartTime).TotalSeconds < 10) continue;
                    p.Kill();
                    p.WaitForExit(1000);
                    n++;
                }
                catch { }
            }
            if (n > 0) Log("Boot", "清理残留进程 " + n + " 个");
            return n;
        }

        private static bool HasVisibleWindow()
        {
            int me;
            try { me = Process.GetCurrentProcess().Id; } catch { me = -1; }
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == me) continue;
                    if (!p.ProcessName.StartsWith("ModPcGUI", StringComparison.OrdinalIgnoreCase)) continue;
                    if (p.MainWindowHandle != IntPtr.Zero && IsWindowVisible(p.MainWindowHandle))
                        return true;
                }
                catch { }
            }
            return false;
        }

        private static void ActivateExisting()
        {
            int me;
            try { me = Process.GetCurrentProcess().Id; } catch { me = -1; }
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == me) continue;
                    if (!p.ProcessName.StartsWith("ModPcGUI", StringComparison.OrdinalIgnoreCase)) continue;
                    IntPtr h = p.MainWindowHandle;
                    if (h != IntPtr.Zero && IsWindowVisible(h))
                    {
                        ShowWindow(h, 9);
                        SetForegroundWindow(h);
                        return;
                    }
                }
                catch { }
            }
        }

        private static void Log(string tag, string msg)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                    "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + tag + "\r\n" + msg + "\r\n\r\n",
                    System.Text.Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>已知噪音签名 -> 上次记录时间（用于 crash.log 去重）</summary>
        private static readonly System.Collections.Generic.Dictionary<string, DateTime> _noiseStamp
            = new System.Collections.Generic.Dictionary<string, DateTime>();

        /// <summary>
        /// 判断是否为「PCL 控件与宿主环境不兼容」导致的已知噪音异常。
        /// 只认两个确切签名，其余异常一律照常记录，避免掩盖新问题：
        /// 1) Popup.OnWindowResize —— WPF(.NET Framework) 在弹层销毁期间重排时的 NRE；
        /// 2) MyScrollViewer_IsVisibleChanged —— PCL 该回调未判空直接读 FrmMain（宿主无此单例）。
        /// 第 2 类已在 PclStyles.xaml 里通过改用原生 ScrollViewer 消除，
        /// 这里只是兜底：万一别处又冒出同类异常，也不至于被每秒重试刷爆日志。
        /// </summary>
        private static bool IsKnownPclNoise(Exception ex)
        {
            if (!(ex is NullReferenceException)) return false;
            string st = ex.StackTrace;
            if (string.IsNullOrEmpty(st)) return false;
            return st.IndexOf("Popup.OnWindowResize", StringComparison.Ordinal) >= 0
                || st.IndexOf("MyScrollViewer_IsVisibleChanged", StringComparison.Ordinal) >= 0;
        }

        private static void SelfTest()
        {
            var sb = new System.Text.StringBuilder();
            Term.OnOutput += (t, nl, c) => { sb.Append(t); if (nl) sb.Append("\r\n"); };

            sb.Append("== ES Tool 自检 ==\r\n");
            sb.Append("WorkDir: ").Append(System.IO.Directory.GetCurrentDirectory()).Append("\r\n");
            sb.Append("BaseDir: ").Append(AppDomain.CurrentDomain.BaseDirectory).Append("\r\n");

            try
            {
                AppHost.Init();
                sb.Append("[OK] Init 完成\r\n");
                sb.Append("Temp 版本: ").Append(text.app.Temp == null ? "null" : text.app.Temp.Version).Append("\r\n");
                sb.Append("服务器: ").Append(text.app.Temp == null ? "null" : text.app.Temp.url).Append("\r\n");
            }
            catch (Exception ex)
            {
                sb.Append("[FAIL] Init: ").Append(ex).Append("\r\n");
            }

            try
            {
                var accs = AppHost.ListAccounts();
                sb.Append("[OK] 账号数量: ").Append(accs.Count).Append("\r\n");
                foreach (var a in accs) sb.Append("   - ").Append(a).Append("\r\n");
            }
            catch (Exception ex)
            {
                sb.Append("[FAIL] 账号: ").Append(ex.Message).Append("\r\n");
            }

            sb.Append("[OK] 命令总数: ").Append(ModPcGUI.Commands.All.Count).Append("\r\n");

            // 本地认证服：验证端口占用时能自动顺延
            try
            {
                ConsoleAppLogin.NetEase.Http.Authserver(11451);
                sb.Append("[认证服] ").Append(ConsoleAppLogin.NetEase.Http.AuthServerUrl)
                  .Append("  IsAuthServer=").Append(ConsoleAppLogin.NetEase.Http.IsAuthServer).Append("\r\n");
                ConsoleAppLogin.NetEase.Http.CloseAuthserver();
                sb.Append("[认证服] 已释放\r\n");
            }
            catch (Exception ex)
            {
                sb.Append("[FAIL] 认证服: ").Append(ex.Message).Append("\r\n");
            }

            // 构造一次「更多功能」窗口，验证 UI 构建不抛异常
            try
            {
                var lv = new ModPcGUI.LogView();
                using (var ff = new ModPcGUI.FeaturesForm(lv))
                {
                    sb.Append("[OK] FeaturesForm 构造成功, 命令 ").Append(ModPcGUI.Commands.All.Count).Append("\r\n");
                }
            }
            catch (Exception ex)
            {
                sb.Append("[FAIL] FeaturesForm: ").Append(ex).Append("\r\n");
            }

            try
            {
                using (var mf = new ModPcGUI.MainForm())
                {
                    sb.Append("[OK] MainForm 构造成功\r\n");
                }
            }
            catch (Exception ex)
            {
                sb.Append("[FAIL] MainForm: ").Append(ex).Append("\r\n");
            }

            // 验证本地签名能力（不联网）
            try
            {
                byte[] sign = Login.Native.CoreSign.PESignCount("selftest");
                sb.Append("[OK] CoreSign.PESignCount len=").Append(sign == null ? -1 : sign.Length).Append("\r\n");
            }
            catch (Exception ex)
            {
                sb.Append("[WARN] CoreSign: ").Append(ex.Message).Append("\r\n");
            }

            // ---- 进程 / 端口自检 ----
            sb.Append("\r\n---- 进程与端口 ----\r\n");
            try
            {
                int zombies = 0, windows = 0;
                int me = Process.GetCurrentProcess().Id;
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.Id == me) continue;
                        if (!p.ProcessName.StartsWith("ModPcGUI", StringComparison.OrdinalIgnoreCase)
                            && !p.ProcessName.Equals("Login", StringComparison.OrdinalIgnoreCase)) continue;
                        bool hasWin = p.MainWindowHandle != IntPtr.Zero;
                        sb.Append("  ").Append(p.ProcessName).Append(" pid=").Append(p.Id)
                          .Append(hasWin ? " [有窗口]" : " [无窗口/残留]").Append("\r\n");
                        if (hasWin) windows++; else zombies++;
                    }
                    catch { }
                }
                sb.Append("[进程] 可见窗口=").Append(windows)
                  .Append("  残留=").Append(zombies).Append("\r\n");
            }
            catch (Exception ex)
            {
                sb.Append("[WARN] 进程检查: ").Append(ex.Message).Append("\r\n");
            }

            try
            {
                var ip = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
                bool busy = false;
                foreach (var ep in ip.GetActiveTcpListeners())
                {
                    if (ep.Port >= 11451 && ep.Port <= 11460)
                    {
                        sb.Append("[端口] ").Append(ep.Port).Append(" 被占用 ").Append(ep.Address).Append("\r\n");
                        busy = true;
                    }
                }
                if (!busy) sb.Append("[端口] 11451~11460 全空闲\r\n");
            }
            catch (Exception ex)
            {
                sb.Append("[WARN] 端口检查: ").Append(ex.Message).Append("\r\n");
            }

            // ---- 关键文件 ----
            sb.Append("\r\n---- 关键文件 ----\r\n");
            string bd = AppDomain.CurrentDomain.BaseDirectory;
            foreach (var f in new[] { "config.json", "steve.png", "skin.json",
                                      "steve_original.png", "ui.json", "mcl.common.dll", "Auth.Sign.dll" })
            {
                string fp = System.IO.Path.Combine(bd, f);
                sb.Append("  ").Append(f).Append(" : ")
                  .Append(System.IO.File.Exists(fp)
                      ? new System.IO.FileInfo(fp).Length + " B"
                      : "缺失").Append("\r\n");
            }
            try
            {
                string gp = (string)Newtonsoft.Json.Linq.JObject.Parse(
                    System.IO.File.ReadAllText(System.IO.Path.Combine(bd, "config.json")))["path"];
                string mc = System.IO.Path.Combine(gp ?? "", "mc.cfg");
                sb.Append("  游戏目录 mc.cfg : ")
                  .Append(System.IO.File.Exists(mc) ? new System.IO.FileInfo(mc).Length + " B" : "缺失")
                  .Append("\r\n");
            }
            catch { }

            sb.Append("\r\n== 自检结束 ==\r\n");
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.log"),
                sb.ToString(), System.Text.Encoding.UTF8);
        }
    }
}
