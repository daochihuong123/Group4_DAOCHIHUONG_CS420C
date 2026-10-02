using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ASP.Net_Remote
{
    [Serializable]
    public class ChatMessage
    {
        public int Id { get; set; }
        public string Sender { get; set; }
        public string Content { get; set; }
        public DateTime Time { get; set; }
        public bool IsServer { get; set; }

        public string FormattedTime
        {
            get { return Time.ToString("HH:mm:ss"); }
        }
    }

    [Serializable]
    public class RemoteFileInfo
    {
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public string Sender { get; set; }
        public DateTime UploadTime { get; set; }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F2} MB";
        }

        public string FormattedSize
        {
            get { return FormatBytes(FileSize); }
        }

        public string FormattedTime
        {
            get { return UploadTime.ToString("HH:mm:ss  dd/MM/yyyy"); }
        }
    }

    // Kế thừa MarshalByRefObject để đối tượng có thể được gọi từ xa qua mạng
    public class CalculatorService : MarshalByRefObject
    {
        private static readonly List<ChatMessage> _chatHistory = new List<ChatMessage>();
        private static readonly object _lockChat = new object();
        private static int _messageCounter = 0;

        private static readonly List<RemoteFileInfo> _fileList = new List<RemoteFileInfo>();
        private static readonly object _lockFiles = new object();
        private static readonly string StorageDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServerStorage");

        public static event Action<ChatMessage> MessageReceived;
        public static event Action<RemoteFileInfo> FileReceived;

        static CalculatorService()
        {
            try
            {
                if (!Directory.Exists(StorageDir))
                    Directory.CreateDirectory(StorageDir);

                var dirInfo = new DirectoryInfo(StorageDir);
                foreach (var f in dirInfo.GetFiles())
                {
                    _fileList.Add(new RemoteFileInfo
                    {
                        FileName = f.Name,
                        FileSize = f.Length,
                        Sender = "Server (Có sẵn)",
                        UploadTime = f.LastWriteTime
                    });
                }
            }
            catch { }
        }

        public string GuiLoiChao(string tenSV, string maSV)
        {
            Console.WriteLine($"[LOG SERVER] Đang xử lý yêu cầu từ: {tenSV} ({maSV})");
            return $"Xin chào {tenSV} ({maSV})! Server .NET Remoting đã nhận và xử lý thành công.";
        }

        public double TinhToan(double a, double b, string phepToan)
        {
            Console.WriteLine($"[LOG SERVER] Nhận phép toán: {a} {phepToan} {b}");
            switch (phepToan)
            {
                case "+": return a + b;
                case "-": return a - b;
                case "*": return a * b;
                case "/": return b != 0 ? a / b : double.NaN;
                default: return 0;
            }
        }

        // ═══════════ CHAT REMOTING API ═══════════

        public void GuiTinNhan(string sender, string noiDung, bool isServer)
        {
            if (string.IsNullOrWhiteSpace(noiDung)) return;

            ChatMessage msg;
            lock (_lockChat)
            {
                _messageCounter++;
                msg = new ChatMessage
                {
                    Id = _messageCounter,
                    Sender = string.IsNullOrWhiteSpace(sender) ? (isServer ? "Server" : "Client") : sender.Trim(),
                    Content = noiDung.Trim(),
                    Time = DateTime.Now,
                    IsServer = isServer
                };
                _chatHistory.Add(msg);
            }

            Console.WriteLine($"[CHAT] {msg.Sender}: {msg.Content}");

            try
            {
                MessageReceived?.Invoke(msg);
            }
            catch { }
        }

        public List<ChatMessage> LayDanhSachTinNhan(int lastId)
        {
            lock (_lockChat)
            {
                return _chatHistory.Where(m => m.Id > lastId).ToList();
            }
        }

        public static void GuiTinNhanTuServer(string noiDung)
        {
            if (string.IsNullOrWhiteSpace(noiDung)) return;

            ChatMessage msg;
            lock (_lockChat)
            {
                _messageCounter++;
                msg = new ChatMessage
                {
                    Id = _messageCounter,
                    Sender = "Server (Admin)",
                    Content = noiDung.Trim(),
                    Time = DateTime.Now,
                    IsServer = true
                };
                _chatHistory.Add(msg);
            }

            Console.WriteLine($"[CHAT] {msg.Sender}: {msg.Content}");

            try
            {
                MessageReceived?.Invoke(msg);
            }
            catch { }
        }

        public static List<ChatMessage> LayToanBoTinNhan()
        {
            lock (_lockChat)
            {
                return new List<ChatMessage>(_chatHistory);
            }
        }

        // ═══════════ FILE TRANSFER REMOTING API ═══════════

        public bool GuiTapTin(string sender, string fileName, byte[] fileData)
        {
            try
            {
                if (fileData == null || string.IsNullOrWhiteSpace(fileName))
                    return false;

                string safeName = Path.GetFileName(fileName);
                if (!Directory.Exists(StorageDir))
                    Directory.CreateDirectory(StorageDir);

                string savePath = Path.Combine(StorageDir, safeName);
                File.WriteAllBytes(savePath, fileData);

                var info = new RemoteFileInfo
                {
                    FileName = safeName,
                    FileSize = fileData.Length,
                    Sender = string.IsNullOrWhiteSpace(sender) ? "Client" : sender.Trim(),
                    UploadTime = DateTime.Now
                };

                lock (_lockFiles)
                {
                    _fileList.RemoveAll(f => f.FileName.Equals(safeName, StringComparison.OrdinalIgnoreCase));
                    _fileList.Insert(0, info);
                }

                Console.WriteLine($"[FILE] Đã nhận tệp: {safeName} ({info.FormattedSize}) từ {info.Sender}");

                try { FileReceived?.Invoke(info); } catch { }
                try { GuiTinNhan(info.Sender, $"Đã gửi tệp: {safeName} ({info.FormattedSize})", false); } catch { }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FILE ERROR] Không thể lưu tệp: {ex.Message}");
                return false;
            }
        }

        public int LaySoLuongTapTin()
        {
            lock (_lockFiles)
            {
                return _fileList.Count;
            }
        }

        public List<RemoteFileInfo> LayDanhSachTapTin()
        {
            lock (_lockFiles)
            {
                return new List<RemoteFileInfo>(_fileList);
            }
        }

        public byte[] TaiTapTin(string fileName)
        {
            try
            {
                string safeName = Path.GetFileName(fileName);
                string filePath = Path.Combine(StorageDir, safeName);
                if (File.Exists(filePath))
                {
                    Console.WriteLine($"[FILE] Đang tải tệp: {safeName} cho Client");
                    return File.ReadAllBytes(filePath);
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FILE ERROR] Không thể đọc tệp để tải: {ex.Message}");
                return null;
            }
        }

        public static bool LuuTapTinTuServer(string sourceFilePath)
        {
            try
            {
                if (!File.Exists(sourceFilePath)) return false;

                string fileName = Path.GetFileName(sourceFilePath);
                byte[] data = File.ReadAllBytes(sourceFilePath);

                if (!Directory.Exists(StorageDir))
                    Directory.CreateDirectory(StorageDir);

                string targetPath = Path.Combine(StorageDir, fileName);
                File.WriteAllBytes(targetPath, data);

                var info = new RemoteFileInfo
                {
                    FileName = fileName,
                    FileSize = data.Length,
                    Sender = "Server (Admin)",
                    UploadTime = DateTime.Now
                };

                lock (_lockFiles)
                {
                    _fileList.RemoveAll(f => f.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                    _fileList.Insert(0, info);
                }

                Console.WriteLine($"[FILE] Server đã thêm tệp chia sẻ: {fileName} ({info.FormattedSize})");

                try { FileReceived?.Invoke(info); } catch { }
                try { new CalculatorService().GuiTinNhan("Server", $"Đã gửi tệp: {fileName} ({info.FormattedSize})", true); } catch { }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string LayThuMucLuuTru()
        {
            if (!Directory.Exists(StorageDir))
                Directory.CreateDirectory(StorageDir);
            return StorageDir;
        }
    }
}