using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Wpf = System.Windows;
using WpfC = System.Windows.Controls;
using WinForms = System.Windows.Forms;
using ConsoleAppLogin.NetEase;

namespace ModPcGUI
{
    /// <summary>
    /// ES Tool 的 PCL UI 前端：窗口里的卡片、按钮、输入框、下拉框全部是
    /// PCL2（Plain Craft Launcher 2）程序集里的原生控件（MyCard / MyButton /
    /// MyComboBox / MyTextBox / MyListItem / MyIconButton），不是仿制。
    /// 业务逻辑仍走 ES Tool 原有的 AppHost / Term / 命令链路。
    /// </summary>
    public sealed partial class PclMainWindow : Wpf.Window
    {
        private readonly PCL.MyTextBox _logBox = new PCL.MyTextBox();
        private readonly PCL.MyTextBox _pathBox = new PCL.MyTextBox();
        private readonly PCL.MyTextBox _addrBox = new PCL.MyTextBox();
        private readonly PCL.MyComboBox _accBox = new PCL.MyComboBox();
        private readonly PCL.MyComboBox _modeBox = new PCL.MyComboBox();
        private readonly PCL.MyComboBox _typeBox = new PCL.MyComboBox();
        private readonly PCL.MyComboBox _roomKindBox = new PCL.MyComboBox();
        private readonly PCL.MyTextBox _roomIdBox = new PCL.MyTextBox();
        private readonly PCL.MyListItem _rbCustom = new PCL.MyListItem();
        private readonly PCL.MyListItem _rbLobby = new PCL.MyListItem();
        private readonly WpfC.TextBlock _status = new WpfC.TextBlock();
        private readonly WpfC.TextBlock _uid = new WpfC.TextBlock();

        // ---- 房间 IP 查询：状态反馈 + 候选房间列表 ----
        private readonly WpfC.TextBlock _roomStatusTb = new WpfC.TextBlock();
        private readonly WpfC.StackPanel _roomCandList = new WpfC.StackPanel();
        private WpfC.Border _roomCandBox;
        private PCL.MyButton _btnRoomIp;
        private readonly List<RoomCandidate> _roomCands = new List<RoomCandidate>();

        /// <summary>账号下拉的数据源（与 _accBox 按下标一一对应）</summary>
        private readonly List<AccItem> _accItems = new List<AccItem>();

        private sealed class AccItem
        {
            public string Path;
            public string Name;
            public override string ToString() { return Name; }
        }

        private void AppendLog(string s)
        {
            _logBox.AppendText(s);
            _logBox.ScrollToEnd();
            LogToFile(s);
        }

        /// <summary>
        /// 同步落盘 run.log。界面日志在「设置」页，用户在「启动」页操作时看不到任何反馈，
        /// 事后也无从查证 —— 登录/下载出问题时这份文件是唯一的现场。
        /// 超过 512 KB 自动重来，避免无限增长。
        /// </summary>
        private void LogToFile(string s)
        {
            try
            {
                if (string.IsNullOrEmpty(s)) return;
                string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "run.log");
                var fi = new FileInfo(f);
                if (fi.Exists && fi.Length > 512 * 1024) File.Delete(f);
                File.AppendAllText(f, "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s,
                    System.Text.Encoding.UTF8);
            }
            catch { }
        }

        // ---------- 业务逻辑（复用原 ES Tool） ----------
        private void RefreshAccounts()
        {
            _accBox.Items.Clear();
            _accItems.Clear();
            foreach (var f in AppHost.ListAccounts())
            {
                var it = new AccItem { Path = f, Name = Path.GetFileNameWithoutExtension(f) };
                _accItems.Add(it);
                _accBox.Items.Add(new PCL.MyComboBoxItem { Content = it.Name });
            }
            if (_accBox.Items.Count > 0) _accBox.SelectedIndex = 0;
            UpdateLaunchCaption();
        }

