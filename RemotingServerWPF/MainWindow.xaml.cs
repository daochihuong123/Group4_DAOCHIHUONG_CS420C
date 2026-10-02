using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ASP.Net_Remote;

namespace RemotingServerWPF
{
    public partial class MainWindow : Window
    {
        private TcpServerChannel _channel;
        private bool _isRunning = false;
        private DateTime _startTime;
        private DispatcherTimer _uptimeTimer;
        private DispatcherTimer _clockTimer;
        private int _requestCount = 0;
        private int _chatCount = 0;
        private string _serverSelectedFilePath;

        public MainWindow()
        {
            InitializeComponent();

            // Timer cập nhật uptime
            _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _uptimeTimer.Tick += UptimeTimer_Tick;

            // Timer cập nhật đồng hồ
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, e) => ClockText.Text = DateTime.Now.ToString("HH:mm:ss  dd/MM/yyyy");
            _clockTimer.Start();
            ClockText.Text = DateTime.Now.ToString("HH:mm:ss  dd/MM/yyyy");

            // Đăng ký nhận sự kiện Chat từ CalculatorService
            CalculatorService.MessageReceived += OnChatMessageReceived;
            // Đăng ký nhận sự kiện File từ CalculatorService
            CalculatorService.FileReceived += OnFileReceived;

            AppendLog("Server đã sẵn sàng. Nhấn 'KHỞI ĐỘNG' để bắt đầu lắng nghe kết nối.");
            DetectAndShowIp();
            LoadServerFileList();
            LoadExistingChatMessages();

            LoadServerSettings();
            this.Closing += (s, e) => SaveServerSettings();
        }

        // ═══════════ CẤU HÌNH SERVER ═══════════

        private bool _isLoadingServerSettings = true;
        private static string ServerSettingsFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_settings.json");

        private void LoadServerSettings()
        {
            try
            {
                string path = ServerSettingsFilePath;
                if (File.Exists(path))
                {
                    string content = File.ReadAllText(path);
                    if (content.IndexOf("\"AutoStart\": true", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        ServerAutoStartCheckBox.IsChecked = true;
                        var autoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                        autoTimer.Tick += (s, e) =>
                        {
                            autoTimer.Stop();
                            if (!_isRunning)
                            {
                                AppendLog("Tự động khởi động Server theo cấu hình đã lưu…");
                                StartServer();
                            }
                        };
                        autoTimer.Start();
                    }
                }
            }
            catch { }
            _isLoadingServerSettings = false;
        }

        private void SaveServerSettings()
        {
            if (_isLoadingServerSettings) return;
            try
            {
                bool autoStart = ServerAutoStartCheckBox.IsChecked == true;
                string json = "{\n  \"AutoStart\": " + (autoStart ? "true" : "false") + "\n}";
                File.WriteAllText(ServerSettingsFilePath, json, Encoding.UTF8);
            }
            catch { }
        }

        private void ServerAutoStartCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            SaveServerSettings();
        }

        // ═══════════ IP LAN DETECTION ═══════════

        // ═══════════ IP MẠNG & RADMIN VPN DETECTION ═══════════

        public class NetworkInterfaceOption
        {
            public string Ip { get; set; }
            public string Name { get; set; }
            public bool IsRadmin { get; set; }

            public override string ToString()
            {
                return $"{Ip}  ({Name})";
            }
        }

        private void DetectAndShowIp()
        {
            var ipList = GetAllNetworkIps();
            ServerIpComboBox.Items.Clear();

            int selectedIndex = 0;
            for (int i = 0; i < ipList.Count; i++)
            {
                var opt = ipList[i];
                ServerIpComboBox.Items.Add(opt);

                // Tự động ưu tiên chọn Radmin VPN (đầu số 26.x) nếu có để hỗ trợ kết nối khác mạng
                if (opt.IsRadmin)
                {
                    selectedIndex = i;
                }
            }

            if (ServerIpComboBox.Items.Count > 0)
            {
                ServerIpComboBox.SelectedIndex = selectedIndex;
            }
        }

