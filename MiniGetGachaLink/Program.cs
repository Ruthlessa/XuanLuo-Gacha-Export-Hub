using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MiniGetGachaLink
{
    internal static class Program
    {
        private delegate string LinkReader(Action<string> log, out bool pathFound);

        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly Dictionary<string, string> CustomDirs = LoadCustomDirs();
        private static readonly HashSet<string> WuwaSkipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "windows", "$recycle.bin", "system volume information", "programdata",
            "program files", "program files (x86)", "node_modules", "appdata"
        };

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        internal static int Generate(Action<string> log)
        {
            Dictionary<string, string> links = new Dictionary<string, string>();
            TryRead("原神", "genshin", ReadGenshinUrl, links, log);
            TryRead("崩坏：星穹铁道", "hsr", ReadHsrUrl, links, log);
            TryRead("绝区零", "zzz", ReadZzzUrl, links, log);
            TryRead("鸣潮", "wuwa", ReadWuwaUrl, links, log);

            string output = Path.Combine(Directory.GetCurrentDirectory(), "gacha-links.json");
            try
            {
                File.WriteAllText(output, ToJson(links), new UTF8Encoding(false));
                log("已生成：" + output);
                log("共获取 " + links.Count + " 个游戏的抽卡链接。");
                return links.Count;
            }
            catch (Exception ex)
            {
                log("写入 JSON 失败：" + ex.Message);
                return -1;
            }
        }

        private static void TryRead(string displayName, string game, LinkReader reader,
            Dictionary<string, string> links, Action<string> log)
        {
            log("正在读取 " + displayName + "...");
            try
            {
                bool pathFound;
                string url = reader(log, out pathFound);
                if (string.IsNullOrEmpty(url))
                {
                    log(pathFound
                        ? "[无链接] " + displayName + "：已识别到路径，但未找到抽卡链接"
                        : "[未识别] " + displayName + "：未识别到游戏路径");
                    return;
                }
                links[game] = FixAuthkey(url);
                log("[成功] " + displayName + "：已获取抽卡链接");
            }
            catch (Exception ex)
            {
                log("[跳过] " + displayName + "：读取失败，" + ex.Message);
            }
        }

        private static string ToJson(Dictionary<string, string> links)
        {
            if (links.Count == 0) return "{}";
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            int index = 0;
            foreach (KeyValuePair<string, string> pair in links)
            {
                sb.Append("\n  ");
                sb.Append(Json.Serialize(pair.Key));
                sb.Append(": ");
                sb.Append(Json.Serialize(pair.Value));
                if (++index < links.Count) sb.Append(',');
            }
            sb.Append("\n}");
            return sb.ToString();
        }

        private static string FixAuthkey(string url)
        {
            return Regex.Replace(url, @"authkey=([^&]+)", delegate(Match match)
            {
                string value = match.Groups[1].Value;
                if (value.IndexOf('=') >= 0 && value.IndexOf('%') < 0)
                    return "authkey=" + Uri.EscapeDataString(value);
                return match.Value;
            });
        }

        private static string ReadGenshinUrl(Action<string> log, out bool pathFound)
        {
            pathFound = false;
            List<string> dirs = new List<string>();
            AddCustomDir(dirs, "genshin");
            AddUnique(dirs, HomePath("AppData", "LocalLow", "miHoYo", "原神"));
            AddUnique(dirs, HomePath("AppData", "LocalLow", "miHoYo", "Genshin Impact"));

            foreach (string dir in dirs)
            {
                string logFile = Path.Combine(dir, "output_log.txt");
                if (!File.Exists(logFile)) continue;
                pathFound = true;
                log("[路径] 原神日志：" + logFile);
                Match match = Regex.Match(ReadAllTextShared(logFile), @"\w:/.+(GenshinImpact_Data|YuanShen_Data)");
                if (!match.Success)
                {
                    log("[提示] 原神：日志中未识别到游戏数据目录");
                    continue;
                }

                string cacheDir = Path.Combine(match.Value, "webCaches");
                log("[路径] 原神缓存目录：" + cacheDir);
                List<string> files = CacheCandidates(cacheDir);
                if (files.Count == 0) log("[提示] 原神：缓存目录中未找到 data_2 文件");
                foreach (string file in files)
                {
                    log("[路径] 原神缓存文件：" + file);
                    MatchCollection urls = Regex.Matches(ReadAllTextShared(file),
                        @"https.+?auth_appid=webview_gacha.+?authkey=.+?game_biz=hk4e_\w+");
                    if (urls.Count > 0) return urls[urls.Count - 1].Value;
                }
            }
            return null;
        }

        private static string ReadHsrUrl(Action<string> log, out bool pathFound)
        {
            pathFound = false;
            List<string> dirs = new List<string>();
            AddCustomDir(dirs, "hsr");
            AddUnique(dirs, HomePath("AppData", "LocalLow", "miHoYo", "崩坏：星穹铁道"));
            AddUnique(dirs, HomePath("AppData", "LocalLow", "Cognosphere", "Star Rail"));

            foreach (string dir in dirs)
            {
                string logFile = Path.Combine(dir, "Player.log");
                if (!File.Exists(logFile)) continue;
                pathFound = true;
                log("[路径] 崩铁日志：" + logFile);
                Match match = Regex.Match(ReadAllTextShared(logFile), @"\w:/.*?/StarRail_Data/", RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    log("[提示] 崩铁：日志中未识别到游戏数据目录");
                    continue;
                }

                string cacheDir = Path.Combine(match.Value, "webCaches");
                log("[路径] 崩铁缓存目录：" + cacheDir);
                List<string> files = CacheCandidates(cacheDir);
                if (files.Count == 0) log("[提示] 崩铁：缓存目录中未找到 data_2 文件");
                foreach (string file in files)
                {
                    log("[路径] 崩铁缓存文件：" + file);
                    MatchCollection urls = Regex.Matches(ReadAllTextShared(file),
                        @"https[^?]+?\?[^?]+?&auth_appid=webview_gacha&.+?authkey=.+?&game_biz=hkrpg_\w+");
                    if (urls.Count > 0) return urls[urls.Count - 1].Value;
                }
            }
            return null;
        }

        private static string ReadZzzUrl(Action<string> log, out bool pathFound)
        {
            pathFound = false;
            List<string> dirs = new List<string>();
            AddCustomDir(dirs, "zzz");
            string[] vendors = { "miHoYo", "Cognosphere" };
            string[] names = { "绝区零", "ZenlessZoneZero", "Zenless Zone Zero" };
            foreach (string vendor in vendors)
                foreach (string name in names)
                    AddUnique(dirs, HomePath("AppData", "LocalLow", vendor, name));

            foreach (string dir in dirs)
            {
                string[] logs = { Path.Combine(dir, "Player.log"), Path.Combine(dir, "output_log.txt") };
                foreach (string logFile in logs)
                {
                    if (!File.Exists(logFile)) continue;
                    pathFound = true;
                    log("[路径] 绝区零日志：" + logFile);
                    string text = ReadAllTextShared(logFile);
                    MatchCollection direct = Regex.Matches(text, @"https://[^\s""']+?/info");
                    if (direct.Count > 0) return direct[direct.Count - 1].Value;

                    Match match = Regex.Match(text, @"[A-Z]:[\\/].*?[\\/](?=ZenlessZoneZero_Data)", RegexOptions.IgnoreCase);
                    if (!match.Success)
                    {
                        log("[提示] 绝区零：日志中未识别到游戏数据目录");
                        continue;
                    }
                    string dataPath = Path.Combine(match.Value, "ZenlessZoneZero_Data");
                    string cacheDir = Path.Combine(dataPath, "webCaches");
                    log("[路径] 绝区零缓存目录：" + cacheDir);
                    List<string> files = CacheCandidates(cacheDir);
                    if (files.Count == 0) log("[提示] 绝区零：缓存目录中未找到 data_2 文件");
                    foreach (string file in files)
                    {
                        log("[路径] 绝区零缓存文件：" + file);
                        MatchCollection urls = Regex.Matches(ReadAllTextShared(file), @"https.+?authkey=.+?end_id=");
                        if (urls.Count > 0) return urls[urls.Count - 1].Value;
                    }
                }
            }
            return null;
        }

        private static string ReadWuwaUrl(Action<string> log, out bool pathFound)
        {
            pathFound = false;
            List<string> dirs = new List<string>();
            string custom = CustomDir("wuwa");
            if (HasWuwaLogs(custom)) AddUnique(dirs, custom);

            string registry = ReadWuwaRegistry();
            if (HasWuwaLogs(registry)) AddUnique(dirs, registry);

            for (int c = 67; c <= 90; c++)
            {
                string dir = Path.Combine(((char)c) + ":\\", "Wuthering Waves", "Wuthering Waves Game");
                if (HasWuwaLogs(dir)) AddUnique(dirs, dir);
            }
            if (dirs.Count == 0)
            {
                log("[扫描] 鸣潮：默认位置未找到日志，正在扫描其他目录...");
                string scanned = ScanWuwaDrives();
                if (HasWuwaLogs(scanned)) AddUnique(dirs, scanned);
            }

            foreach (string dir in dirs)
            {
                pathFound = true;
                log("[路径] 鸣潮游戏目录：" + dir);
                foreach (string logFile in WuwaLogCandidates(dir))
                {
                    log("[路径] 鸣潮日志：" + logFile);
                    string text = DecryptWuwaLog(ReadAllBytesShared(logFile));
                    string link = ExtractWuwaLink(text);
                    if (!string.IsNullOrEmpty(link)) return link;
                }
            }
            return null;
        }

        private static List<string> CacheCandidates(string webCaches)
        {
            List<string> files = new List<string>();
            AddFile(files, Path.Combine(webCaches, "Cache", "Cache_Data", "data_2"));
            try
            {
                foreach (string dir in Directory.GetDirectories(webCaches))
                    AddFile(files, Path.Combine(dir, "Cache", "Cache_Data", "data_2"));
            }
            catch { }
            files.Sort(delegate(string a, string b) { return LastWriteTime(b).CompareTo(LastWriteTime(a)); });
            return files;
        }

        private static DateTime LastWriteTime(string file)
        {
            try { return File.GetLastWriteTimeUtc(file); } catch { return DateTime.MinValue; }
        }

        private static void AddFile(List<string> files, string file)
        {
            try
            {
                if (File.Exists(file) && !files.Contains(file)) files.Add(file);
            }
            catch { }
        }

        private static string DecryptWuwaLog(byte[] input)
        {
            byte[] output = new byte[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                byte b = input[i];
                output[i] = (b & 0x0F) % 2 == 1 ? (byte)(b ^ 0xA5) : (byte)(b ^ 0xEF);
            }
            return Encoding.UTF8.GetString(output);
        }

        private static string ExtractWuwaLink(string text)
        {
            MatchCollection found = Regex.Matches(text,
                @"https[^\s""'<>]*/aki/gacha/index\.html#/record[^\s""'<>]*");
            string latest = null;
            foreach (Match match in found)
            {
                string clean = match.Value.Trim().TrimEnd(',', '，', ';', '；');
                if (clean.IndexOf("player_id=", StringComparison.Ordinal) >= 0 &&
                    clean.IndexOf("record_id=", StringComparison.Ordinal) >= 0)
                    latest = clean;
            }
            return latest;
        }

        private static bool HasWuwaLogs(string dir)
        {
            return !string.IsNullOrEmpty(dir) && WuwaLogCandidates(dir).Count > 0;
        }

        private static List<string> WuwaLogCandidates(string dir)
        {
            List<string> files = new List<string>();
            if (string.IsNullOrEmpty(dir)) return files;
            string logsDir = Path.Combine(dir, "Client", "Saved", "Logs");
            if (!Directory.Exists(logsDir)) return files;

            AddFile(files, Path.Combine(logsDir, "Client.log"));
            try
            {
                List<string> backups = new List<string>();
                foreach (string file in Directory.GetFiles(logsDir))
                {
                    if (Regex.IsMatch(Path.GetFileName(file), @"^Client-backup.*\.log$", RegexOptions.IgnoreCase))
                        backups.Add(file);
                }
                backups.Sort(delegate(string a, string b) { return LastWriteTime(b).CompareTo(LastWriteTime(a)); });
                foreach (string file in backups) AddFile(files, file);
            }
            catch { }
            return files;
        }

        private static string ReadWuwaRegistry()
        {
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                using (RegistryKey uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (uninstall == null) return null;
                    foreach (string name in uninstall.GetSubKeyNames())
                    {
                        using (RegistryKey key = uninstall.OpenSubKey(name))
                        {
                            if (key == null) continue;
                            string install = Convert.ToString(key.GetValue("InstallPath", ""));
                            string display = Convert.ToString(key.GetValue("DisplayName", ""));
                            if (!string.IsNullOrEmpty(install) &&
                                (ContainsWuthering(display) || ContainsWuthering(install)))
                                return install;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static bool ContainsWuthering(string value)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf("Wuthering", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ScanWuwaDrives()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(12);
            for (int c = 67; c <= 90; c++)
            {
                string root = ((char)c) + ":\\";
                if (!Directory.Exists(root)) continue;
                string found = ScanWuwaDir(root, 2, deadline);
                if (!string.IsNullOrEmpty(found)) return found;
                if (DateTime.UtcNow > deadline) break;
            }
            return null;
        }

        private static string ScanWuwaDir(string dir, int depth, DateTime deadline)
        {
            if (DateTime.UtcNow > deadline) return null;
            List<DirectoryInfo> dirs = new List<DirectoryInfo>();
            try
            {
                foreach (DirectoryInfo entry in new DirectoryInfo(dir).GetDirectories())
                    if (!WuwaSkipDirs.Contains(entry.Name)) dirs.Add(entry);
            }
            catch { return null; }

            foreach (DirectoryInfo entry in dirs)
            {
                if (ContainsWuthering(entry.Name) &&
                    Directory.Exists(Path.Combine(entry.FullName, "Client", "Saved", "Logs")))
                    return entry.FullName;
            }
            if (depth <= 1) return null;
            for (int i = 0; i < dirs.Count && i < 40; i++)
            {
                string found = ScanWuwaDir(dirs[i].FullName, depth - 1, deadline);
                if (!string.IsNullOrEmpty(found)) return found;
            }
            return null;
        }

        private static Dictionary<string, string> LoadCustomDirs()
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            try
            {
                string file = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "XuanLuo Gacha Export Hub", "userdata", "app.config.json");
                if (!File.Exists(file)) return result;
                var root = Json.DeserializeObject(ReadAllTextShared(file)) as Dictionary<string, object>;
                if (root == null || !root.ContainsKey("settings")) return result;
                var settings = root["settings"] as Dictionary<string, object>;
                if (settings == null || !settings.ContainsKey("gameLogDirs")) return result;
                var dirs = settings["gameLogDirs"] as Dictionary<string, object>;
                if (dirs == null) return result;
                foreach (KeyValuePair<string, object> pair in dirs)
                {
                    string value = pair.Value as string;
                    if (!string.IsNullOrEmpty(value)) result[pair.Key] = value;
                }
            }
            catch { }
            return result;
        }

        private static string CustomDir(string game)
        {
            string dir;
            return CustomDirs.TryGetValue(game, out dir) ? dir : null;
        }

        private static void AddCustomDir(List<string> dirs, string game)
        {
            AddUnique(dirs, CustomDir(game));
        }

        private static void AddUnique(List<string> dirs, string dir)
        {
            if (!string.IsNullOrEmpty(dir) && !dirs.Contains(dir)) dirs.Add(dir);
        }

        private static string HomePath(params string[] parts)
        {
            string path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (string part in parts) path = Path.Combine(path, part);
            return path;
        }

        private static FileStream OpenReadShared(string file)
        {
            return new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }

        private static string ReadAllTextShared(string file)
        {
            using (FileStream stream = OpenReadShared(file))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                return reader.ReadToEnd();
        }

        private static byte[] ReadAllBytesShared(string file)
        {
            using (FileStream stream = OpenReadShared(file))
            using (MemoryStream memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly TextBox logBox;
        private readonly Label statusLabel;
        private readonly Button runButton;
        private bool running;

        internal MainForm()
        {
            Text = "MiniGetGachaLink";
            StartPosition = FormStartPosition.CenterScreen;
            Width = 680;
            Height = 440;
            MinimumSize = new Size(560, 340);
            Font = new Font("Microsoft YaHei UI", 9F);

            logBox = new TextBox();
            logBox.Dock = DockStyle.Fill;
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.WordWrap = false;
            logBox.ScrollBars = ScrollBars.Both;
            logBox.BackColor = Color.White;

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 44;
            bottom.Padding = new Padding(10, 7, 10, 7);

            statusLabel = new Label();
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Text = "准备获取抽卡链接";

            runButton = new Button();
            runButton.Dock = DockStyle.Right;
            runButton.Width = 104;
            runButton.Text = "重新获取";
            runButton.Click += delegate { Start(); };

            bottom.Controls.Add(statusLabel);
            bottom.Controls.Add(runButton);
            Controls.Add(logBox);
            Controls.Add(bottom);
            Shown += delegate { Start(); };
        }

        private void Start()
        {
            if (running) return;
            running = true;
            runButton.Enabled = false;
            statusLabel.Text = "正在获取抽卡链接...";
            logBox.Clear();
            Log("输出目录：" + Directory.GetCurrentDirectory());

            Task.Factory.StartNew(delegate
            {
                try
                {
                    int count = Program.Generate(Log);
                    SetCompleted(count);
                }
                catch (Exception ex)
                {
                    Log("运行失败：" + ex.Message);
                    SetCompleted(-1);
                }
            });
        }

        internal void Log(string message)
        {
            if (IsDisposed || Disposing) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (logBox.IsDisposed) return;
                    string time = DateTime.Now.ToString("HH:mm:ss");
                    logBox.AppendText("[" + time + "] " + message + Environment.NewLine);
                });
            }
            catch { }
        }

        private void SetCompleted(int count)
        {
            if (IsDisposed || Disposing) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    running = false;
                    runButton.Enabled = true;
                    statusLabel.Text = count >= 0
                        ? "完成，共获取 " + count + " 个游戏的抽卡链接"
                        : "运行失败，请查看日志";
                });
            }
            catch { }
        }
    }
}
