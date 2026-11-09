using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ModPcGUI
{
    /// <summary>网易客户端（NEMC）的一个可下载版本。</summary>
    internal sealed class NemcVersion
    {
        public string Version;
        public string PackageUrl;
        public string ManifestUrl;
        public bool IsStandard;

        public override string ToString() { return Version ?? ""; }
    }

    /// <summary>
    /// 网易我的世界中国版客户端（Minecraft.Windows.exe）的版本列表与下载安装。
    /// 协议、接口、目录结构与 PCL NEMC 的 ModNemc 完全一致：
    /// 版本列表走 x19 的 get-for-mcstudio 设置接口（user-id / user-token 鉴权），
    /// 安装包从 patchlist 里的包地址下载，安装完后把游戏目录设为启动路径。
    /// 鉴权头用 x19Crypt.ComputeDynamicToken，与 ES Tool 其它 x19 请求同源。
    /// </summary>
    internal static class NemcDownload
    {
        public const string GameExe = "Minecraft.Windows.exe";

        // ---- 与 PCL NEMC ModNemc 一致的接口常量 ----
        private const string ClientHost = "https://x19mclobt.nie.netease.com";
        private const string ListPath = "/interconn/web/pack-setting/get-for-mcstudio";
        private const string ListBody = "{\"setting_name\":\"pc_sdkclient_full_patchlist\"}";
        private const string ManifestSuffix = "/patch.json";

        // ---- 下载参数 ----
        private const long SegmentMin = 2L * 1024 * 1024;      // 单段最小 2 MB
        private const int SegmentMax = 32;                     // 最多 32 段（网易 CDN 按连接限速）
        private const int SegRetryMax = 4;
        private const int SegBuffer = 256 * 1024;
        private const int StreamBuffer = 1024 * 1024;
        private const long SafetyBuffer = 512L * 1024 * 1024;  // 磁盘额外余量
        private const long UnknownFallback = 4L * 1024 * 1024 * 1024;

        /// <summary>当前是否已有可用的 x19 登录态（entity_id + user token）。</summary>
        public static bool HasLogin
        {
            get
            {
                try
                {
                    return !string.IsNullOrWhiteSpace(ConsoleAppLogin.NetEase.Http.UID)
                        && !string.IsNullOrWhiteSpace(ConsoleAppLogin.NetEase.Http.LoginSRCToken);
                }
                catch { return false; }
            }
        }

        // ==================== 版本列表 ====================

        /// <summary>拉取可下载版本列表：标准版本按版本号降序在前，其余按名称排在后面。</summary>
        public static List<NemcVersion> GetVersions()
        {
            if (!HasLogin) throw new Exception("请先在「启动」页完成登录（未登录拿不到版本列表）");

            string token = Mark.x19Crypt.ComputeDynamicToken(
                ListPath, ListBody, ConsoleAppLogin.NetEase.Http.LoginSRCToken);
            string resp = PostJson(ListPath, ListBody, ConsoleAppLogin.NetEase.Http.UID, token);
            if (string.IsNullOrWhiteSpace(resp)) throw new Exception("版本列表响应为空");

            JObject root = JObject.Parse(resp);
            JToken code = root["code"];
            if (code == null || code.ToString() != "0")
            {
                string msg = root["message"] == null ? "" : root["message"].ToString();
                throw new Exception(msg == "" ? "获取游戏版本列表失败" : "获取游戏版本列表失败：" + msg);
            }

            JObject entity = root["entity"] as JObject;
            if (entity == null) throw new Exception("版本列表缺少 entity 数据");
            JToken settingValue = entity["setting_value"];
            if (settingValue == null) throw new Exception("版本列表缺少 setting_value 数据");

            JObject listRoot;
            if (settingValue.Type == JTokenType.Object) listRoot = (JObject)settingValue;
            else if (settingValue.Type == JTokenType.String) listRoot = JObject.Parse((string)settingValue);
            else throw new Exception("版本列表 setting_value 数据无效");

            List<NemcVersion> versions = ParseVersionList(listRoot);
            if (versions.Count == 0) throw new Exception("未获取到可下载的游戏版本");
            return versions;
        }

        private static string PostJson(string path, string body, string uid, string userToken)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.ExpectContinue = false;
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "WPFLauncher/0.0.0.0");
                using (var req = new HttpRequestMessage(HttpMethod.Post, ClientHost + path))
                {
                    req.Content = new StringContent(body, Encoding.UTF8);
                    req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    req.Headers.TryAddWithoutValidation("user-id", uid);
                    req.Headers.TryAddWithoutValidation("user-token", userToken);
                    using (HttpResponseMessage resp = client.SendAsync(req).GetAwaiter().GetResult())
                    {
                        string text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        if (!resp.IsSuccessStatusCode)
                            throw new Exception("版本列表请求失败：HTTP " + (int)resp.StatusCode);
                        return text;
                    }
                }
            }
        }

        private static List<NemcVersion> ParseVersionList(JObject root)
        {
            var dict = new Dictionary<string, NemcVersion>(StringComparer.OrdinalIgnoreCase);

            foreach (JProperty prop in root.Properties())
            {
                if (IsReservedKey(prop.Name)) continue;
                NemcVersion info = TryCreateVersion(prop.Name, prop.Value);
                if (info != null) dict[prop.Name] = info;
            }

            JToken entities = root["entities"];
            if (entities != null && entities.Type == JTokenType.Object)
            {
                foreach (JProperty prop in ((JObject)entities).Properties())
                {
                    NemcVersion info = TryCreateVersion(prop.Name, prop.Value);
                    if (info != null) dict[prop.Name] = info;
                }
            }

            List<NemcVersion> standard = dict.Values.Where(v => v.IsStandard).ToList();
            standard.Sort((a, b) => CompareVersions(b.Version, a.Version));
            List<NemcVersion> special = dict.Values.Where(v => !v.IsStandard)
                .OrderBy(v => v.Version, StringComparer.Ordinal).ToList();
            standard.AddRange(special);
            return standard;
        }

        private static NemcVersion TryCreateVersion(string version, JToken element)
        {
            if (string.IsNullOrWhiteSpace(version)) return null;
            string url = ExtractUrl(element);
            if (!IsHttpUrl(url)) return null;

            string packageUrl = url;
            string manifestUrl = "";
            if (packageUrl.EndsWith(ManifestSuffix, StringComparison.OrdinalIgnoreCase))
            {
                manifestUrl = packageUrl;
                packageUrl = packageUrl.Substring(0, packageUrl.Length - ManifestSuffix.Length) + ".zip";
            }
            return new NemcVersion
            {
                Version = version,
                PackageUrl = packageUrl,
                ManifestUrl = manifestUrl,
                IsStandard = IsStandardVersion(version)
            };
        }

        private static string ExtractUrl(JToken element)
        {
            if (element == null) return null;
            if (element.Type == JTokenType.String)
            {
                string s = (string)element;
                if (IsHttpUrl(s)) return s;
            }
            if (element.Type != JTokenType.Object) return null;
            foreach (JProperty prop in ((JObject)element).Properties())
            {
                string n = prop.Name.ToLowerInvariant();
                if ((n == "url" || n == "download_url" || n == "package_url") && prop.Value.Type == JTokenType.String)
                {
                    string s = (string)prop.Value;
                    if (IsHttpUrl(s)) return s;
                }
            }
            return null;
        }

        private static bool IsReservedKey(string key)
        {
            return key.Equals("entities", StringComparison.OrdinalIgnoreCase)
                || key.Equals("beta", StringComparison.OrdinalIgnoreCase)
                || key.Equals("stable", StringComparison.OrdinalIgnoreCase)
                || key.Equals("test", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStandardVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return false;
            foreach (string part in version.Split('.'))
            {
                int v;
                if (!int.TryParse(part, out v)) return false;
            }
            return true;
        }

        private static int CompareVersions(string left, string right)
        {
            string[] lp = left.Split('.');
            string[] rp = right.Split('.');
            int len = Math.Max(lp.Length, rp.Length);
            for (int i = 0; i < len; i++)
            {
                int lv = 0, rv = 0;
                if (i < lp.Length) int.TryParse(lp[i], out lv);
                if (i < rp.Length) int.TryParse(rp[i], out rv);
                if (lv != rv) return lv.CompareTo(rv);
            }
            return 0;
        }

        private static bool IsHttpUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u)
                && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
        }

        // ==================== 下载 + 安装 ====================

        /// <summary>
        /// 下载并安装指定版本，返回游戏目录（Minecraft.Windows.exe 所在目录）。
        /// progress(百分比，-1 表示不确定进度，状态文本)；isCancelled 返回 true 时抛 OperationCanceledException。
        /// </summary>
        public static string DownloadAndInstall(NemcVersion version, string installDir,
            Action<double, string> progress, Func<bool> isCancelled)
        {
            if (version == null) throw new Exception("未选择版本");
            if (string.IsNullOrWhiteSpace(version.PackageUrl)) throw new Exception("选中的版本缺少安装包地址");
            if (string.IsNullOrWhiteSpace(installDir)) throw new Exception("下载路径不能为空");
            if (!HasLogin) throw new Exception("请先完成登录后再下载游戏");

            string fullDir = Path.GetFullPath(installDir);
            string versionDir = Path.Combine(fullDir, SanitizeFileName(version.Version));
            if (Directory.Exists(Lp(versionDir)) &&
                Directory.EnumerateFileSystemEntries(Lp(versionDir)).Any())
            {
                throw new Exception("版本目录已存在且不是空目录：" + versionDir);
            }

            Report(progress, -1, "正在获取安装包元数据...");
            long? size;
            string hash, hashAlgo;
            ResolveMetadata(version, out size, out hash, out hashAlgo);

            Report(progress, -1, size.HasValue
                ? "安装包约 " + FormatBytes(size.Value) + "，正在检查磁盘空间..."
                : "未获取到安装包大小，按 " + FormatBytes(UnknownFallback) + " 预留空间检查...");
            EnsureDiskSpace(fullDir, size);

            Directory.CreateDirectory(Lp(fullDir));
            // 暂存目录名要短：安装包里有 80+ 字符的顶层目录，路径很容易顶到 260
            string stagingDir = Path.Combine(fullDir, ".nemc-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(Lp(stagingDir));
            string archivePath = Path.Combine(fullDir, "minecraft-" + SanitizeFileName(version.Version) + ".zip");

            try
            {
                Report(progress, 0, "正在连接下载服务器...");
                DownloadPackage(version.PackageUrl, archivePath, size, progress, isCancelled);

                if (!string.IsNullOrEmpty(hash) && !string.IsNullOrEmpty(hashAlgo))
                {
                    Report(progress, -1, "正在校验安装包（" + hashAlgo.ToUpperInvariant() + "）...");
                    VerifyHash(archivePath, hashAlgo, hash);
                    Report(progress, 100, "安装包校验通过");
                }
                else
                {
                    Report(progress, 100, "未获取到校验信息，已跳过安装包校验");
                }

                ThrowIfCancelled(isCancelled);
                Report(progress, 0, "正在解压游戏文件...");
                ExtractArchive(archivePath, stagingDir, progress, isCancelled);

                Report(progress, -1, "正在整理版本目录...");
                string gameDir = NormalizeInstalledDir(stagingDir, versionDir);
                Report(progress, 100, "安装完成：" + version.Version);
                return gameDir;
            }
            finally
            {
                TryDeleteFile(archivePath);
                TryDeleteDir(stagingDir);
            }
        }

        // ---------- 元数据 ----------

        private sealed class MetaCandidate
        {
            public string Url = "";
            public long? Size;
            public string Hash = "";
            public string Algo = "";
            public bool UrlMatch;
            public int Score
            {
                get { return (UrlMatch ? 4 : 0) + (Hash != "" && Algo != "" ? 2 : 0) + (Size.HasValue ? 1 : 0); }
            }
        }

        private static void ResolveMetadata(NemcVersion version, out long? size, out string hash, out string algo)
        {
            size = null; hash = ""; algo = "";

            if (!string.IsNullOrEmpty(version.ManifestUrl))
            {
                try
                {
                    using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                    {
                        string content = client.GetStringAsync(version.ManifestUrl).GetAwaiter().GetResult();
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            var candidates = new List<MetaCandidate>();
                            CollectManifestCandidates(JToken.Parse(content), version.PackageUrl, candidates);
                            MetaCandidate best = candidates.OrderByDescending(c => c.Score).FirstOrDefault();
                            if (best != null)
                            {
                                size = best.Size;
                                if (best.Hash != "" && best.Algo != "") { hash = best.Hash; algo = best.Algo; }
                            }
                        }
                    }
                }
                catch { }
            }

            if (!size.HasValue)
            {
                try
                {
                    using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                    using (var req = new HttpRequestMessage(HttpMethod.Head, version.PackageUrl))
                    using (HttpResponseMessage resp = client.SendAsync(req).GetAwaiter().GetResult())
                    {
                        if (resp.IsSuccessStatusCode) size = resp.Content.Headers.ContentLength;
                    }
                }
                catch { }
            }
        }

        private static void CollectManifestCandidates(JToken element, string packageUrl, List<MetaCandidate> list)
        {
            if (element == null) return;
            if (element.Type == JTokenType.Object)
            {
                var c = new MetaCandidate();
                foreach (JProperty prop in ((JObject)element).Properties())
                {
                    string n = prop.Name.ToLowerInvariant();
                    JToken v = prop.Value;
                    if ((n == "url" || n == "download_url" || n == "package_url")
                        && v.Type == JTokenType.String && IsHttpUrl((string)v))
                    {
                        c.Url = (string)v;
                    }
                    else if (n == "size" || n == "package_size" || n == "download_size")
                    {
                        long l;
                        if (TryGetLong(v, out l)) c.Size = l;
                    }
                    else if (n == "md5" && v.Type == JTokenType.String && ((string)v).Trim() != "")
                    {
                        c.Hash = ((string)v).Trim(); c.Algo = "md5";
                    }
                    else if ((n == "sha1" || n == "sha_1") && v.Type == JTokenType.String && ((string)v).Trim() != "")
                    {
                        c.Hash = ((string)v).Trim(); c.Algo = "sha1";
                    }
                    else if ((n == "hash" || n == "checksum") && v.Type == JTokenType.String && ((string)v).Trim() != "")
                    {
                        c.Hash = ((string)v).Trim();
                        if (c.Algo == "") c.Algo = DetectAlgo(c.Hash);
                    }
                    else if ((n == "hash_type" || n == "hash_algorithm" || n == "algorithm")
                        && v.Type == JTokenType.String && ((string)v).Trim() != "")
                    {
                        c.Algo = NormalizeAlgo((string)v);
                    }
                }
                if (c.Size.HasValue || (c.Hash != "" && c.Algo != ""))
                {
                    c.UrlMatch = string.Equals(c.Url, packageUrl, StringComparison.OrdinalIgnoreCase);
                    list.Add(c);
                }
                foreach (JProperty prop in ((JObject)element).Properties())
                    CollectManifestCandidates(prop.Value, packageUrl, list);
            }
            else if (element.Type == JTokenType.Array)
            {
                foreach (JToken item in (JArray)element)
                    CollectManifestCandidates(item, packageUrl, list);
            }
        }

        private static bool TryGetLong(JToken token, out long value)
        {
            value = 0;
            if (token == null) return false;
            if (token.Type == JTokenType.Integer) { value = (long)token; return true; }
            if (token.Type == JTokenType.Float) { value = (long)(double)token; return true; }
            if (token.Type == JTokenType.String) return long.TryParse(((string)token).Trim(), out value);
            return false;
        }

        private static string DetectAlgo(string hash)
        {
            string h = (hash ?? "").Trim();
            if (h.Length == 32) return "md5";
            if (h.Length == 40) return "sha1";
            if (h.Length == 64) return "sha256";
            return "";
        }

        private static string NormalizeAlgo(string algo)
        {
            string a = (algo ?? "").Trim().ToLowerInvariant().Replace("-", "").Replace("_", "");
            if (a == "sha1") return "sha1";
            if (a == "sha256") return "sha256";
            if (a == "md5") return "md5";
            return a;
        }

        private static void EnsureDiskSpace(string dir, long? size)
        {
            try
            {
                long need = (size ?? UnknownFallback) + SafetyBuffer;
                string root = Path.GetPathRoot(Path.GetFullPath(dir));
                if (string.IsNullOrEmpty(root)) return;
                var drive = new DriveInfo(root);
                if (drive.IsReady && drive.AvailableFreeSpace < need)
                {
                    throw new Exception("磁盘剩余空间不足：需要约 " + FormatBytes(need)
                        + "，当前可用 " + FormatBytes(drive.AvailableFreeSpace));
                }
            }
            catch (Exception ex)
            {
                // 只把「空间不足」当致命错误，其它（如网络盘）忽略
                if (ex.Message.StartsWith("磁盘剩余空间不足")) throw;
            }
        }

        // ---------- 下载 ----------

        private static void DownloadPackage(string url, string path, long? expected,
            Action<double, string> progress, Func<bool> isCancelled)
        {
            ThrowIfCancelled(isCancelled);

            long total = expected ?? 0;
            bool rangeOk = false;
            try { Probe(url, ref total, ref rangeOk); } catch { rangeOk = false; }
            if (total <= 0) throw new Exception("无法获取安装包大小，已中止下载");

            int segments = rangeOk ? ChooseSegments(total) : 1;
            if (segments <= 1)
            {
                DownloadSingle(url, path, total, progress, isCancelled, rangeOk);
                return;
            }

            try
            {
                DownloadSegmented(url, path, total, segments, progress, isCancelled);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // 分段失败退回单连接，避免整次下载白跑
                Report(progress, 0, "分段下载失败，改用单连接重试（" + ex.Message + "）");
                TryDeleteFile(path);
                DownloadSingle(url, path, total, progress, isCancelled, rangeOk);
            }
        }

        /// <summary>探测是否支持 Range 与真实大小。</summary>
        private static void Probe(string url, ref long total, ref bool rangeOk)
        {
            using (var client = NewDownloadClient())
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                req.Headers.Range = new RangeHeaderValue(0, 0);
                using (HttpResponseMessage resp = client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead)
                    .GetAwaiter().GetResult())
                {
                    if ((int)resp.StatusCode == 206)
                    {
                        rangeOk = true;
                        ContentRangeHeaderValue cr = resp.Content.Headers.ContentRange;
                        if (cr != null && cr.Length.HasValue) total = cr.Length.Value;
                    }
                    else if (resp.IsSuccessStatusCode && resp.Content.Headers.ContentLength.HasValue)
                    {
                        total = resp.Content.Headers.ContentLength.Value;
                    }
                }
            }
        }

        private static int ChooseSegments(long total)
        {
            if (total < 8L * 1024 * 1024) return 1;
            int n = (int)(total / SegmentMin);
            if (n < 2) n = 2;
            if (n > SegmentMax) n = SegmentMax;
            return n;
        }

        private static void DownloadSingle(string url, string path, long total,
            Action<double, string> progress, Func<bool> isCancelled, bool rangeOk)
        {
            using (var client = NewDownloadClient())
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            using (HttpResponseMessage resp = client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead)
                .GetAwaiter().GetResult())
            {
                if (!resp.IsSuccessStatusCode) throw new Exception("下载请求失败：HTTP " + (int)resp.StatusCode);
                if (resp.Content.Headers.ContentLength.HasValue && resp.Content.Headers.ContentLength.Value > 0)
                    total = resp.Content.Headers.ContentLength.Value;

                using (var input = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                using (var output = new FileStream(Lp(path), FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[StreamBuffer];
                    long done = 0;
                    var sw = Stopwatch.StartNew();
                    long lastMs = 0;
                    while (true)
                    {
                        ThrowIfCancelled(isCancelled);
                        int read = input.Read(buffer, 0, buffer.Length);
                        if (read <= 0) break;
                        output.Write(buffer, 0, read);
                        done += read;
                        if (sw.ElapsedMilliseconds - lastMs > 200 || done >= total)
                        {
                            lastMs = sw.ElapsedMilliseconds;
                            Report(progress, total > 0 ? done * 100.0 / total : -1,
                                BuildStatus(done, total, sw.Elapsed));
                        }
                    }
                    if (total > 0 && done < total)
                        throw new Exception("下载不完整：" + FormatBytes(done) + " / " + FormatBytes(total));
                }
            }
        }

        private static void DownloadSegmented(string url, string path, long total, int segments,
            Action<double, string> progress, Func<bool> isCancelled)
        {
            try
            {
                using (var fs = new FileStream(Lp(path), FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                    fs.SetLength(total);
            }
            catch { }

            long done = 0;
            var sw = Stopwatch.StartNew();
            long lastMs = 0;
            object gate = new object();

            Action<long> onBytes = n =>
            {
                long now = Interlocked.Add(ref done, n);
                lock (gate)
                {
                    if (sw.ElapsedMilliseconds - lastMs < 200 && now < total) return;
                    lastMs = sw.ElapsedMilliseconds;
                }
                Report(progress, now * 100.0 / total, BuildStatus(now, total, sw.Elapsed));
            };

            var tasks = new List<Task>();
            for (int i = 0; i < segments; i++)
            {
                long start = total * i / segments;
                long end = total * (i + 1) / segments - 1;
                if (end < start) continue;
                tasks.Add(Task.Run(() => DownloadSegment(url, path, start, end, onBytes, isCancelled)));
            }
            try { Task.WaitAll(tasks.ToArray()); }
            catch (AggregateException ex)
            {
                Exception inner = ex.Flatten().InnerExceptions.FirstOrDefault();
                if (inner is OperationCanceledException) throw new OperationCanceledException("已取消下载");
                throw inner ?? ex;
            }

            var fi = new FileInfo(Lp(path));
            if (fi.Length != total) throw new Exception("分段下载长度异常：" + FormatBytes(fi.Length));
        }

        private static void DownloadSegment(string url, string path, long start, long end,
            Action<long> onBytes, Func<bool> isCancelled)
        {
            long pos = start;
            Exception last = null;
            for (int attempt = 0; attempt <= SegRetryMax; attempt++)
            {
                ThrowIfCancelled(isCancelled);
                try
                {
                    using (var client = NewDownloadClient())
                    using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        req.Headers.Range = new RangeHeaderValue(pos, end);
                        using (HttpResponseMessage resp = client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead)
                            .GetAwaiter().GetResult())
                        {
                            if ((int)resp.StatusCode != 206)
                                throw new Exception("服务器未按分段返回（HTTP " + (int)resp.StatusCode + "）");
                            using (var input = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                            using (var output = new FileStream(Lp(path), FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                            {
                                output.Seek(pos, SeekOrigin.Begin);
                                byte[] buffer = new byte[SegBuffer];
                                while (pos <= end)
                                {
                                    ThrowIfCancelled(isCancelled);
                                    int want = (int)Math.Min(buffer.Length, end - pos + 1);
                                    int read = input.Read(buffer, 0, want);
                                    if (read <= 0) break;
                                    output.Write(buffer, 0, read);
                                    pos += read;
                                    onBytes(read);
                                }
                            }
                        }
                    }
                    if (pos > end) return;
                    last = new Exception("连接提前结束");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    last = ex;
                    try { Thread.Sleep(300 * (attempt + 1)); } catch { }
                }
            }
            throw last ?? new Exception("分段下载失败");
        }

        private static HttpClient NewDownloadClient()
        {
            var client = new HttpClient();
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "WPFLauncher/0.0.0.0");
            return client;
        }

        private static string BuildStatus(long done, long total, TimeSpan elapsed)
        {
            double speed = elapsed.TotalSeconds > 0.2 ? done / elapsed.TotalSeconds : 0;
            string pct = total > 0 ? (done * 100.0 / total).ToString("0.0") + "%" : "";
            string left = total > 0 ? FormatBytes(done) + " / " + FormatBytes(total) : FormatBytes(done);
            string sp = speed > 0 ? "  ·  " + FormatBytes((long)speed) + "/s" : "";
            return "正在下载 " + pct + "  ·  " + left + sp;
        }

        // ---------- 校验 ----------

        private static void VerifyHash(string path, string algo, string expected)
        {
            string actual;
            using (var stream = new FileStream(Lp(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                HashAlgorithm ha;
                string a = (algo ?? "").ToLowerInvariant();
                if (a == "md5") ha = MD5.Create();
                else if (a == "sha1") ha = SHA1.Create();
                else if (a == "sha256") ha = SHA256.Create();
                else return;  // 未知算法：跳过校验，不误报失败
                using (ha)
                {
                    actual = BitConverter.ToString(ha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                }
            }
            if (!string.Equals(actual, (expected ?? "").Trim().ToLowerInvariant(), StringComparison.Ordinal))
                throw new Exception("安装包校验失败（" + algo + " 不匹配），已中止安装");
        }

        // ---------- 解压 ----------

        private static void ExtractArchive(string archivePath, string destDir,
            Action<double, string> progress, Func<bool> isCancelled)
        {
            using (var fs = new FileStream(Lp(archivePath), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                long totalBytes = 0;
                foreach (ZipArchiveEntry e in zip.Entries) totalBytes += e.Length;
                long done = 0;
                var sw = Stopwatch.StartNew();
                long lastMs = 0;

                string baseFull = Path.GetFullPath(destDir);
                if (!baseFull.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    baseFull += Path.DirectorySeparatorChar;

                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    ThrowIfCancelled(isCancelled);
                    string rel = entry.FullName.Replace('\\', '/');
                    if (rel.EndsWith("/"))
                    {
                        Directory.CreateDirectory(Lp(Path.Combine(destDir, rel)));
                        continue;
                    }

                    // 目录穿越防护：解压目标必须落在暂存目录内
                    string target = Path.GetFullPath(Path.Combine(baseFull, rel));
                    if (!target.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
                        throw new Exception("压缩包包含非法路径，已中止解压");

                    string dir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(Lp(dir));

                    using (Stream input = entry.Open())
                    using (var output = new FileStream(Lp(target), FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[StreamBuffer];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output.Write(buffer, 0, read);
                            done += read;
                            if (sw.ElapsedMilliseconds - lastMs > 200)
                            {
                                lastMs = sw.ElapsedMilliseconds;
                                Report(progress, totalBytes > 0 ? done * 100.0 / totalBytes : -1,
                                    "正在解压 " + FormatBytes(done) + " / " + FormatBytes(totalBytes));
                            }
                        }
                    }
                }
            }
        }

        private static string NormalizeInstalledDir(string stagingDir, string versionDir)
        {
            string extracted = FindGameDir(stagingDir);

            if (Directory.Exists(Lp(versionDir)))
            {
                if (Directory.EnumerateFileSystemEntries(Lp(versionDir)).Any())
                    throw new Exception("版本目录已存在且不是空目录：" + versionDir);
                Directory.Delete(Lp(versionDir));
            }
            string parent = Path.GetDirectoryName(versionDir);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(Lp(parent));
            Directory.Move(Lp(extracted), Lp(versionDir));

            if (!File.Exists(Lp(Path.Combine(versionDir, GameExe))))
                throw new Exception("整理后的游戏目录无效（缺少 " + GameExe + "）");
            return versionDir;
        }

        private static string FindGameDir(string root)
        {
            if (File.Exists(Lp(Path.Combine(root, GameExe)))) return root;

            string found = null;
            foreach (string f in Directory.EnumerateFiles(Lp(root), GameExe, SearchOption.AllDirectories))
            {
                if (found == null || f.Length < found.Length) found = f;
            }
            if (string.IsNullOrEmpty(found))
                throw new Exception("未在解压目录中找到有效的游戏目录（" + GameExe + "）");

            found = StripLongPath(found);
            string dir = Path.GetDirectoryName(found);
            if (string.IsNullOrWhiteSpace(dir)) throw new Exception("解压后的游戏目录无效");
            return dir;
        }

        // ---------- 工具 ----------

        private static void Report(Action<double, string> progress, double pct, string msg)
        {
            if (progress == null) return;
            try { progress(pct, msg); } catch { }
        }

        private static void ThrowIfCancelled(Func<bool> isCancelled)
        {
            if (isCancelled != null && isCancelled()) throw new OperationCanceledException("已取消下载");
        }

        private static string SanitizeFileName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in value ?? "")
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            string s = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(s) ? "package" : s;
        }

        /// <summary>超过 240 字符时加 \\?\ 前缀，绕过 MAX_PATH（安装包里目录层级很深）。</summary>
        private static string Lp(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (path.StartsWith(@"\\?\")) return path;
            string full = path;
            try { if (!Path.IsPathRooted(path)) full = Path.GetFullPath(path); } catch { full = path; }
            if (full.Length < 240) return full;
            if (full.StartsWith(@"\\")) return @"\\?\UNC\" + full.Substring(2);
            return @"\\?\" + full;
        }

        private static string StripLongPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + path.Substring(8);
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path.Substring(4);
            return path;
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(Lp(path))) File.Delete(Lp(path)); } catch { }
        }

        private static void TryDeleteDir(string path)
        {
            try { if (Directory.Exists(Lp(path))) Directory.Delete(Lp(path), true); } catch { }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "-";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
            return v.ToString(i == 0 ? "0" : "0.0") + " " + units[i];
        }
    }
}
