using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using Wpf = System.Windows;
using WpfC = System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace ModPcGUI
{
    /// <summary>
    /// 「下载」页 —— 与 PCL NEMC 的 PageDownloadNemc 同一套功能：
    /// 下载路径 + 版本下拉 + 刷新 + 下载/取消 + 状态 + 进度条，
    /// 只是把 WPF 原生控件换成了 PCL2 的 MyTextBox / MyComboBox / MyButton / MyCard。
    /// </summary>
    public sealed partial class PclMainWindow
    {
        /// <summary>「下载」页在导航里的下标（NavNames：启动 / 下载 / 联机大厅 / 设置 / 更多）</summary>
        internal const int DownloadPageIndex = 1;

        private PCL.MyTextBox _dlPathBox;
        private PCL.MyComboBox _dlVerBox;
        private PCL.MyButton _dlBtn;
        private PCL.MyButton _dlCancelBtn;
        private WpfC.ProgressBar _dlBar;
        private WpfC.TextBlock _dlStatus;

        private readonly List<NemcVersion> _dlVersions = new List<NemcVersion>();
        private bool _dlBusy;
        private volatile bool _dlCancel;
        /// <summary>上一次自动填入的下载路径：用户没改过时，进入页面可以跟着配置重新算</summary>
        private string _dlPathAuto = "";

        // ==================== 页面 ====================

        private Wpf.FrameworkElement BuildDownloadPage(SolidColorBrush blue, SolidColorBrush text, SolidColorBrush sub)
        {
            var sv = new WpfC.ScrollViewer { VerticalScrollBarVisibility = WpfC.ScrollBarVisibility.Auto };
            var root = new WpfC.StackPanel { Margin = new Wpf.Thickness(20, 16, 20, 16) };
            sv.Content = root;

            root.Children.Add(new WpfC.TextBlock
            {
                Text = "下载并安装网易我的世界中国版客户端",
                FontSize = 11,
                Foreground = sub,
                Margin = new Wpf.Thickness(2, 0, 0, 12)
            });

            // ---- 卡片：网易客户端（PCL NEMC 下载页同款布局） ----
            var card = new PCL.MyCard { Title = "网易客户端", Margin = new Wpf.Thickness(0, 0, 0, 14) };
            var sp = new WpfC.StackPanel { Margin = new Wpf.Thickness(15, 35, 15, 15) };

            sp.Children.Add(Label("下载路径", sub));
            var pathRow = new WpfC.Grid { Margin = new Wpf.Thickness(0, 0, 0, 14) };
            pathRow.ColumnDefinitions.Add(new WpfC.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
            pathRow.ColumnDefinitions.Add(new WpfC.ColumnDefinition { Width = Wpf.GridLength.Auto });

            _dlPathBox = new PCL.MyTextBox
            {
                Height = 30,
                FontSize = 12,
                HintText = "客户端会安装到该目录下的版本子文件夹"
            };
            WpfC.Grid.SetColumn(_dlPathBox, 0);
            pathRow.Children.Add(_dlPathBox);

            var bBrowse = new PCL.MyButton { Text = "浏览", Width = 76, Height = 30, Margin = new Wpf.Thickness(10, 0, 0, 0) };
            bBrowse.Click += (s, e) => BrowseDownloadPath();
            WpfC.Grid.SetColumn(bBrowse, 1);
            pathRow.Children.Add(bBrowse);
            sp.Children.Add(pathRow);

            sp.Children.Add(Label("游戏版本", sub));
            var verRow = new WpfC.Grid { Margin = new Wpf.Thickness(0, 0, 0, 16) };
            verRow.ColumnDefinitions.Add(new WpfC.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
            verRow.ColumnDefinitions.Add(new WpfC.ColumnDefinition { Width = Wpf.GridLength.Auto });

            _dlVerBox = new PCL.MyComboBox { Height = 30 };
            _dlVerBox.SelectedIndex = -1;
            WpfC.Grid.SetColumn(_dlVerBox, 0);
            verRow.Children.Add(_dlVerBox);

            var bRefresh = new PCL.MyButton { Text = "刷新版本", Width = 96, Height = 30, Margin = new Wpf.Thickness(10, 0, 0, 0) };
            bRefresh.Click += (s, e) => LoadVersions(true);
            WpfC.Grid.SetColumn(bRefresh, 1);
            verRow.Children.Add(bRefresh);
            sp.Children.Add(verRow);

            var btnRow = new WpfC.StackPanel { Orientation = WpfC.Orientation.Horizontal, Margin = new Wpf.Thickness(0, 0, 0, 4) };
            _dlBtn = new PCL.MyButton
            {
                Text = "下载并安装",
                Width = 120,
                Height = 34,
                ColorType = PCL.MyButton.ColorState.Highlight
            };
            _dlBtn.Click += (s, e) => StartDownload();
            btnRow.Children.Add(_dlBtn);

            _dlCancelBtn = new PCL.MyButton
            {
                Text = "取消",
                Width = 88,
                Height = 34,
                IsEnabled = false,
                Margin = new Wpf.Thickness(10, 0, 0, 0)
            };
            _dlCancelBtn.Click += (s, e) => CancelDownload();
            btnRow.Children.Add(_dlCancelBtn);
            sp.Children.Add(btnRow);

            _dlStatus = new WpfC.TextBlock
            {
                Text = "未加载版本",
                FontSize = 11,
                Foreground = sub,
                TextTrimming = Wpf.TextTrimming.CharacterEllipsis,
                Margin = new Wpf.Thickness(2, 10, 0, 6)
            };
            sp.Children.Add(_dlStatus);

            _dlBar = new WpfC.ProgressBar
            {
                Height = 6,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Foreground = blue,
                Background = Brush("#E3E7EE"),
                BorderThickness = new Wpf.Thickness(0),
                Visibility = Wpf.Visibility.Collapsed
            };
            sp.Children.Add(_dlBar);

            var hint = new PCL.MyHint
            {
                Text = "安装包走网易官方 CDN，体积约 1–2 GB；下载完成后会自动把游戏路径指向该版本目录。\r\n"
                     + "版本列表需要登录后才能获取。",
                Theme = PCL.MyHint.Themes.Yellow,
                Margin = new Wpf.Thickness(0, 14, 0, 0)
            };
            sp.Children.Add(hint);

            card.Children.Add(sp);
            root.Children.Add(card);

            return sv;
        }

        // ==================== 交互 ====================

        /// <summary>切到下载页时补默认路径、按需拉一次版本列表（PCL 的 PageLoaded 同款时机）。</summary>
        private void EnsureDownloadPageReady()
        {
            try
            {
                if (_dlPathBox != null)
                {
                    // 页面构造时 _pathBox 还没从 ui.json 读出来，所以默认路径要在这里重算一次
                    string auto = DefaultDownloadPath();
                    string current = (_dlPathBox.Text ?? "").Trim();
                    if (current.Length == 0 || current == _dlPathAuto)
                    {
                        _dlPathBox.Text = auto;
                        _dlPathAuto = auto;
                    }
                }
                if (!_dlBusy && _dlVersions.Count == 0 && NemcDownload.HasLogin)
                    LoadVersions(false);
                else if (!_dlBusy && _dlVersions.Count == 0 && _dlStatus != null)
                    _dlStatus.Text = "未登录：请先在「启动」页登录，再点「刷新版本」";
            }
            catch { }
        }

        private string DefaultDownloadPath()
        {
            try
            {
                string configured = (_pathBox.Text ?? "").Trim();
                if (configured.Length > 0)
                {
                    string parent = Path.GetDirectoryName(configured.TrimEnd('\\', '/'));
                    if (!string.IsNullOrWhiteSpace(parent)) return parent;
                }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "NEMC");
        }

        private void BrowseDownloadPath()
        {
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.Description = "选择网易客户端下载目录";
                string current = (_dlPathBox.Text ?? "").Trim();
                if (Directory.Exists(current)) dlg.SelectedPath = current;
                if (dlg.ShowDialog() == WinForms.DialogResult.OK) _dlPathBox.Text = dlg.SelectedPath;
            }
        }

        private void LoadVersions(bool showHint)
        {
            if (_dlBusy) return;
            if (!NemcDownload.HasLogin)
            {
                _dlStatus.Text = "未登录：请先在「启动」页登录，再点「刷新版本」";
                if (showHint) Wpf.MessageBox.Show("请先在「启动」页登录后再获取版本列表。", "ES Tool");
                return;
            }

            _dlStatus.Text = "正在获取版本列表...";
            _dlBtn.IsEnabled = false;
            Task.Run(() =>
            {
                List<NemcVersion> list = null;
                string error = null;
                try { list = NemcDownload.GetVersions(); }
                catch (Exception ex) { error = ex.Message; }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (error != null)
                    {
                        _dlStatus.Text = "获取版本列表失败：" + error;
                        AppendLog("[下载] 获取版本列表失败：" + error + "\r\n");
                    }
                    else
                    {
                        _dlVersions.Clear();
                        _dlVersions.AddRange(list);
                        _dlVerBox.Items.Clear();
                        foreach (NemcVersion v in list)
                            _dlVerBox.Items.Add(new PCL.MyComboBoxItem { Content = v.Version });
                        if (list.Count > 0) _dlVerBox.SelectedIndex = 0;
                        _dlStatus.Text = "已获取 " + list.Count + " 个可下载版本";
                        if (showHint) AppendLog("已刷新版本列表：" + list.Count + " 个版本\r\n");
                    }
                    if (!_dlBusy) _dlBtn.IsEnabled = true;
                }));
            });
        }

        private void StartDownload()
        {
            if (_dlBusy) return;
            if (!NemcDownload.HasLogin)
            {
                Wpf.MessageBox.Show("请先在「启动」页登录后再下载客户端。", "ES Tool");
                return;
            }

            int idx = _dlVerBox.SelectedIndex;
            NemcVersion ver = (idx >= 0 && idx < _dlVersions.Count) ? _dlVersions[idx] : null;
            if (ver == null)
            {
                Wpf.MessageBox.Show("请先选择要下载的版本（可点「刷新版本」）。", "ES Tool");
                return;
            }

            string dir = (_dlPathBox.Text ?? "").Trim();
            if (dir.Length == 0)
            {
                Wpf.MessageBox.Show("请先选择下载路径。", "ES Tool");
                return;
            }

            _dlBusy = true;
            _dlCancel = false;
            UpdateDownloadUi(true);
            _dlBar.Value = 0;
            _dlBar.Visibility = Wpf.Visibility.Visible;
            _dlStatus.Text = "准备下载...";
            AppendLog("› 开始下载网易客户端 " + ver.Version + "\r\n");

            NemcVersion target = ver;
            string installDir = dir;
            Task.Run(() =>
            {
                try
                {
                    string gameDir = NemcDownload.DownloadAndInstall(target, installDir,
                        (pct, msg) => Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (pct >= 0) _dlBar.Value = Math.Max(0, Math.Min(100, pct));
                            _dlStatus.Text = msg;
                        })),
                        () => _dlCancel);

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _dlBar.Value = 100;
                        _dlStatus.Text = "安装完成：" + gameDir;

                        // PCL 同款收尾：把游戏目录设为启动路径
                        _pathBox.Text = gameDir;
                        SaveUi();
                        if (AppHost.Started)
                        {
                            try { Term.Submit("setpath"); Term.Submit(gameDir); } catch { }
                        }
                        AppendLog("✓ 客户端安装完成：" + gameDir + "\r\n");
                        AppendLog("  已把游戏路径切换到这个版本\r\n");
                    }));
                }
                catch (OperationCanceledException)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _dlBar.Value = 0;
                        _dlStatus.Text = "已取消下载";
                        AppendLog("已取消下载\r\n");
                    }));
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _dlStatus.Text = "下载失败：" + ex.Message;
                        AppendLog("[下载失败] " + ex.Message + "\r\n");
                    }));
                }
                finally
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _dlBusy = false;
                        UpdateDownloadUi(false);
                    }));
                }
            });
        }

        private void CancelDownload()
        {
            if (!_dlBusy) return;
            _dlCancel = true;
            _dlCancelBtn.IsEnabled = false;
            _dlStatus.Text = "正在取消...";
        }

        private void UpdateDownloadUi(bool busy)
        {
            try
            {
                _dlBtn.IsEnabled = !busy;
                _dlCancelBtn.IsEnabled = busy;
                _dlPathBox.IsEnabled = !busy;
                _dlVerBox.IsEnabled = !busy;
            }
            catch { }
        }
    }
}
