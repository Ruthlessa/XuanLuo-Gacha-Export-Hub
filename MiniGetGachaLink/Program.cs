using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace MiniGetGachaLink
{
    internal static class Program
    {
        private delegate string LinkReader();

        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly Dictionary<string, string> CustomDirs = LoadCustomDirs();
        private static readonly HashSet<string> WuwaSkipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "windows", "$recycle.bin", "system volume information", "programdata",
            "program files", "program files (x86)", "node_modules", "appdata"
        };

        private static int Main()
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            Dictionary<string, string> links = new Dictionary<string, string>();
            TryRead("原神", "genshin", ReadGenshinUrl, links);
            TryRead("崩坏：星穹铁道", "hsr", ReadHsrUrl, links);
            TryRead("绝区零", "zzz", ReadZzzUrl, links);
            TryRead("鸣潮", "wuwa", ReadWuwaUrl, links);

            string output = Path.Combine(Directory.GetCurrentDirectory(), "gacha-links.json");
            try
            {
                File.WriteAllText(output, ToJson(links), new UTF8Encoding(false));
                Console.WriteLine("已生成：" + output);
                Console.WriteLine("共获取 " + links.Count + " 个游戏的抽卡链接。");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("写入 JSON 失败：" + ex.Message);
                return 1;
            }
        }

        private static void TryRead(string displayName, string game, LinkReader reader, Dictionary<string, string> links)
        {
            try
            {
                string url = reader();
                if (string.IsNullOrEmpty(url))
                {
                    Console.WriteLine("[跳过] " + displayName + "：未找到抽卡链接");
                    return;
                }
                links[game] = FixAuthkey(url);
                Console.WriteLine("[成功] " + displayName);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[跳过] " + displayName + "：" + ex.Message);
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

        private static string ReadGenshinUrl()
        {
            List<string> dirs = new List<string>();
            AddCustomDir(dirs, "genshin");
            AddUnique(dirs, HomePath("AppData", "LocalLow", "miHoYo", "原神"));
            AddUnique(dirs, HomePath("AppData", "LocalLow", "miHoYo", "Genshin Impact"));

            foreach (string dir in dirs)
            {
                string logFile = Path.Combine(dir, "output_log.txt");
                if (!File.Exists(logFile)) continue;
                Match match = Regex.Match(ReadAllTextShared(logFile), @"\w:/.+(GenshinImpact_Data|YuanShen_Data)");
                if (!match.Success) continue;

                foreach (string file in CacheCandidates(Path.Combine(match.Value, "webCaches")))
                {
                    MatchCollection urls = Regex.Matches(ReadAllTextShared(file),
                        @"https.+?auth_appid=webview_gacha.+?authkey=.+?game_biz=hk4e_\w+");
                    if (urls.Count > 0) return urls[urls.Count - 1].Value;
                }
            }
            return null;
        }

        private static string ReadHsrUrl()
        {
            List<string> dirs = new List<string>();
            AddCustomDir(dirs, "hsr");
            AddUnique(dirs, HomePath("AppData", "LocalLow", "miHoYo", "崩坏：星穹铁道"));
            AddUnique(dirs, HomePath("AppData", "LocalLow", "Cognosphere", "Star Rail"));

            foreach (string dir in dirs)
            {
                string logFile = Path.Combine(dir, "Player.log");
                if (!File.Exists(logFile)) continue;
                Match match = Regex.Match(ReadAllTextShared(logFile), @"\w:/.*?/StarRail_Data/", RegexOptions.IgnoreCase);
                if (!match.Success) continue;

                foreach (string file in CacheCandidates(Path.Combine(match.Value, "webCaches")))
                {
                    MatchCollection urls = Regex.Matches(ReadAllTextShared(file),
                        @"https[^?]+?\?[^?]+?&auth_appid=webview_gacha&.+?authkey=.+?&game_biz=hkrpg_\w+");
                    if (urls.Count > 0) return urls[urls.Count - 1].Value;
                }
            }
            return null;
        }

        private static string ReadZzzUrl()
        {
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
                    string text = ReadAllTextShared(logFile);
                    MatchCollection direct = Regex.Matches(text, @"https://[^\s""']+?/info");
                    if (direct.Count > 0) return direct[direct.Count - 1].Value;

                    Match match = Regex.Match(text, @"[A-Z]:[\\/].*?[\\/](?=ZenlessZoneZero_Data)", RegexOptions.IgnoreCase);
                    if (!match.Success) continue;
                    string dataPath = Path.Combine(match.Value, "ZenlessZoneZero_Data");
                    foreach (string file in CacheCandidates(Path.Combine(dataPath, "webCaches")))
                    {
                        MatchCollection urls = Regex.Matches(ReadAllTextShared(file), @"https.+?authkey=.+?end_id=");
                        if (urls.Count > 0) return urls[urls.Count - 1].Value;
                    }
                }
            }
            return null;
        }

        private static string ReadWuwaUrl()
        {
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
                string scanned = ScanWuwaDrives();
                if (HasWuwaLogs(scanned)) AddUnique(dirs, scanned);
            }

            foreach (string dir in dirs)
            {
                foreach (string logFile in WuwaLogCandidates(dir))
                {
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
}