        private void RefreshState()
        {
            bool run = AppHost.Started;
            bool busy = AppHost.LoggingIn;
            string err = AppHost.LastError;

            _status.Text = run ? "●  已连接控制台" : (busy ? "●  正在登录…" : "●  未登录");
            _uid.Text = "UID: " + (string.IsNullOrEmpty(Http.UID) ? "-" : Http.UID)
                      + "    ·    命令 " + Commands.All.Count + " 条";
            try
            {
                if (_statePill != null)
                    _statePill.Text = run ? "已连接控制台 · 网易渠道"
                                    : (busy ? "正在登录 · 请稍候" : "已就绪 · 网易渠道");
            }
            catch { }
            // 启动页顶部提示：登录进度和失败原因必须出现在用户眼皮底下。
            // 之前这些信息只写进「设置」页的日志框，用户在启动页点登录就是「一片安静」。
            try
            {
                if (_pageHint != null)
                {
                    if (run) _pageHint.Text = "已登录，点「启动游戏」进入客户端";
                    else if (busy) _pageHint.Text = "正在登录，请稍候…（若弹出浏览器，请完成验证）";
                    else if (!string.IsNullOrEmpty(err)) _pageHint.Text = "上次登录失败：" + err;
                    else _pageHint.Text = "选择账号并登录，然后启动客户端";
                }
            }
            catch { }
            // 登录成功后隐藏「登录 / 添加账号」按钮行，把空间让给账号区
            try
            {
                if (_btnLoginRow != null)
                    _btnLoginRow.Visibility = run ? Wpf.Visibility.Collapsed : Wpf.Visibility.Visible;
            }
            catch { }
            // 「启动模式」只影响登录方式（auto/pc/pe），登录结束后没有意义，一并隐藏
            try
            {
                if (_modeRow != null)
                    _modeRow.Visibility = run ? Wpf.Visibility.Collapsed : Wpf.Visibility.Visible;
            }
            catch { }
        }

        private bool Ready()
        {
            if (!AppHost.Started)
            {
                Wpf.MessageBox.Show("请先登录，或点「离线模式」。", "ES Tool");
                return false;
            }
            return true;
        }

        private void DoLogin()
        {
            if (AppHost.Started) { AppendLog("已经登录过了\r\n"); return; }
            if (AppHost.LoggingIn) { AppendLog("正在登录中…\r\n"); return; }
            int i = _accBox.SelectedIndex;
            var item = (i >= 0 && i < _accItems.Count) ? _accItems[i] : null;
            if (item == null) { Wpf.MessageBox.Show("还没有账号，先点「添加账号」。", "ES Tool"); return; }
            string f = item.Path;

            // 账号文件先自检：坏文件（比如手工粘贴时内层引号没转义）会让登录静默失败，
            // 而错误信息只写进设置页的日志框 —— 表现就是「点了登录没反应」。这里直接说清楚。
            string bad = AppHost.CheckAccount(f);
            if (bad != null)
            {
                Wpf.MessageBox.Show(
                    "这个账号文件不能用：\n" + bad + "\n\n" + f
                    + "\n\n请删掉它重新「添加账号」，或修正 JSON 后再试。",
                    "ES Tool", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning);
                return;
            }

            string mode = "auto";
            if (_modeBox.SelectedIndex == 1) mode = "pc";
            else if (_modeBox.SelectedIndex == 2) mode = "pe";

            SaveUi();
            Term.ClearInput();
            AppendLog("› 开始登录：" + Path.GetFileNameWithoutExtension(f) + "\r\n");
            string file = f;
            AppHost.KickOff(() => AppHost.Login(file, mode), "login");
        }

        private void DoAddAccount()
        {
            using (var dlg = new AddAccountDialog())
            {
                if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                {
                    AppHost.AddAccount(dlg.AccountName, dlg.Cookie);
                    RefreshAccounts();
                }
            }
        }

        private void DoDeleteAccount()
        {
            int i = _accBox.SelectedIndex;
            var item = (i >= 0 && i < _accItems.Count) ? _accItems[i] : null;
            if (item == null) return;
            string f = item.Path;
            if (Wpf.MessageBox.Show("删除账号 " + item.Name + " ？",
                    "ES Tool", Wpf.MessageBoxButton.OKCancel) != Wpf.MessageBoxResult.OK) return;
            AppHost.DeleteAccount(f);
            RefreshAccounts();
        }

