using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using ASP.Net_Remote;

namespace RemotingClientWPF
{
    public partial class MainWindow : Window
    {
        private static bool _channelRegistered = false;
        private static TcpClientChannel _channel;
        private CalculatorService _remoteService;
        private bool _isConnected = false;
        private DispatcherTimer _clockTimer;
        private DispatcherTimer _chatTimer;
        private int _lastChatId = 0;
        private string _selectedFilePath;
        private string _clientDownloadDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ClientDownloads");
        private int _lastFileCount = -1;
        private int _filePollCounter = 0;
        private ClientSettings _settings;
        private bool _isLoadingSettings = true;

        public MainWindow()
        {
            InitializeComponent();

            // Timer đồng hồ
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, e) => ClientClockText.Text = DateTime.Now.ToString("HH:mm:ss  dd/MM/yyyy");
            _clockTimer.Start();
            ClientClockText.Text = DateTime.Now.ToString("HH:mm:ss  dd/MM/yyyy");

            // Timer cập nhật chat định kỳ
            _chatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _chatTimer.Tick += ChatTimer_Tick;

            UpdateConnectionUI(false);

            // Nạp cấu hình đã lưu
            _settings = ClientSettings.Load();
            ApplySettings();
            _isLoadingSettings = false;

            this.Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveCurrentSettings(false);
        }

