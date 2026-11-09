using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ConsoleAppLogin.NetEase;
using FeverToSauth;
using Login.NetEase;
using Login.NetEase.LoginotpList;
using MCStudio.Network.Http;
using Newtonsoft.Json;
using text;

namespace ModPcGUI
{
    /// <summary>把原程序的控制台流程封装成 GUI 可调用的宿主</summary>
    public static class AppHost
    {
        public static volatile bool Started;      // 已进入命令循环
        public static volatile bool LoggingIn;    // 正在登录
        public static string AccountName = "";
        public static string LastError = "";
        public static bool Initialized;

        public static event Action StateChanged;

        private static void Notify()
        {
            try { StateChanged?.Invoke(); } catch { }
        }

        /// <summary>首屏初始化：工作目录、原生库、config.json</summary>
        public static void Init()
        {
            if (Initialized) return;
            Initialized = true;

            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            // 运行时数据自愈：先保证 config.json 与 user\ 存在，再做任何依赖它们的事。
            // 官方发行包自带这两样，但从源码编译出来的 bin\Debug\net48 里没有 ——
            // 缺了会让 CoreNative.CanLaunch 拿到 null 配置直接 NRE，账号列表也永远是空的
            // （点「登录」只会弹「还没有账号」）。
            try { EnsureRuntimeData(); }
            catch (Exception ex) { Term.WriteLine("[init] 运行时数据自愈失败：" + ex.Message); }

            try
            {
                string fpd;
                CoreNative.CanLaunch("", "", out fpd);
            }
            catch (Exception ex)
            {
                Term.WriteLine("[init] CoreNative.CanLaunch 失败：" + ex.Message);
            }

            try
            {
                string cfg = app.SimpleRead("config.json");
                app.Temp = JsonConvert.DeserializeObject<Login.temp>(cfg);
                Term.WriteLine("[init] 已加载 config.json  版本 " + app.Temp.Version
                               + "  服务器 " + app.Temp.url);
            }
            catch (Exception ex)
            {
                Term.WriteLine("[init] config.json 读取失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 补齐跑起来必需、但发行包里才有的运行时数据。
        /// 只补「缺了就直接白屏 / 功能全废」的两样，不动已有文件：
        ///   user\       —— 账号 json 的存放目录，缺了账号下拉永远是空的
        ///   config.json —— 版本号 / 服务器地址 / 游戏路径，缺了 app.Temp 为 null
        /// 默认值取网易正式包的当前值，只作为兜底，用户改过就永不覆盖。
        /// </summary>
        private static void EnsureRuntimeData()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string userDir = Path.Combine(baseDir, "user");
            if (!Directory.Exists(userDir)) Directory.CreateDirectory(userDir);

            string cfgPath = Path.Combine(baseDir, "config.json");
            if (File.Exists(cfgPath)) return;

            var def = new Login.temp
            {
                Version = "1.21.130",
                url = "https://x19obtcore.nie.netease.com:8443",
                PE_url = "https://g79obtcore.nie.netease.com:8443",
                engineVersion = "3.4.5.272725",
                connect_log = true
            };
            File.WriteAllText(cfgPath, JsonConvert.SerializeObject(def));
            Term.WriteLine("[init] 未找到 config.json，已生成默认配置（服务器 " + def.url + "）\r\n");
        }

        public static string UserDir
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user"); }
        }

        public static List<string> ListAccounts()
        {
            var list = new List<string>();
            try
            {
                string dir = UserDir;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                foreach (var f in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
                    list.Add(f);
            }
            catch { }
            return list;
        }

        /// <summary>
        /// 账号文件自检。返回 null 表示看起来正常，否则返回人话描述的毛病。
        /// 典型坏法：手工粘贴 sauth 时内层引号没转义，外层 JSON 直接不成立 ——
        /// 这种文件选中后点登录会一路静默失败（错误只写进设置页的日志框），
        /// 表现就是「登录按钮点了没反应」，所以要在点之前就拦住。
        /// </summary>
        public static string CheckAccount(string path)
        {
            try
            {
                if (!File.Exists(path)) return "文件不存在";
                string raw = File.ReadAllText(path).Trim();
                if (raw.Length == 0) return "文件是空的";
                if (raw.StartsWith("ey") || raw.StartsWith("SDK4399")) return null;  // 纯 sauth JWT，无需解析
                Newtonsoft.Json.Linq.JObject.Parse(raw);
                return null;
            }
            catch (Exception ex)
            {
                return "不是合法 JSON（" + ex.Message + "）";
            }
        }

        public static void AddAccount(string name, string cookie)
        {
            string dir = UserDir;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string safe = string.IsNullOrWhiteSpace(name) ? ("账号" + DateTime.Now.ToString("MMddHHmmss")) : name;
            foreach (char c in Path.GetInvalidFileNameChars())
                safe = safe.Replace(c, '_');
            string path = Path.Combine(dir, safe + ".json");
            File.WriteAllText(path, cookie ?? "", System.Text.Encoding.UTF8);
            Term.WriteLine("[账号] 已保存：" + path);
        }

        public static void DeleteAccount(string path)
        {
            try { File.Delete(path); Term.WriteLine("[账号] 已删除：" + Path.GetFileName(path)); }
            catch (Exception ex) { Term.WriteLine("[账号] 删除失败：" + ex.Message); }
        }

        /// <summary>登录并在后台进入命令循环（阻塞式，需在线程中调用）</summary>
        public static void Login(string accountFile, string mode)
        {
            if (LoggingIn || Started) return;
            LoggingIn = true;
            LastError = "";
            Notify();

            try
            {
                string content = app.ReadJson(accountFile);
                if (string.IsNullOrEmpty(content))
                    throw new Exception("账号文件为空：" + accountFile);

                bool pe;
                if (mode == "pe") pe = true;
                else if (mode == "pc") pe = false;
                else pe = content.TrimStart().StartsWith("ey") || content.StartsWith("SDK4399");

                string otp = content;
                if (content.StartsWith("ey"))
                {
                    Term.WriteLine("[账号] 检测到手机版 sauth，正在转换…");
                    otp = FeverAuth.FeverToSauth(content);
                }

                AccountName = Path.GetFileNameWithoutExtension(accountFile);
                Term.WriteLine("正在登录中，请稍等…  (" + AccountName + (pe ? " / 手机版" : " / 电脑版") + ")");

                app.PE_Login = pe;
                if (pe || app.Temp.is_g79) Http.PE_Login(otp);
                else Http.Login(otp);

                if (app.Temp != null && app.Temp.InitializeName)
                {
                    Term.WriteLine("[账号] 检测到需要初始化昵称…");
                    // 图形界面里没有控制台可敲昵称：挂个弹窗钩子。
                    // --legacy / 纯控制台模式 Application.Current 为空，保持 null，仍走 Term.ReadLine。
                    if (System.Windows.Application.Current != null)
                        text.app.NamePrompt = SetNameDialog.Ask;
                    try
                    {
                        app.Name();
                    }
                    finally
                    {
                        text.app.NamePrompt = null;
                    }
                }

                Function.GetName(app.player_info);
                Term.WriteLine("你好！冒险家：" + app.displayName);
                Term.WriteLine("输入 help 查看全部命令，或点击左侧命令直接执行");

                Started = true;
                LoggingIn = false;
                Notify();
                app.Command();
            }
            catch (ModPcFatalException ex)
            {
                LastError = ex.Message;
                Term.WriteLine("✗ " + ex.Message);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Term.WriteLine("✗ 登录异常：" + ex.Message);
                Term.WriteLine(ex.StackTrace);
            }
            finally
            {
                LoggingIn = false;
                Notify();
            }
        }

        /// <summary>离线进入命令循环（等同原程序输入 of）</summary>
        public static void Offline()
        {
            if (Started) return;
            try
            {
                Term.WriteLine("[模式] 离线控制台：未登录，仅本地功能可用");
                Started = true;
                Notify();
                app.Command();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Term.WriteLine("✗ " + ex.Message);
            }
            finally
            {
                Started = false;
                Notify();
            }
        }

        public static void KickOff(Action body, string threadName)
        {
            var t = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex)
                {
                    Term.WriteLine("✗ 线程异常：" + ex.Message);
                }
            });
            t.IsBackground = true;
            t.Name = threadName;
            t.Start();
        }
    }
}