        private void BrowsePath()
        {
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.Description = "选择 x64_mc 目录";
                if (Directory.Exists(_pathBox.Text)) dlg.SelectedPath = _pathBox.Text;
                if (dlg.ShowDialog() == WinForms.DialogResult.OK) _pathBox.Text = dlg.SelectedPath;
            }
        }

        private void SavePath()
        {
            string p = _pathBox.Text.Trim();
            if (p.Length == 0 || !Directory.Exists(p))
            {
                Wpf.MessageBox.Show("目录不存在：" + p, "ES Tool");
                return;
            }
            if (!Ready()) return;
            Term.Submit("setpath");
            Term.Submit(p);
            SaveUi();
        }

        /// <summary>后台线程安全的日志输出（AppendLog 只能在 UI 线程调）</summary>
        private void LogUi(string s)
        {
            try { Dispatcher.BeginInvoke(new Action(() => AppendLog(s))); } catch { }
        }

        /// <summary>在 JSON 里递归深搜字符串字段（网易把地址塞在 entity/data 各层不定）</summary>
        private static string DeepStr(Newtonsoft.Json.Linq.JObject o, string key)
        {
            if (o == null) return "";
            foreach (var prop in o.Properties())
            {
                if (prop.Name == key && prop.Value != null &&
                    (prop.Value.Type == Newtonsoft.Json.Linq.JTokenType.String ||
                     prop.Value.Type == Newtonsoft.Json.Linq.JTokenType.Integer ||
                     prop.Value.Type == Newtonsoft.Json.Linq.JTokenType.Float))
                {
                    var s = prop.Value.ToString();
                    if (s.Length > 0) return s;
                }
                if (prop.Value is Newtonsoft.Json.Linq.JObject child)
                {
                    var r = DeepStr(child, key);
                    if (r.Length > 0) return r;
                }
                if (prop.Value is Newtonsoft.Json.Linq.JArray arr)
                {
                    foreach (var item in arr)
                        if (item is Newtonsoft.Json.Linq.JObject jo2)
                        {
                            var r = DeepStr(jo2, key);
                            if (r.Length > 0) return r;
                        }
                }
            }
            return "";
        }

        /// <summary>
        /// 房间号 → IP:端口。
        /// 流程：先同时搜 PC / PE 两个大厅 → 0 个报未找到，1 个直接查，多个列出来让用户选。
        /// 全程有状态反馈（获取中 / 获取成功 / 获取失败）。
        /// </summary>
        private void DoRoomIp()
        {
            if (!Ready()) return;
            string roomRef = _roomIdBox.Text.Trim();
            if (roomRef.Length == 0) { Wpf.MessageBox.Show("先输入房间号 / 租赁服号。", "ES Tool"); return; }
            string kind = _roomKindBox.SelectedIndex == 1 ? "rental"
                        : (_roomKindBox.SelectedIndex == 2 ? "realms" : "room");

            RenderCandidates(null);
            SetRoomStatus("获取中…正在搜索房间（" + kind + "）", "#4C8DFF");
            SetRoomBusy(true);

            // 租赁服 / 山头没有「同名房」问题，直接查
            if (kind != "room")
            {
                AppHost.KickOff(() =>
                {
                    try
                    {
                        string a = RoomIpQuery.Query(roomRef, kind, s => LogUi("  [房间IP] " + s + "\r\n"));
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            _addrBox.Text = a;
                            SetRoomBusy(false);
                            SetRoomStatus("获取成功：" + a, "#2E9E63");
                            AppendLog("  已自动填入服务器地址，点「启动游戏」进服\r\n");
                        }));
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            SetRoomBusy(false);
                            SetRoomStatus("获取失败：" + ex.Message, "#E05A5A");
                            LogUi("✗ " + ex.Message + "\r\n");
                        }));
                    }
                }, "roomip");
                return;
            }

            AppHost.KickOff(() =>
            {
                try
                {
                    var cands = RoomIpQuery.SearchAll(roomRef, s => LogUi("  [房间IP] " + s + "\r\n"));

                    if (cands.Count == 0)
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            SetRoomBusy(false);
                            SetRoomStatus("获取失败：两个大厅都没找到「" + roomRef + "」（可能已关闭或已改名）", "#E05A5A");
                            LogUi("✗ 没搜到房间 " + roomRef + "\r\n");
                        }));
                        return;
                    }

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        RenderCandidates(cands);

                        if (cands.Count == 1)
                        {
                            // 只有一个，直接查，不用再点一次
                            QueryCandidate(cands[0]);
                            return;
                        }

                        SetRoomBusy(false);
                        SetRoomStatus("找到 " + cands.Count + " 个房间，请在下方列表里选一个（双击或点「查这个」）",
                            "#4C8DFF");
                        AppendLog("  注意：同名房在 PC / PE 大厅是各自独立的，看清大厅再选\r\n");
                    }));
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        SetRoomBusy(false);
                        SetRoomStatus("获取失败：" + ex.Message, "#E05A5A");
                        LogUi("✗ " + ex.Message + "\r\n");
                    }));
                }
            }, "roomip-search");
        }

        /// <summary>查询指定候选房间，并把结果写回地址框。</summary>
        private void QueryCandidate(RoomCandidate c)
        {
            if (c == null) return;
            SetRoomBusy(true);
            SetRoomStatus("获取中…正在进「" + c.Name + "」（" + c.Lobby + " 大厅）取地址", "#4C8DFF");
            AppendLog("› 查询 " + c.Lobby + " 大厅的房间 " + c.Name + "（" + c.EntityId + "）\r\n");

            AppHost.KickOff(() =>
            {
                try
                {
                    string addr = RoomIpQuery.QueryCandidate(c, s => LogUi("  [房间IP] " + s + "\r\n"));
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _addrBox.Text = addr;
                        SetRoomBusy(false);
                        SetRoomStatus("获取成功：" + addr + "（" + c.Lobby + " 大厅）", "#2E9E63");
                        AppendLog("  ✓ " + addr + " 已填入服务器地址\r\n");
                    }));
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        SetRoomBusy(false);
                        SetRoomStatus("获取失败：" + ex.Message, "#E05A5A");
                        LogUi("✗ " + ex.Message + "\r\n");
                    }));
                }
            }, "roomip");
        }

        /// <summary>把候选房间渲染成可点的列表（大厅徽标 + 房主 + 地图 + 人数）。</summary>
        private void RenderCandidates(List<RoomCandidate> cands)
        {
            _roomCands.Clear();
            _roomCandList.Children.Clear();

            if (cands == null || cands.Count == 0)
            {
                _roomCandBox.Visibility = Wpf.Visibility.Collapsed;
                return;
            }

            foreach (var c in cands) _roomCands.Add(c);

            bool pe;
            foreach (var c in cands)
            {
                pe = c.Lobby == "PE";

                var grid = new WpfC.Grid();
                grid.ColumnDefinitions.Add(new WpfC.ColumnDefinition { Width = Wpf.GridLength.Auto });
                grid.ColumnDefinitions.Add(new WpfC.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new WpfC.ColumnDefinition { Width = Wpf.GridLength.Auto });

                // 大厅徽标
                var badge = new WpfC.Border
                {
                    Background = Brush(pe ? "#2F6F4F" : "#2B4A73"),
                    CornerRadius = new Wpf.CornerRadius(3),
                    Padding = new Wpf.Thickness(7, 3, 7, 3),
                    VerticalAlignment = Wpf.VerticalAlignment.Center,
                    Margin = new Wpf.Thickness(0, 0, 9, 0),
                    Child = new WpfC.TextBlock
                    {
                        Text = c.Lobby,
                        FontSize = 10.5,
                        FontWeight = Wpf.FontWeights.Bold,
                        Foreground = Brush("#EAF2FF")
                    }
                };
                WpfC.Grid.SetColumn(badge, 0);
                grid.Children.Add(badge);

                // 标题 + 详情
                var txt = new WpfC.StackPanel { VerticalAlignment = Wpf.VerticalAlignment.Center };
                txt.Children.Add(new WpfC.TextBlock
                {
                    Text = c.Title,
                    FontSize = 12,
                    FontWeight = Wpf.FontWeights.SemiBold,
                    Foreground = Brush("#E9ECEF")
                });
                txt.Children.Add(new WpfC.TextBlock
                {
                    Text = c.Detail,
                    FontSize = 10.5,
                    Margin = new Wpf.Thickness(0, 2, 0, 0),
                    Foreground = Brush("#8B929B"),
                    TextWrapping = Wpf.TextWrapping.Wrap
                });
                WpfC.Grid.SetColumn(txt, 1);
                grid.Children.Add(txt);

                var pick = new PCL.MyButton
                {
                    Text = "查这个",
                    Width = 68,
                    Height = 26,
                    ColorType = PCL.MyButton.ColorState.Highlight,
                    VerticalAlignment = Wpf.VerticalAlignment.Center,
                    Margin = new Wpf.Thickness(10, 0, 0, 0)
                };
                WpfC.TextBlock.SetFontSize(pick, 11.5);
                var captured = c;
                pick.Click += (s, e) => QueryCandidate(captured);
                WpfC.Grid.SetColumn(pick, 2);
                grid.Children.Add(pick);

                var row = new WpfC.Border
                {
                    Background = Brush("#1B1F23"),
                    CornerRadius = new Wpf.CornerRadius(3),
                    Padding = new Wpf.Thickness(8, 6, 8, 6),
                    Margin = new Wpf.Thickness(0, 3, 0, 0),
                    Cursor = Wpf.Input.Cursors.Hand,
                    Child = grid
                };
                row.MouseLeftButtonUp += (s, e) => QueryCandidate(captured);
                // PCL 式悬停：90ms 颜色过渡，不再是硬切（与 MyCard 的 AaColor 参数一致）
                PclAnim.HoverBackground(row, "#1B1F23", "#232A31");
                // 候选逐条上浮出现，步长 25ms（PCL AaStack 的默认间隔）
                PclAnim.SlideFadeIn(row, 8, PclAnim.Normal, _roomCandList.Children.Count * PclAnim.Step);

                _roomCandList.Children.Add(row);
            }

            _roomCandBox.Visibility = Wpf.Visibility.Visible;
        }

        /// <summary>状态行：获取中（蓝）/ 成功（绿）/ 失败（红）。</summary>
        private void SetRoomStatus(string text, string colorHex)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetRoomStatus(text, colorHex)));
                return;
            }
            _roomStatusTb.Text = text;
            _roomStatusTb.Foreground = Brush(colorHex);
            _roomStatusTb.Visibility = Wpf.Visibility.Visible;
        }

        private void SetRoomBusy(bool busy)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetRoomBusy(busy)));
                return;
            }
            if (_btnRoomIp != null) _btnRoomIp.IsEnabled = !busy;
        }

        private void DoLaunch()
        {
            if (!Ready()) return;
            string ip, port;
            if (!SplitAddr(_addrBox.Text, out ip, out port))
            {
                Wpf.MessageBox.Show("地址格式应为 127.0.0.1:1000", "ES Tool");
                return;
            }
            int mode = _typeBox.SelectedIndex == 1 ? 1000 : (_typeBox.SelectedIndex == 2 ? 1 : 100);
            SaveUi();
            AppendLog("› 启动游戏（" + ip + ":" + port + "，模式 " + mode + "）\r\n");
            Term.Submit("gametype");
            Term.Submit(mode.ToString(CultureInfo.InvariantCulture));
            Term.Submit(ip);
            Term.Submit(port);
        }

        private static bool SplitAddr(string raw, out string ip, out string port)
        {
            ip = port = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string s = raw.Trim();
            int i = s.LastIndexOf(':');
            if (i > 0 && i < s.Length - 1) { ip = s.Substring(0, i).Trim(); port = s.Substring(i + 1).Trim(); }
            else
            {
                var a = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (a.Length != 2) return false;
                ip = a[0]; port = a[1];
            }
            int p;
            if (!int.TryParse(port, out p) || p <= 0 || p > 65535) return false;
            if (ip.StartsWith("[") && ip.EndsWith("]")) ip = ip.Substring(1, ip.Length - 2);
            return true;
        }

        private void DoQuick(string cmd, string label)
        {
            if (!Ready()) return;
            AppendLog("› " + label + "\r\n");
            Term.Submit(cmd);
        }

        private void OpenFeatures()
        {
            using (var f = new FeaturesForm(new LogView()))
            {
                f.ShowDialog();
            }
        }

        // ---------- ui.json ----------
        private string UiPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui.json"); } }

        private void LoadUi()
        {
            try
            {
                if (!File.Exists(UiPath)) return;
                var o = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(UiPath));
                string p = (string)o["path"];
                string a = (string)o["addr"];
                if (!string.IsNullOrEmpty(p)) _pathBox.Text = p;
                if (!string.IsNullOrEmpty(a)) _addrBox.Text = a;
                if (o["mode"] != null) _typeBox.SelectedIndex = Math.Max(0, Math.Min(2, (int)o["mode"]));
            }
            catch { }
            if (string.IsNullOrEmpty(_pathBox.Text))
            {
                try { if (text.app.Temp != null) _pathBox.Text = text.app.Temp.path; } catch { }
            }
            UpdateAddrRow();
        }

        private void SaveUi()
        {
            try
            {
                var o = new Newtonsoft.Json.Linq.JObject();
                o["path"] = _pathBox.Text.Trim();
                o["addr"] = _addrBox.Text.Trim();
                o["mode"] = _typeBox.SelectedIndex;
                File.WriteAllText(UiPath, o.ToString(), new System.Text.UTF8Encoding(false));
            }
            catch { }
        }
    }
}