        private List<NetworkInterfaceOption> GetAllNetworkIps()
        {
            var list = new List<NetworkInterfaceOption>();
            try
            {
                foreach (var net in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (net.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (net.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

                    var props = net.GetIPProperties();
                    foreach (var unicast in props.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            string ipStr = unicast.Address.ToString();
                            if (ipStr.StartsWith("169.254.")) continue; // Bỏ qua IP tự động gán APIPA không định tuyến được

                            string netName = net.Name;
                            bool isRadmin = netName.IndexOf("radmin", StringComparison.OrdinalIgnoreCase) >= 0
                                         || (net.Description ?? "").IndexOf("radmin", StringComparison.OrdinalIgnoreCase) >= 0
                                         || ipStr.StartsWith("26.");

                            if (isRadmin) netName = "Radmin VPN (Khác mạng)";

                            list.Add(new NetworkInterfaceOption
                            {
                                Ip = ipStr,
                                Name = netName,
                                IsRadmin = isRadmin
                            });
                        }
                    }
                }
            }
            catch { }

            // Thêm tùy chọn cục bộ
            list.Add(new NetworkInterfaceOption { Ip = "127.0.0.1", Name = "Cục bộ / Localhost", IsRadmin = false });
            return list;
        }

        private string GetSelectedServerIp()
        {
            if (ServerIpComboBox.SelectedItem is NetworkInterfaceOption opt)
            {
                return opt.Ip;
            }
            return "127.0.0.1";
        }

        private void ServerIpComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerIpComboBox.SelectedItem is NetworkInterfaceOption opt)
            {
                string ip = opt.Ip;
                ConnectionUrlText.Text = $"tcp://{ip}:8085/CalculatorServiceURI";
                AppendLog($"Đã chọn giao diện mạng: {ip} ({opt.Name})");
                AppendLog($"URL kết nối Client tương ứng: tcp://{ip}:8085/CalculatorServiceURI");
                if (_isRunning)
                {
                    AppendLog("Lưu ý: Nếu đổi mạng khi Server đang chạy, vui lòng bấm 'DỪNG SERVER' rồi 'KHỞI ĐỘNG' lại để áp dụng IP mới.");
                }
            }
        }

        private void CopyUrlBtn_Click(object sender, RoutedEventArgs e)
        {
            string url = ConnectionUrlText.Text;
            Clipboard.SetText(url);

            // Phản hồi nút copy
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var tb = new TextBlock
            {
                Text = "Đã sao chép!",
                Foreground = Brushes.White,
                FontSize = 12, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            sp.Children.Add(tb);
            CopyUrlBtn.Content = sp;

            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            t.Tick += (s2, e2) =>
            {
                var sp2 = new StackPanel { Orientation = Orientation.Horizontal };
                var vb = new Viewbox { Width = 13, Height = 13, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                var path = new System.Windows.Shapes.Path
                {
                    Fill = Brushes.White,
                    Stretch = Stretch.Uniform,
                    Data = (Geometry)FindResource("ClipboardIcon")
                };
                vb.Child = path;
                sp2.Children.Add(vb);
                sp2.Children.Add(new TextBlock
                {
                    Text = "Sao chép",
                    Foreground = Brushes.White,
                    FontSize = 12, FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                });
                CopyUrlBtn.Content = sp2;
                t.Stop();
            };
            t.Start();

            AppendLog($"URL đã được sao chép: {url}");
        }

        // ═══════════ START / STOP ═══════════

        private void StartStopBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_isRunning)
                StartServer();
            else
                StopServer();
        }

        private void StartServer()
        {
            try
            {
                int port = 8085;

                // Tự động mở firewall (quyền Admin từ app.manifest)
                EnsureFirewallRule(port);

                string selectedIp = GetSelectedServerIp();

                var serverProv = new BinaryServerFormatterSinkProvider();
                serverProv.TypeFilterLevel = System.Runtime.Serialization.Formatters.TypeFilterLevel.Full;

                var props = new System.Collections.Hashtable();
                props["port"] = port;
                props["name"] = "tcpServer_" + port;
                props["useIpAddress"] = true; // Bắt buộc: Dùng IP trực tiếp trong URI proxy để Client qua VPN không bị lỗi tìm hostname
                props["rejectRemoteRequests"] = false;

                // Nếu có IP thực (Radmin VPN hoặc LAN/WiFi), gán machineName để ObjRef gửi cho client dùng đúng IP này
                if (!string.IsNullOrEmpty(selectedIp) && selectedIp != "127.0.0.1")
                {
                    props["machineName"] = selectedIp;
                }

                _channel = new TcpServerChannel(props, serverProv);
                ChannelServices.RegisterChannel(_channel, false);

                RemotingConfiguration.RegisterWellKnownServiceType(
                    typeof(CalculatorService),
                    "CalculatorServiceURI",
                    WellKnownObjectMode.SingleCall
                );

                // Bắt log Console từ CalculatorService
                Console.SetOut(new UITextWriter(this));

                _isRunning = true;
                _startTime = DateTime.Now;
                _requestCount = 0;
                RequestCountText.Text = "0";
                _uptimeTimer.Start();

                UpdateServerUI(true);

                AppendLog($"Server đã khởi động thành công trên cổng {port}");
                AppendLog($"Giao diện mạng phát sóng: {selectedIp}");
                AppendLog($"URL cho Client kết nối: tcp://{selectedIp}:{port}/CalculatorServiceURI");
                AppendLog("Hỗ trợ kết nối cùng mạng LAN, Wi-Fi và mạng ảo Radmin VPN / Hamachi / ZeroTier.");
            }
            catch (Exception ex)
            {
                AppendLog($"LỖI: {ex.Message}");
                MessageBox.Show(
                    "Lỗi khởi động server:\n" + ex.Message,
                    "Lỗi Server",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void EnsureFirewallRule(int port)
        {
            try
            {
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;

                // Mở port TCP 8085 cho mọi profile mạng (Domain, Private, Public - Radmin VPN mặc định là Public)
                RunNetsh($"advfirewall firewall delete rule name=\"RemotingServer_{port}\"");
                RunNetsh($"advfirewall firewall add rule " +
                         $"name=\"RemotingServer_{port}\" " +
                         $"protocol=TCP dir=in localport={port} action=allow " +
                         $"profile=any description=\".NET Remoting Server - port {port}\"");

                // Mở cho file exe thực thi
                RunNetsh($"advfirewall firewall delete rule name=\"RemotingServerApp\"");
                RunNetsh($"advfirewall firewall add rule " +
                         $"name=\"RemotingServerApp\" " +
                         $"dir=in action=allow program=\"{exePath}\" enable=yes " +
                         $"profile=any description=\".NET Remoting Server Application\"");

                AppendLog($"Firewall: Đã kích hoạt rule cho cổng {port}/TCP trên tất cả các mạng.");
            }
            catch (Exception ex)
            {
                AppendLog($"Cảnh báo Firewall: {ex.Message}");
            }
        }

        private void RunNetsh(string args)
        {
            using (var proc = new System.Diagnostics.Process())
            {
                proc.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                proc.Start();
                proc.WaitForExit(5000);
            }
        }

        private void StopServer()
        {
            try
            {
                if (_channel != null)
                {
                    ChannelServices.UnregisterChannel(_channel);
                    _channel = null;
                }

                _isRunning = false;
                _uptimeTimer.Stop();

                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });

                UpdateServerUI(false);
                AppendLog("Server đã dừng.");
            }
            catch (Exception ex)
            {
                AppendLog($"LỖI khi dừng: {ex.Message}");
            }
        }

        // ═══════════ CHAT TRỰC TIẾP ═══════════

        private void OnChatMessageReceived(ChatMessage msg)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnChatMessageReceived(msg)));
                return;
            }

            AddMessageToUI(msg);
        }

        private void ServerSendChatBtn_Click(object sender, RoutedEventArgs e)
        {
            SendServerMessage();
        }

        private void ServerChatInputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SendServerMessage();
                e.Handled = true;
            }
        }

        private void ServerChatAttachBtn_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Chọn ảnh, GIF, video, âm thanh hoặc tệp tin từ Server",
                Filter = "Tất cả media & tệp|*.*|Hình ảnh & GIF (*.png;*.jpg;*.gif)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|Âm thanh (*.mp3;*.wav)|*.mp3;*.wav;*.m4a;*.wma;*.aac|Video (*.mp4;*.wmv;*.avi)|*.mp4;*.wmv;*.avi;*.mov;*.mkv"
            };

            if (ofd.ShowDialog() == true)
            {
                try
                {
                    bool ok = CalculatorService.LuuTapTinTuServer(ofd.FileName);
                    if (ok)
                    {
                        AppendLog($"Đã gửi tệp media từ Server: {Path.GetFileName(ofd.FileName)}");
                        LoadServerFileList();
                    }
                    else
                    {
                        MessageBox.Show("Không thể gửi tệp từ Server.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ═══════════ KÉO THẢ TỆP & DÁN CLIPBOARD (CTRL+V) TRÊN SERVER ═══════════

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
                    HandleServerDroppedOrPastedFiles(files);
                }
                e.Handled = true;
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                var focused = Keyboard.FocusedElement as TextBox;
                bool isOtherTextBox = (focused != null && focused != ServerChatInputTextBox);

                // Ưu tiên 1: Dán danh sách tệp copy từ File Explorer (Ctrl+C ngoài thư mục -> Ctrl+V vào app)
                if (Clipboard.ContainsFileDropList())
                {
                    var fileList = Clipboard.GetFileDropList();
                    if (fileList != null && fileList.Count > 0)
                    {
                        var files = new string[fileList.Count];
                        fileList.CopyTo(files, 0);
                        HandleServerDroppedOrPastedFiles(files);
                        e.Handled = true;
                        return;
                    }
                }

                // Ưu tiên 2: Dán ảnh chụp màn hình (Snipping Tool / Win+Shift+S)
                if (Clipboard.ContainsImage() && !isOtherTextBox)
                {
                    PasteServerScreenshotAndSend();
                    e.Handled = true;
                    return;
                }
            }
        }

        private void HandleServerDroppedOrPastedFiles(string[] files)
        {
            if (files == null || files.Length == 0) return;

            int count = 0;
            foreach (var file in files)
            {
                if (File.Exists(file))
                {
                    bool ok = CalculatorService.LuuTapTinTuServer(file);
                    if (ok)
                    {
                        count++;
                        AppendLog($"Đã nhận tệp từ kéo thả/dán: {Path.GetFileName(file)}");
                    }
                }
            }

            if (count > 0)
            {
                LoadServerFileList();
            }
        }

        private void PasteServerScreenshotAndSend()
        {
            try
            {
                var img = Clipboard.GetImage();
                if (img != null)
                {
                    string tempFile = Path.Combine(Path.GetTempPath(), $"server_screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                    using (var fs = new FileStream(tempFile, FileMode.Create))
                    {
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
                        encoder.Save(fs);
                    }
                    CalculatorService.LuuTapTinTuServer(tempFile);
                    AppendLog("Đã dán ảnh chụp màn hình từ Clipboard và chia sẻ tới Client.");
                    LoadServerFileList();
                }
            }
            catch (Exception ex)
            {
                AppendLog("Không thể dán ảnh từ clipboard: " + ex.Message);
            }
        }

        private void ServerChatInputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Các phím tắt Ctrl+V đã được xử lý tập trung tại Window_PreviewKeyDown
        }

        private void SendServerMessage()
        {
            string content = ServerChatInputTextBox.Text.Trim();
            if (string.IsNullOrEmpty(content)) return;

            CalculatorService.GuiTinNhanTuServer(content);
            ServerChatInputTextBox.Text = "";
        }

        private void AddMessageToUI(ChatMessage msg)
        {
            ServerChatEmptyHint.Visibility = Visibility.Collapsed;

            var bubble = new Border
            {
                CornerRadius = msg.IsServer ? new CornerRadius(14, 14, 2, 14) : new CornerRadius(14, 14, 14, 2),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = msg.IsServer ? new Thickness(50, 4, 4, 6) : new Thickness(4, 4, 50, 6),
                HorizontalAlignment = msg.IsServer ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                MaxWidth = 520
            };

            var stack = new StackPanel();

            // Header
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
            else
            {
                senderText.Text = msg.Sender;
                senderText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60A5FA"));
                bubble.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#142A4A"));
                bubble.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E4B82"));
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

            // Nội dung
            var contentText = new TextBlock
            {
                Text = msg.Content,
                FontSize = 13.5,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19
            };
            stack.Children.Add(contentText);

            // Hiển thị preview đa phương tiện nếu có (Ảnh, GIF, Audio, Video)
            if (ChatMediaHelper.TryExtractFileName(msg.Content, out string fileName))
            {
                string storageDir = CalculatorService.LayThuMucLuuTru();
                string filePath = Path.Combine(storageDir, fileName);

                if (File.Exists(filePath))
                {
                    var preview = ChatMediaHelper.BuildMediaPreview(filePath, fileName, () => OpenFileExternal(filePath));
                    stack.Children.Add(preview);
                }
            }

            bubble.Child = stack;
            ServerChatMessagesPanel.Children.Add(bubble);

            _chatCount++;
            ServerChatBadge.Visibility = Visibility.Visible;
            ServerChatCount.Text = $"{_chatCount} tin";

            ServerChatScrollViewer.ScrollToEnd();
        }

        private void LoadExistingChatMessages()
        {
            try
            {
                var messages = CalculatorService.LayToanBoTinNhan();
                if (messages != null && messages.Count > 0)
                {
                    foreach (var msg in messages)
                    {
                        AddMessageToUI(msg);
                    }
                }
            }
            catch { }
        }

        private static void OpenFileExternal(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
                    {
                        UseShellExecute = true
                    });
                }
            }
            catch { }
        }

        // ═══════════ QUẢN LÝ TỆP TIN ═══════════

        private void OnFileReceived(RemoteFileInfo file)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnFileReceived(file)));
                return;
            }

            AppendLog($"[TỆP TIN] Đã nhận tệp: {file.FileName} ({file.FormattedSize}) từ {file.Sender}");
            LoadServerFileList();
        }

        private void ChooseFileServerBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Chọn tệp tin từ máy Server để gửi cho Client",
                Filter = "Mọi tệp tin (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                _serverSelectedFilePath = dlg.FileName;
                var fi = new System.IO.FileInfo(_serverSelectedFilePath);
                ServerSelectedFileText.Text = $"{fi.Name} ({RemoteFileInfo.FormatBytes(fi.Length)})";
                ServerSelectedFileText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));
                SendFileToClientBtn.IsEnabled = _isRunning;
            }
        }

        private void SendFileToClientBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_serverSelectedFilePath) || !System.IO.File.Exists(_serverSelectedFilePath))
            {
                MessageBox.Show("Vui lòng chọn một tệp tin hợp lệ trước.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                bool success = CalculatorService.LuuTapTinTuServer(_serverSelectedFilePath);
                if (success)
                {
                    var fi = new System.IO.FileInfo(_serverSelectedFilePath);
                    AppendLog($"[TỆP TIN] Đã gửi tệp: {fi.Name} ({RemoteFileInfo.FormatBytes(fi.Length)}) cho Client");
                    MessageBox.Show($"Đã gửi tệp '{fi.Name}' thành công!\nClient có thể tải về ngay lập tức.", "Gửi tệp thành công", MessageBoxButton.OK, MessageBoxImage.Information);

                    _serverSelectedFilePath = null;
                    ServerSelectedFileText.Text = "Chưa chọn tệp tin nào từ máy Server...";
                    ServerSelectedFileText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5A7BA8"));
                    SendFileToClientBtn.IsEnabled = false;

                    LoadServerFileList();
                }
                else
                {
                    MessageBox.Show("Không thể lưu tệp tin vào Server.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi gửi tệp: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshServerFilesBtn_Click(object sender, RoutedEventArgs e)
        {
            LoadServerFileList();
        }

        private void AddFileServerBtn_Click(object sender, RoutedEventArgs e)
        {
            ChooseFileServerBtn_Click(sender, e);
        }

        private void OpenStorageFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = CalculatorService.LayThuMucLuuTru();
                System.Diagnostics.Process.Start("explorer.exe", path);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể mở thư mục: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadServerFileList()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(LoadServerFileList));
                return;
            }

            try
            {
                var files = new CalculatorService().LayDanhSachTapTin();
                ServerFileListPanel.Children.Clear();

                if (files == null || files.Count == 0)
                {
                    ServerFilesEmptyHint.Visibility = Visibility.Visible;
                    ServerFileListPanel.Children.Add(ServerFilesEmptyHint);
                    ServerFilesCount.Text = "0 tệp";
                    return;
                }

                ServerFilesEmptyHint.Visibility = Visibility.Collapsed;
                ServerFilesCount.Text = $"{files.Count} tệp";

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
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    // Icon
                    var vb = new Viewbox { Width = 22, Height = 22, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
                    var path = new System.Windows.Shapes.Path
                    {
                        Data = (Geometry)FindResource("FolderIcon"),
                        Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A8DFF")),
                        Stretch = Stretch.Uniform
                    };
                    vb.Child = path;
                    Grid.SetColumn(vb, 0);
                    grid.Children.Add(vb);

                    // Name + Size
                    var spName = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    var tbName = new TextBlock
                    {
                        Text = file.FileName,
                        FontSize = 13.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.White
                    };
                    var tbDetails = new TextBlock
                    {
                        Text = $"Kích thước: {file.FormattedSize}  ·  Người gửi: {file.Sender}",
                        FontSize = 11,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8DB4D8")),
                        Margin = new Thickness(0, 3, 0, 0)
                    };
                    spName.Children.Add(tbName);
                    spName.Children.Add(tbDetails);
                    Grid.SetColumn(spName, 1);
                    grid.Children.Add(spName);

                    // Time
                    var tbTime = new TextBlock
                    {
                        Text = file.FormattedTime,
                        FontSize = 11,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5A7BA8")),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(16, 0, 16, 0)
                    };
                    Grid.SetColumn(tbTime, 2);
                    grid.Children.Add(tbTime);

                    // Open file button
                    var openBtn = new Button
                    {
                        Style = (Style)FindResource("PrimaryBtn"),
                        Content = "Mở tệp",
                        Padding = new Thickness(14, 7, 14, 7),
                        FontSize = 11.5,
                        Tag = file.FileName,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    openBtn.Click += (s, ev) =>
                    {
                        try
                        {
                            string target = System.IO.Path.Combine(CalculatorService.LayThuMucLuuTru(), (string)((Button)s).Tag);
                            if (File.Exists(target))
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
                            }
                            else
                            {
                                MessageBox.Show("Không tìm thấy tệp tin.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Lỗi khi mở tệp: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    };
                    Grid.SetColumn(openBtn, 3);
                    grid.Children.Add(openBtn);

                    itemBorder.Child = grid;
                    ServerFileListPanel.Children.Add(itemBorder);
                }
            }
            catch { }
        }

        // ═══════════ UI HELPERS ═══════════

        private void UpdateServerUI(bool running)
        {
            if (running)
            {
                StatusDotColor.Color = (Color)ColorConverter.ConvertFromString("#00E676");
                StatusDotGlow.Color = (Color)ColorConverter.ConvertFromString("#00E676");
                StatusTextColor.Color = (Color)ColorConverter.ConvertFromString("#00E676");
                StatusBadgeBg.Color = (Color)ColorConverter.ConvertFromString("#0A2010");
                StatusBadgeBorder.Color = (Color)ColorConverter.ConvertFromString("#1A4A20");
                StatusText.Text = "Online";

                StartStopBtn.Content = "DỪNG SERVER";
                StartStopBtn.Background = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString("#EF4444"),
                    (Color)ColorConverter.ConvertFromString("#FF5252"), 0);
                StartStopBtn.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 16, Opacity = 0.5, ShadowDepth = 0,
                    Color = (Color)ColorConverter.ConvertFromString("#EF4444")
                };
                if (SendFileToClientBtn != null) SendFileToClientBtn.IsEnabled = !string.IsNullOrEmpty(_serverSelectedFilePath);
                StartPulseAnimation();
            }
            else
            {
                StatusDotColor.Color = (Color)ColorConverter.ConvertFromString("#FF5252");
                StatusDotGlow.Color = (Color)ColorConverter.ConvertFromString("#FF5252");
                StatusTextColor.Color = (Color)ColorConverter.ConvertFromString("#FF5252");
                StatusBadgeBg.Color = (Color)ColorConverter.ConvertFromString("#1A0D0D");
                StatusBadgeBorder.Color = (Color)ColorConverter.ConvertFromString("#4A1515");
                StatusText.Text = "Offline";

                StartStopBtn.Content = "KHỞI ĐỘNG";
                StartStopBtn.Background = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString("#00C853"),
                    (Color)ColorConverter.ConvertFromString("#00E676"), 0);
                StartStopBtn.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 16, Opacity = 0.5, ShadowDepth = 0,
                    Color = (Color)ColorConverter.ConvertFromString("#00C853")
                };
                if (SendFileToClientBtn != null) SendFileToClientBtn.IsEnabled = false;
                StopPulseAnimation();
            }
        }

        public void AppendLog(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendLog(message)));
                return;
            }

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            LogTextBox.AppendText($"[{timestamp}]  {message}\r\n");
            LogTextBox.ScrollToEnd();

            if (message.Contains("[LOG SERVER]") || message.Contains("[CHAT]"))
            {
                _requestCount++;
                RequestCountText.Text = _requestCount.ToString();
            }

            int lineCount = LogTextBox.LineCount;
            LogCountBadge.Text = $"{lineCount} sự kiện";
        }

        private void ClearLogBtn_Click(object sender, RoutedEventArgs e)
        {
            LogTextBox.Clear();
            AppendLog("Log đã được xóa.");
        }

        private void UptimeTimer_Tick(object sender, EventArgs e)
        {
            TimeSpan uptime = DateTime.Now - _startTime;
            UptimeText.Text = uptime.ToString(@"hh\:mm\:ss");
        }

        private void StartPulseAnimation()
        {
            var animation = new DoubleAnimation
            {
                From = 1.0, To = 0.3,
                Duration = TimeSpan.FromMilliseconds(800),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase()
            };
            StatusDot.BeginAnimation(OpacityProperty, animation);
        }

        private void StopPulseAnimation()
        {
            StatusDot.BeginAnimation(OpacityProperty, null);
            StatusDot.Opacity = 1.0;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isRunning)
            {
                StopServer();
            }
            _clockTimer?.Stop();
        }
    }

    public class UITextWriter : TextWriter
    {
        private readonly MainWindow _window;

        public UITextWriter(MainWindow window)
        {
            _window = window;
        }

        public override void WriteLine(string value)
        {
            _window.AppendLog(value);
        }

        public override void Write(string value)
        {
            if (!string.IsNullOrEmpty(value))
                _window.AppendLog(value);
        }

        public override Encoding Encoding => Encoding.UTF8;
    }
}
