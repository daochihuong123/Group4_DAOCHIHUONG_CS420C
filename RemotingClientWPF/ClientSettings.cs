using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace RemotingClientWPF
{
    public class ClientSettings
    {
        public string ServerUrl { get; set; } = "tcp://localhost:8085/CalculatorServiceURI";
        public string StudentName { get; set; } = "";
        public string StudentId { get; set; } = "";
        public string ChatSender { get; set; } = "SinhVien";
        public bool AutoConnect { get; set; } = false;
        public bool RememberSettings { get; set; } = true;
        public int SelectedTabIndex { get; set; } = 0;

        private static string SettingsFilePath
        {
            get
            {
                try
                {
                    string dir = AppDomain.CurrentDomain.BaseDirectory;
                    return Path.Combine(dir, "client_settings.json");
                }
                catch
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string dir = Path.Combine(appData, "ASPNetRemote");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    return Path.Combine(dir, "client_settings.json");
                }
            }
        }

        public static ClientSettings Load()
        {
            var settings = new ClientSettings();
            try
            {
                string path = SettingsFilePath;
                if (!File.Exists(path)) return settings;

                string json = File.ReadAllText(path, Encoding.UTF8);
                settings.ServerUrl = ExtractString(json, "ServerUrl", settings.ServerUrl);
                settings.StudentName = ExtractString(json, "StudentName", settings.StudentName);
                settings.StudentId = ExtractString(json, "StudentId", settings.StudentId);
                settings.ChatSender = ExtractString(json, "ChatSender", settings.ChatSender);
                settings.AutoConnect = ExtractBool(json, "AutoConnect", settings.AutoConnect);
                settings.RememberSettings = ExtractBool(json, "RememberSettings", settings.RememberSettings);
                settings.SelectedTabIndex = ExtractInt(json, "SelectedTabIndex", settings.SelectedTabIndex);
            }
            catch
            {
                // Fallback nếu có lỗi đọc file
            }
            return settings;
        }

        public void Save()
        {
            try
            {
                string path = SettingsFilePath;
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"ServerUrl\": \"{EscapeJson(ServerUrl)}\",");
                sb.AppendLine($"  \"StudentName\": \"{EscapeJson(StudentName)}\",");
                sb.AppendLine($"  \"StudentId\": \"{EscapeJson(StudentId)}\",");
                sb.AppendLine($"  \"ChatSender\": \"{EscapeJson(ChatSender)}\",");
                sb.AppendLine($"  \"AutoConnect\": {(AutoConnect ? "true" : "false")},");
                sb.AppendLine($"  \"RememberSettings\": {(RememberSettings ? "true" : "false")},");
                sb.AppendLine($"  \"SelectedTabIndex\": {SelectedTabIndex}");
                sb.AppendLine("}");
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Bỏ qua lỗi ghi nếu không có quyền ghi đĩa
            }
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        }

        private static string UnescapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private static string ExtractString(string json, string key, string defaultVal)
        {
            var match = Regex.Match(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*\"([^\"]*)\"");
            if (match.Success) return UnescapeJson(match.Groups[1].Value);
            return defaultVal;
        }

        private static bool ExtractBool(string json, string key, bool defaultVal)
        {
            var match = Regex.Match(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
            if (match.Success) return bool.Parse(match.Groups[1].Value.ToLower());
            return defaultVal;
        }

        private static int ExtractInt(string json, string key, int defaultVal)
        {
            var match = Regex.Match(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*(\\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int val)) return val;
            return defaultVal;
        }
    }
}