        private void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            if (!_isLoadingSettings)
            {
                SaveCurrentSettings(false);
            }
        }

        private void SaveSettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentSettings(true);
        }

        private void ApplySettings()
        {
            if (_settings == null) return;

            if (!string.IsNullOrEmpty(_settings.ServerUrl))
                ServerUrlTextBox.Text = _settings.ServerUrl;

            if (!string.IsNullOrEmpty(_settings.StudentName))
                NameTextBox.Text = _settings.StudentName;

            if (!string.IsNullOrEmpty(_settings.StudentId))
                StudentIdTextBox.Text = _settings.StudentId;

            if (!string.IsNullOrEmpty(_settings.ChatSender))
                ChatSenderTextBox.Text = _settings.ChatSender;

            AutoConnectCheckBox.IsChecked = _settings.AutoConnect;
            RememberSettingsCheckBox.IsChecked = _settings.RememberSettings;

            if (_settings.SelectedTabIndex >= 0 && _settings.SelectedTabIndex < ClientTabControl.Items.Count)
            {
                ClientTabControl.SelectedIndex = _settings.SelectedTabIndex;
            }

            // Tự động kết nối nếu được bật và nhớ cấu hình
            if (_settings.AutoConnect && _settings.RememberSettings)
            {
                var autoConnectTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                autoConnectTimer.Tick += (s, ev) =>
                {
                    autoConnectTimer.Stop();
                    if (!_isConnected)
                    {
                        StatusBarText.Text = "Đang tự động kết nối theo cấu hình đã lưu…";
                        Connect();
                    }
                };
                autoConnectTimer.Start();
            }
        }

        private void SaveCurrentSettings(bool notify = false)
        {
            if (_isLoadingSettings || _settings == null) return;

            if (RememberSettingsCheckBox.IsChecked != true)
            {
                _settings.RememberSettings = false;
                _settings.AutoConnect = false;
                _settings.Save();
                if (notify) StatusBarText.Text = "Đã tắt ghi nhớ cấu hình.";
                return;
            }

            _settings.ServerUrl = ServerUrlTextBox.Text.Trim();
            _settings.StudentName = NameTextBox.Text.Trim();
            _settings.StudentId = StudentIdTextBox.Text.Trim();
            _settings.ChatSender = ChatSenderTextBox.Text.Trim();
            _settings.AutoConnect = AutoConnectCheckBox.IsChecked == true;
            _settings.RememberSettings = true;
            _settings.SelectedTabIndex = ClientTabControl.SelectedIndex;

            _settings.Save();

            if (notify)
            {
                StatusBarText.Text = "Đã lưu cấu hình (IP, Họ tên, Mã SV, Chat)!";
                if (SaveSettingsBtn != null)
                {
                    SaveSettingsBtn.Content = "Đã lưu!";
                    var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    t.Tick += (s, e) =>
                    {
                        t.Stop();
                        SaveSettingsBtn.Content = "Lưu cấu hình";
                    };
                    t.Start();
                }
            }
        }

        // ═══════════ DÁN IP & KẾT NỐI ═══════════

        private void PasteIpBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    ShowError("Clipboard hiện đang trống. Hãy sao chép địa chỉ IP từ Server trước!");
                    return;
                }

                string raw = Clipboard.GetText()?.Trim();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    ShowError("Clipboard không chứa nội dung hợp lệ.");
                    return;
                }

                // Xử lý thông minh:
                // Trường hợp 1: Copy toàn bộ URL (ví dụ từ nút Sao chép ở Server: tcp://192.168.1.10:8085/CalculatorServiceURI)
                if (raw.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
                {
                    ServerUrlTextBox.Text = raw;
                }
                else
                {
                    // Trường hợp 2: Copy IP đơn thuần (vd: 192.168.1.10) hoặc IP:port (vd: 192.168.1.10:8085)
                    string host = raw;
                    string port = "8085";

                    if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                        host = host.Substring(7);
                    else if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        host = host.Substring(8);

                    if (host.Contains("/"))
                    {
                        var slashParts = host.Split(new[] { '/' }, 2);
                        host = slashParts[0];
                    }

                    if (host.Contains(":"))
                    {
                        var colonParts = host.Split(':');
                        host = colonParts[0].Trim();
                        if (colonParts.Length > 1 && !string.IsNullOrWhiteSpace(colonParts[1]))
                            port = colonParts[1].Trim();
                    }

                    ServerUrlTextBox.Text = $"tcp://{host}:{port}/CalculatorServiceURI";
                }

                ServerUrlTextBox.Focus();
                ServerUrlTextBox.SelectAll();

                // Hiệu ứng phản hồi trên nút Dán IP
                PasteIpBtnText.Text = "Đã dán!";
                StatusBarText.Text = $"Đã cập nhật Server URL: {ServerUrlTextBox.Text}";
                SaveCurrentSettings(false);

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (s, args) =>
                {
                    PasteIpBtnText.Text = "Dán IP";
                    timer.Stop();
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                ShowError($"Không thể dán từ clipboard: {ex.Message}");
            }
        }

        private void ConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected)
                Connect();
            else
                Disconnect();
        }

        private void Connect()
        {
            try
            {
                string serverUrl = ServerUrlTextBox.Text.Trim();
                if (string.IsNullOrEmpty(serverUrl))
                {
                    ShowError("Vui lòng nhập Server URL.");
                    return;
                }

                // Kiểm tra nếu người dùng chưa thay thế placeholder IP
                if (serverUrl.Contains("<") || serverUrl.Contains(">") || serverUrl.Contains("IP-SERVER"))
                {
                    ShowError("Vui lòng thay thế <IP-SERVER> bằng địa chỉ IP thật của máy Server!\n\nVí dụ: tcp://192.168.1.10:8085/CalculatorServiceURI\n(Hoặc dùng tcp://localhost:8085/... nếu đang chạy cùng máy)");
                    return;
                }

                // Đăng ký TCP Client Channel một lần duy nhất an toàn
                EnsureChannelRegistered();

                _remoteService = (CalculatorService)Activator.GetObject(
                    typeof(CalculatorService),
                    serverUrl
                );

                if (_remoteService == null)
                {
                    throw new Exception("Không thể tạo đối tượng Remote Service.");
                }

                // Thử gọi kiểm tra kết nối
                string testResult = _remoteService.GuiLoiChao("Client-Test", "INIT");
                if (testResult == null)
                {
                    throw new Exception("Server không phản hồi.");
                }

                _isConnected = true;
                UpdateConnectionUI(true);
                StatusBarText.Text = $"Đã kết nối  |  {serverUrl}";

                // Bắt đầu nhận tin nhắn chat
                _chatTimer.Start();
                PollChatMessages();

                // Tải danh sách tệp trên server
                LoadClientFileList();

                // Lưu cấu hình khi kết nối thành công
                SaveCurrentSettings(false);
            }
            catch (Exception ex)
            {
                _remoteService = null;
                _isConnected = false;
                UpdateConnectionUI(false);

                string message = "Không thể kết nối tới Server!\n\n" +
                                 $"Chi tiết lỗi: {ex.Message}\n\n" +
                                 "Hướng dẫn kiểm tra kết nối qua Radmin VPN (hoặc khác mạng):\n" +
                                 "1. Cả 2 máy đã vào chung phòng Radmin VPN chưa? (chấm tròn xanh cạnh tên máy).\n" +
                                 "2. Trên máy Server: đã chọn đúng IP Radmin VPN (đầu số 26.x.x.x) và bấm 'KHỞI ĐỘNG' chưa?\n" +
                                 "3. Trên máy Client: Hãy nhập đúng URL có IP Radmin của Server, ví dụ:\n" +
                                 "   tcp://26.x.x.x:8085/CalculatorServiceURI\n" +
                                 "4. Thử mở Command Prompt trên máy Client và gõ: ping <IP-Radmin-Của-Server> xem có phản hồi không.\n" +
                                 "5. Đảm bảo Windows Firewall trên máy Server đã cho phép cổng 8085.";

                ShowError(message);
            }
        }

        private static void EnsureChannelRegistered()
        {
            if (!_channelRegistered)
            {
                try
                {
                    var existing = ChannelServices.GetChannel("tcpClient");
                    if (existing == null)
                    {
                        var clientProv = new BinaryClientFormatterSinkProvider();
                        var serverProv = new BinaryServerFormatterSinkProvider();
                        serverProv.TypeFilterLevel = System.Runtime.Serialization.Formatters.TypeFilterLevel.Full;

                        var props = new System.Collections.Hashtable();
                        props["name"] = "tcpClient";

                        _channel = new TcpClientChannel(props, clientProv);
                        ChannelServices.RegisterChannel(_channel, false);
                    }
                    _channelRegistered = true;
                }
                catch
                {
                    _channelRegistered = true;
                }
            }
        }

        private void Disconnect()
        {
            try
            {
                _chatTimer.Stop();
                _remoteService = null;
                _isConnected = false;
                _selectedFilePath = null;

                UpdateConnectionUI(false);
                StatusBarText.Text = ".NET Remoting Client  ·  .NET Framework 4.7.2  ·  TCP Channel";

                // Xóa kết quả cũ
                GreetingResultText.Text = "Kết quả sẽ hiển thị tại đây…";
                GreetingResultText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4A6A8A"));
                if (CalcFormulaText != null) CalcFormulaText.Text = "KẾT QUẢ";
                CalcResultText.Text = "—";
                CalcResultText.ClearValue(ForegroundProperty);
            }
            catch (Exception ex)
            {
                ShowError("Lỗi khi ngắt kết nối: " + ex.Message);
            }
        }

        // ═══════════ GỬI LỜI CHÀO ═══════════

        private void SendGreetingBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateConnection()) return;

            string hoTen = NameTextBox.Text.Trim();
            string maSV = StudentIdTextBox.Text.Trim();

            if (string.IsNullOrEmpty(hoTen) || string.IsNullOrEmpty(maSV))
            {
                ShowError("Vui lòng nhập đầy đủ Họ tên và Mã sinh viên.");
                return;
            }

            try
            {
                string result = _remoteService.GuiLoiChao(hoTen, maSV);
                GreetingResultText.Text = result;
                GreetingResultText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));

                AnimateResultAppear(GreetingResultText);
                SaveCurrentSettings(false);
            }
            catch (Exception ex)
            {
                GreetingResultText.Text = "Lỗi: " + ex.Message;
                GreetingResultText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                CheckConnectionLost();
            }
        }

        // ═══════════ TÍNH TOÁN ═══════════

        private void NumberTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && CalculateBtn != null && CalculateBtn.IsEnabled)
            {
                CalculateBtn_Click(CalculateBtn, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void CalculateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateConnection()) return;

            string strA = (NumberATextBox.Text ?? "").Trim().Replace(',', '.');
            string strB = (NumberBTextBox.Text ?? "").Trim().Replace(',', '.');

            double a, b;
            if (!double.TryParse(strA, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out a))
            {
                ShowError("Số A không hợp lệ. Vui lòng nhập số thực hợp lệ.");
                NumberATextBox.Focus();
                return;
            }
            if (!double.TryParse(strB, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out b))
            {
                ShowError("Số B không hợp lệ. Vui lòng nhập số thực hợp lệ.");
                NumberBTextBox.Focus();
                return;
            }

            string op = GetSelectedOperation();
            string displayOp = op;
            if (op == "*") displayOp = "×";
            else if (op == "/") displayOp = "÷";
            else if (op == "-") displayOp = "−";

            try
            {
                double result = _remoteService.TinhToan(a, b, op);

                if (double.IsNaN(result))
                {
                    if (CalcFormulaText != null) CalcFormulaText.Text = $"{a} {displayOp} {b}";
                    CalcResultText.Text = "Không thể chia cho 0";
                    CalcResultText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                }
                else
                {
                    if (CalcFormulaText != null) CalcFormulaText.Text = "KẾT QUẢ";
                    CalcResultText.Text = $"{a} {displayOp} {b} = {result}";
                    CalcResultText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB74D"));
                }

                AnimateResultAppear(CalcResultText);
            }
            catch (Exception ex)
            {
                CalcResultText.Text = "Lỗi: " + ex.Message;
                CalcResultText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                CheckConnectionLost();
            }
        }

        // ═══════════ CHAT ═══════════

        private void ChatTimer_Tick(object sender, EventArgs e)
        {
            PollChatMessages();

            _filePollCounter++;
            if (_filePollCounter % 2 == 0) // Đồng bộ danh sách file định kỳ ~1.6 giây
            {
                PollFileList();
            }
        }

        private void PollFileList()
        {
            if (!_isConnected || _remoteService == null) return;
            try
            {
                int count = _remoteService.LaySoLuongTapTin();
                if (count != _lastFileCount)
                {
                    _lastFileCount = count;
                    LoadClientFileList();
                }
            }
            catch { }
        }

        private void PollChatMessages()
        {
            if (!_isConnected || _remoteService == null) return;

            try
            {
                var newMsgs = _remoteService.LayDanhSachTinNhan(_lastChatId);
                if (newMsgs != null && newMsgs.Count > 0)
                {
                    ChatEmptyHint.Visibility = Visibility.Collapsed;

                    foreach (var msg in newMsgs)
                    {
                        AddMessageToUI(msg);
                        if (msg.Id > _lastChatId)
                            _lastChatId = msg.Id;
                    }

                    ChatScrollViewer.ScrollToEnd();
                }
            }
            catch
            {
                // Không ngắt ứng dụng khi polling timeout ngẫu nhiên
            }
        }

        private void SendChatBtn_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }

        private void ChatInputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SendMessage();
                e.Handled = true;
            }
        }

        private void ChatAttachBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateConnection()) return;

            var ofd = new OpenFileDialog
            {
                Title = "Chọn ảnh, GIF, video, âm thanh hoặc tệp tin để gửi",
                Filter = "Tất cả media & tệp|*.*|Hình ảnh & GIF (*.png;*.jpg;*.gif)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|Âm thanh (*.mp3;*.wav)|*.mp3;*.wav;*.m4a;*.wma;*.aac|Video (*.mp4;*.wmv;*.avi)|*.mp4;*.wmv;*.avi;*.mov;*.mkv"
            };

            if (ofd.ShowDialog() == true)
            {
                SendMediaFile(ofd.FileName);
            }
        }

        // ═══════════ KÉO THẢ TỆP & DÁN CLIPBOARD (CTRL+V) ═══════════

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    HandleDroppedOrPastedFiles(files);
                }
                e.Handled = true;
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                var focused = Keyboard.FocusedElement as TextBox;
                bool isOtherTextBox = (focused != null && focused != ChatInputTextBox);

                // Ưu tiên 1: Dán danh sách tệp copy từ File Explorer (Ctrl+C ngoài thư mục -> Ctrl+V vào app)
                if (Clipboard.ContainsFileDropList())
                {
                    var fileList = Clipboard.GetFileDropList();
                    if (fileList != null && fileList.Count > 0)
                    {
                        var files = new string[fileList.Count];
                        fileList.CopyTo(files, 0);
                        HandleDroppedOrPastedFiles(files);
                        e.Handled = true;
                        return;
                    }
                }

                // Ưu tiên 2: Dán ảnh chụp màn hình (Snipping Tool / Win+Shift+S)
                if (Clipboard.ContainsImage() && !isOtherTextBox)
                {
                    PasteScreenshotAndSend();
                    e.Handled = true;
                    return;
                }
            }
        }

        private void HandleDroppedOrPastedFiles(string[] files)
        {
            if (files == null || files.Length == 0) return;

            // Nếu đang ở Tab Truyền Tệp Tin (Tab 3) và chưa kết nối thì chọn file sẵn
            if (ClientTabControl != null && ClientTabControl.SelectedIndex == 3)
            {
                string first = files[0];
                if (File.Exists(first))
                {
                    _selectedFilePath = first;
                    long len = new FileInfo(first).Length;
                    string size = len < 1024 ? $"{len} B" : (len < 1024 * 1024 ? $"{len / 1024.0:F1} KB" : $"{len / (1024.0 * 1024.0):F2} MB");
                    SelectedFileText.Text = $"{Path.GetFileName(first)} ({size})";
                    SelectedFileText.Foreground = Brushes.White;
                    UploadFileBtn.IsEnabled = _isConnected;

                    if (_isConnected && files.Length == 1)
                    {
                        UploadFileBtn_Click(UploadFileBtn, new RoutedEventArgs());
                        return;
                    }
                }
            }

            if (!ValidateConnection()) return;

            int sentCount = 0;
            foreach (var file in files)
            {
                if (File.Exists(file))
                {
                    SendMediaFile(file);
                    sentCount++;
                }
            }

            if (sentCount > 0)
            {
                if (ClientTabControl != null && ClientTabControl.SelectedIndex != 3)
                {
                    ClientTabControl.SelectedIndex = 2; // Tự chuyển sang tab Trò Chuyện
                }
                LoadClientFileList();
            }
        }

        private void PasteScreenshotAndSend()
        {
            if (!ValidateConnection()) return;

            try
            {
                var img = Clipboard.GetImage();
                if (img != null)
                {
                    string tempFile = Path.Combine(Path.GetTempPath(), $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                    using (var fs = new FileStream(tempFile, FileMode.Create))
                    {
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
                        encoder.Save(fs);
                    }
                    SendMediaFile(tempFile);
                    if (ClientTabControl != null && ClientTabControl.SelectedIndex != 3)
                    {
                        ClientTabControl.SelectedIndex = 2;
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError("Không thể dán ảnh từ clipboard: " + ex.Message);
            }
        }

        private void ChatInputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Các phím tắt Ctrl+V đã được xử lý tập trung tại Window_PreviewKeyDown
        }

        private void SendMediaFile(string filePath)
        {
            if (!ValidateConnection()) return;
            if (!File.Exists(filePath)) return;

            try
            {
                string fileName = Path.GetFileName(filePath);
                byte[] data = File.ReadAllBytes(filePath);
                string senderName = ChatSenderTextBox.Text.Trim();
                if (string.IsNullOrEmpty(senderName)) senderName = "Client";

                // Lưu sẵn vào ChatCache của client để xem preview ngay
                string cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChatCache");
                if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
                File.WriteAllBytes(Path.Combine(cacheDir, fileName), data);

                bool success = _remoteService.GuiTapTin(senderName, fileName, data);
                if (!success)
                {
                    ShowError("Không thể gửi tệp tin tới Server.");
                }
                else
                {
                    PollChatMessages();
                }
            }
            catch (Exception ex)
            {
                ShowError("Lỗi khi gửi tệp: " + ex.Message);
            }
        }

        private string FindLocalFile(string fileName)
        {
            try
            {
                string pathInDownloads = Path.Combine(_clientDownloadDir, fileName);
                if (File.Exists(pathInDownloads)) return pathInDownloads;

                string pathInCache = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChatCache", fileName);
                if (File.Exists(pathInCache)) return pathInCache;

                string pathInServer = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ServerStorage", fileName);
                if (File.Exists(pathInServer)) return pathInServer;
            }
            catch { }
            return null;
        }

        private void SendMessage()
        {
            if (!ValidateConnection()) return;

            string content = ChatInputTextBox.Text.Trim();
            if (string.IsNullOrEmpty(content)) return;

            string senderName = ChatSenderTextBox.Text.Trim();
            if (string.IsNullOrEmpty(senderName))
                senderName = "Client";

            try
            {
                _remoteService.GuiTinNhan(senderName, content, false);
                ChatInputTextBox.Text = "";

                // Cập nhật ngay lập tức tin nhắn mới
                PollChatMessages();
                SaveCurrentSettings(false);
            }
            catch (Exception ex)
            {
                ShowError("Lỗi khi gửi tin nhắn: " + ex.Message);
            }
        }

        private void AddMessageToUI(ChatMessage msg)
        {
            string currentSender = ChatSenderTextBox.Text.Trim();
            bool isMine = !msg.IsServer && string.Equals(msg.Sender, currentSender, StringComparison.OrdinalIgnoreCase);

            var bubble = new Border
            {
                CornerRadius = isMine ? new CornerRadius(14, 14, 2, 14) : new CornerRadius(14, 14, 14, 2),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = isMine ? new Thickness(50, 4, 4, 6) : new Thickness(4, 4, 50, 6),
                HorizontalAlignment = isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                MaxWidth = 520
            };

            var stack = new StackPanel();

            // Header (Tên + Giờ)
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var senderText = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.Bold
            };

            if (msg.IsServer)
            {
                senderText.Text = "[Server] " + msg.Sender;
                senderText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));
                bubble.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F281E"));
                bubble.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1B4D35"));
                bubble.BorderThickness = new Thickness(1);
            }
            else if (isMine)
            {
                senderText.Text = "Bạn (" + msg.Sender + ")";
                senderText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60A5FA"));
                bubble.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#142A4A"));
                bubble.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E4B82"));
                bubble.BorderThickness = new Thickness(1);
            }
            else
            {
                senderText.Text = msg.Sender;
                senderText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#93C5FD"));
                bubble.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111C30"));
                bubble.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E2E4B"));
                bubble.BorderThickness = new Thickness(1);
            }

            Grid.SetColumn(senderText, 0);
            headerGrid.Children.Add(senderText);

            var timeText = new TextBlock
            {
                Text = msg.FormattedTime,
                FontSize = 10,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(timeText, 1);
            headerGrid.Children.Add(timeText);

            stack.Children.Add(headerGrid);

            // Nội dung tin nhắn
            var contentText = new TextBlock
            {
                Text = msg.Content,
                FontSize = 13.5,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19
            };
            stack.Children.Add(contentText);

            // Kiểm tra và hiển thị preview đa phương tiện (Ảnh, GIF, Audio, Video)
            if (ChatMediaHelper.TryExtractFileName(msg.Content, out string fileName))
            {
                string localPath = FindLocalFile(fileName);

                if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
                {
                    var preview = ChatMediaHelper.BuildMediaPreview(localPath, fileName, () => DownloadFile(fileName));
                    stack.Children.Add(preview);
                }
                else if (_isConnected && _remoteService != null)
                {
                    var placeholder = ChatMediaHelper.BuildLoadingPlaceholder(fileName);
                    stack.Children.Add(placeholder);

                    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            byte[] data = _remoteService.TaiTapTin(fileName);
                            if (data != null && data.Length > 0)
                            {
                                string cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChatCache");
                                if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
                                string cachedFile = Path.Combine(cacheDir, fileName);
                                File.WriteAllBytes(cachedFile, data);

                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    int idx = stack.Children.IndexOf(placeholder);
                                    if (idx >= 0)
                                    {
                                        stack.Children.RemoveAt(idx);
                                        var preview = ChatMediaHelper.BuildMediaPreview(cachedFile, fileName, () => DownloadFile(fileName));
                                        stack.Children.Insert(idx, preview);
                                    }
                                }));
                            }
                        }
                        catch { }
                    });
                }
            }

            bubble.Child = stack;
            ChatMessagesPanel.Children.Add(bubble);
        }

        // ═══════════ HELPERS ═══════════

        private string GetSelectedOperation()
        {
            if (OpAdd.IsChecked == true) return "+";
            if (OpSub.IsChecked == true) return "-";
            if (OpMul.IsChecked == true) return "*";
            if (OpDiv.IsChecked == true) return "/";
            return "+";
        }

        private bool ValidateConnection()
        {
            if (!_isConnected || _remoteService == null)
            {
                ShowError("Chưa kết nối tới Server!\nVui lòng kết nối trước khi thao tác.");
                return false;
            }
            return true;
        }

        private void CheckConnectionLost()
        {
            var result = MessageBox.Show(
                "Kết nối với Server có thể đã bị mất.\nBạn có muốn ngắt kết nối?",
                "Cảnh báo",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                Disconnect();
            }
        }

        private void ShowError(string message)
        {
            MessageBox.Show(message, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // ═══════════ UI STATE ═══════════

        private void UpdateConnectionUI(bool connected)
        {
            if (connected)
            {
                ConnDotColor.Color = (Color)ColorConverter.ConvertFromString("#00E676");
                ConnTextColor.Color = (Color)ColorConverter.ConvertFromString("#00E676");
                ConnBadgeBg.Color = (Color)ColorConverter.ConvertFromString("#0A2010");
                ConnBadgeBorder.Color = (Color)ColorConverter.ConvertFromString("#1A4A20");
                ConnStatusText.Text = "Đã kết nối";

                ConnectBtn.Content = "NGẮT KẾT NỐI";
                ConnectBtn.Background = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString("#EF4444"),
                    (Color)ColorConverter.ConvertFromString("#FF5252"), 0);
                ConnectBtn.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 14, Opacity = 0.5, ShadowDepth = 0,
                    Color = (Color)ColorConverter.ConvertFromString("#EF4444")
                };
                ServerUrlTextBox.IsEnabled = false;

                SendGreetingBtn.IsEnabled = true;
                CalculateBtn.IsEnabled = true;
                SendChatBtn.IsEnabled = true;

                if (ChooseFileBtn != null) ChooseFileBtn.IsEnabled = true;
                if (UploadFileBtn != null) UploadFileBtn.IsEnabled = !string.IsNullOrEmpty(_selectedFilePath);
                if (RefreshFilesBtn != null) RefreshFilesBtn.IsEnabled = true;

                ChatOnlineIndicator.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));
                ChatStatusText.Text = "Đang trực tuyến";
                ChatStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));

                var pulse = new DoubleAnimation
                {
                    From = 1.0, To = 0.3,
                    Duration = TimeSpan.FromMilliseconds(800),
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase()
                };
                ConnStatusDot.BeginAnimation(OpacityProperty, pulse);
            }
            else
            {
                ConnDotColor.Color = (Color)ColorConverter.ConvertFromString("#FF5252");
                ConnTextColor.Color = (Color)ColorConverter.ConvertFromString("#FF5252");
                ConnBadgeBg.Color = (Color)ColorConverter.ConvertFromString("#1A0D0D");
                ConnBadgeBorder.Color = (Color)ColorConverter.ConvertFromString("#4A1515");
                ConnStatusText.Text = "Chưa kết nối";

                ConnectBtn.Content = "KẾT NỐI";
                ConnectBtn.Background = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString("#3A8DFF"),
                    (Color)ColorConverter.ConvertFromString("#00C6FF"), 0);
                ConnectBtn.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 14, Opacity = 0.5, ShadowDepth = 0,
                    Color = (Color)ColorConverter.ConvertFromString("#3A8DFF")
                };
                ServerUrlTextBox.IsEnabled = true;

                SendGreetingBtn.IsEnabled = false;
                CalculateBtn.IsEnabled = false;
                SendChatBtn.IsEnabled = false;

                if (ChooseFileBtn != null) ChooseFileBtn.IsEnabled = false;
                if (UploadFileBtn != null) UploadFileBtn.IsEnabled = false;
                if (RefreshFilesBtn != null) RefreshFilesBtn.IsEnabled = false;
                _selectedFilePath = null;
                if (SelectedFileText != null)
                {
                    SelectedFileText.Text = "Chưa chọn tệp tin nào (hỗ trợ mọi định dạng tệp...)";
                    SelectedFileText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5A7BA8"));
                }
                if (ClientFileListPanel != null)
                {
                    ClientFileListPanel.Children.Clear();
                    if (ClientFilesEmptyHint != null)
                    {
                        ClientFilesEmptyHint.Text = "Chưa có tệp tin nào. Kết nối tới Server và bấm 'Làm mới danh sách'!";
                        ClientFileListPanel.Children.Add(ClientFilesEmptyHint);
                    }
                }

                ChatOnlineIndicator.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5252"));
                ChatStatusText.Text = "Chưa kết nối";
                ChatStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8892A4"));

                ConnStatusDot.BeginAnimation(OpacityProperty, null);
                ConnStatusDot.Opacity = 1.0;
            }
        }

        // ═══════════ TRUYỀN TỆP TIN ═══════════

        private void ChooseFileBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Chọn tệp tin để gửi lên Server",
                Filter = "Mọi tệp tin (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                _selectedFilePath = dlg.FileName;
                var fi = new FileInfo(_selectedFilePath);
                SelectedFileText.Text = $"{fi.Name} ({RemoteFileInfo.FormatBytes(fi.Length)})";
                SelectedFileText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));
                UploadFileBtn.IsEnabled = _isConnected;
            }
        }

        private void UploadFileBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateConnection()) return;

            if (string.IsNullOrEmpty(_selectedFilePath) || !File.Exists(_selectedFilePath))
            {
                ShowError("Vui lòng chọn một tệp tin hợp lệ trước.");
                return;
            }

            try
            {
                var fi = new FileInfo(_selectedFilePath);
                if (fi.Length > 50 * 1024 * 1024)
                {
                    ShowError("Kích thước tệp quá lớn (> 50MB). Vui lòng chọn tệp nhỏ hơn.");
                    return;
                }

                byte[] fileData = File.ReadAllBytes(_selectedFilePath);
                string senderName = ChatSenderTextBox.Text.Trim();
                if (string.IsNullOrEmpty(senderName))
                    senderName = "Client";

                UploadFileBtn.IsEnabled = false;
                bool success = _remoteService.GuiTapTin(senderName, fi.Name, fileData);

                if (success)
                {
                    MessageBox.Show($"Đã gửi tệp '{fi.Name}' lên Server thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    _selectedFilePath = null;
                    SelectedFileText.Text = "Chưa chọn tệp tin nào (hỗ trợ mọi định dạng tệp...)";
                    SelectedFileText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5A7BA8"));
                    UploadFileBtn.IsEnabled = false;

                    LoadClientFileList();
                }
                else
                {
                    ShowError("Server không thể lưu tệp tin này.");
                    UploadFileBtn.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                UploadFileBtn.IsEnabled = _isConnected && !string.IsNullOrEmpty(_selectedFilePath);
                ShowError("Lỗi khi gửi tệp lên Server: " + ex.Message);
            }
        }

        private void RefreshFilesBtn_Click(object sender, RoutedEventArgs e)
        {
            LoadClientFileList();
        }

        private void OpenClientDownloadsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Directory.Exists(_clientDownloadDir))
                {
                    Directory.CreateDirectory(_clientDownloadDir);
                }
                System.Diagnostics.Process.Start("explorer.exe", _clientDownloadDir);
            }
            catch (Exception ex)
            {
                ShowError("Không thể mở thư mục tải về: " + ex.Message);
            }
        }

        private void LoadClientFileList()
        {
            if (!_isConnected || _remoteService == null) return;

            try
            {
                var files = _remoteService.LayDanhSachTapTin();
                _lastFileCount = files != null ? files.Count : 0;
                ClientFileListPanel.Children.Clear();

                if (files == null || files.Count == 0)
                {
                    ClientFileListPanel.Children.Add(new TextBlock
                    {
                        Text = "Chưa có tệp tin nào trên Server. Bạn có thể gửi tệp tin đầu tiên!",
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4A6A8A")),
                        FontStyle = FontStyles.Italic,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 30, 0, 0),
                        FontSize = 13
                    });
                    return;
                }

                foreach (var file in files)
                {
                    var itemBorder = new Border
                    {
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0D1829")),
                        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E2D4A")),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(16, 12, 16, 12),
                        Margin = new Thickness(0, 0, 0, 8)
                    };

                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    // Icon
                    var vb = new Viewbox { Width = 20, Height = 20, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                    var path = new System.Windows.Shapes.Path
                    {
                        Data = (Geometry)FindResource("FolderIcon"),
                        Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A8DFF")),
                        Stretch = Stretch.Uniform
                    };
                    vb.Child = path;
                    Grid.SetColumn(vb, 0);
                    grid.Children.Add(vb);

                    // File info
                    var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    var nameText = new TextBlock
                    {
                        Text = file.FileName,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 13.5,
                        Foreground = Brushes.White,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    var metaText = new TextBlock
                    {
                        Text = $"{file.FormattedSize}  •  Người gửi: {file.Sender}  •  {file.FormattedTime}",
                        FontSize = 11,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8892A4")),
                        Margin = new Thickness(0, 2, 0, 0)
                    };
                    infoPanel.Children.Add(nameText);
                    infoPanel.Children.Add(metaText);
                    Grid.SetColumn(infoPanel, 1);
                    grid.Children.Add(infoPanel);

                    // Download Button
                    var dlBtn = new Button
                    {
                        Style = (Style)FindResource("SecondaryBtn"),
                        Padding = new Thickness(14, 7, 14, 7),
                        VerticalAlignment = VerticalAlignment.Center,
                        Cursor = Cursors.Hand
                    };
                    var dlStack = new StackPanel { Orientation = Orientation.Horizontal };
                    var dlVb = new Viewbox { Width = 12, Height = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                    var dlPath = new System.Windows.Shapes.Path
                    {
                        Data = (Geometry)FindResource("DownloadIcon"),
                        Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676")),
                        Stretch = Stretch.Uniform
                    };
                    dlVb.Child = dlPath;
                    dlStack.Children.Add(dlVb);
                    dlStack.Children.Add(new TextBlock
                    {
                        Text = "Tải về máy",
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676")),
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    dlBtn.Content = dlStack;

                    string currentFileName = file.FileName;
                    dlBtn.Click += (s, args) => DownloadFile(currentFileName);

                    Grid.SetColumn(dlBtn, 2);
                    grid.Children.Add(dlBtn);

                    itemBorder.Child = grid;
                    ClientFileListPanel.Children.Add(itemBorder);
                }
            }
            catch (Exception ex)
            {
                ShowError("Lỗi khi tải danh sách tệp từ Server: " + ex.Message);
            }
        }

        private void DownloadFile(string fileName)
        {
            if (!ValidateConnection()) return;

            try
            {
                byte[] data = _remoteService.TaiTapTin(fileName);
                if (data == null || data.Length == 0)
                {
                    ShowError($"Không thể tải tệp '{fileName}' từ Server.");
                    return;
                }

                if (!Directory.Exists(_clientDownloadDir))
                {
                    Directory.CreateDirectory(_clientDownloadDir);
                }

                string savePath = Path.Combine(_clientDownloadDir, fileName);
                File.WriteAllBytes(savePath, data);

                var res = MessageBox.Show($"Đã tải tệp về thành công:\n{savePath}\n\nBạn có muốn mở thư mục chứa tệp không?",
                    "Tải về thành công", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (res == MessageBoxResult.Yes)
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{savePath}\"");
                }
            }
            catch (Exception ex)
            {
                ShowError("Lỗi khi tải tệp: " + ex.Message);
            }
        }

        private void AnimateResultAppear(TextBlock target)
        {
            target.Opacity = 0;
            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            target.BeginAnimation(OpacityProperty, fadeIn);
        }

        protected override void OnClosed(EventArgs e)
        {
            _chatTimer?.Stop();
            _clockTimer?.Stop();
            base.OnClosed(e);
        }
    }
}
