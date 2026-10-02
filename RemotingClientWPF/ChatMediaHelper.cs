using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace RemotingClientWPF
{
    public enum ChatMediaType
    {
        None,
        Image,
        Gif,
        Audio,
        Video,
        OtherFile
    }

    public static class ChatMediaHelper
    {
        public static ChatMediaType DetectMediaType(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return ChatMediaType.None;
            string ext = Path.GetExtension(fileName).ToLowerInvariant();

            switch (ext)
            {
                case ".gif":
                    return ChatMediaType.Gif;

                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".bmp":
                case ".webp":
                case ".ico":
                    return ChatMediaType.Image;

                case ".mp3":
                case ".wav":
                case ".m4a":
                case ".wma":
                case ".aac":
                case ".flac":
                case ".ogg":
                    return ChatMediaType.Audio;

                case ".mp4":
                case ".wmv":
                case ".avi":
                case ".mov":
                case ".mkv":
                case ".webm":
                case ".3gp":
                    return ChatMediaType.Video;

                default:
                    if (!string.IsNullOrEmpty(ext) && ext.Length >= 2)
                        return ChatMediaType.OtherFile;
                    return ChatMediaType.None;
            }
        }

        public static bool TryExtractFileName(string text, out string fileName)
        {
            fileName = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            // Dạng 1: Đã gửi tệp: filename.ext (xx KB)
            if (text.Contains("Đã gửi tệp:"))
            {
                int start = text.IndexOf("Đã gửi tệp:") + "Đã gửi tệp:".Length;
                string part = text.Substring(start).Trim();
                int paren = part.LastIndexOf('(');
                if (paren > 0)
                {
                    part = part.Substring(0, paren).Trim();
                }
                fileName = Path.GetFileName(part);
                return !string.IsNullOrEmpty(fileName);
            }

            // Dạng 2: [FILE:filename.ext] hoặc [MEDIA:filename.ext]
            var matchTag = Regex.Match(text, @"\[(FILE|MEDIA):\s*([^\]]+)\]", RegexOptions.IgnoreCase);
            if (matchTag.Success)
            {
                fileName = Path.GetFileName(matchTag.Groups[2].Value.Trim());
                return true;
            }

            // Dạng 3: Chuỗi chứa tên file có phần mở rộng media phổ biến
            var matchFile = Regex.Match(text, @"([a-zA-Z0-9_\-\.\s\(\)]+\.(gif|png|jpg|jpeg|bmp|webp|mp3|wav|m4a|wma|aac|mp4|wmv|avi|mov|mkv))", RegexOptions.IgnoreCase);
            if (matchFile.Success)
            {
                fileName = Path.GetFileName(matchFile.Groups[1].Value.Trim());
                return true;
            }

            return false;
        }

        public static FrameworkElement BuildMediaPreview(string filePath, string fileName, Action onOpenOrSave = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return BuildPlaceholder("Không tìm thấy file trên đĩa để xem trước.", fileName, onOpenOrSave);
            }

            var type = DetectMediaType(fileName);
            switch (type)
            {
                case ChatMediaType.Image:
                    return BuildImagePreview(filePath, fileName, onOpenOrSave);
                case ChatMediaType.Gif:
                    return BuildGifPreview(filePath, fileName, onOpenOrSave);
                case ChatMediaType.Audio:
                    return BuildAudioPreview(filePath, fileName, onOpenOrSave);
                case ChatMediaType.Video:
                    return BuildVideoPreview(filePath, fileName, onOpenOrSave);
                default:
                    return BuildGenericFilePreview(filePath, fileName, onOpenOrSave);
            }
        }

        public static FrameworkElement BuildLoadingPlaceholder(string fileName)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0B1322")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E365E")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 8, 0, 4)
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = $"Đang tải bản xem trước ({fileName})…",
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#93C5FD")),
                VerticalAlignment = VerticalAlignment.Center
            });

            border.Child = sp;
            return border;
        }

        private static FrameworkElement BuildPlaceholder(string message, string fileName, Action onOpenOrSave)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0B1322")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A3E62")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 8, 0, 4)
            };

            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = fileName,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                FontSize = 12
            });
            sp.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 11,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                Margin = new Thickness(0, 2, 0, 0)
            });

            border.Child = sp;
            return border;
        }

        // ═══════════ IMAGE PREVIEW ═══════════

        private static FrameworkElement BuildImagePreview(string filePath, string fileName, Action onOpenOrSave)
        {
            var container = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#090F1B")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#203864")),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 8, 0, 4),
                ClipToBounds = true,
                MaxWidth = 360,
                Cursor = Cursors.Hand
            };

            var stack = new StackPanel();

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                var img = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    MaxHeight = 260,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                stack.Children.Add(img);
            }
            catch
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "Không thể hiển thị ảnh.",
                    Foreground = Brushes.Tomato,
                    Padding = new Thickness(12)
                });
            }

            // Bottom bar với tên tệp và nút mở
            var bottomGrid = new Grid();
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleText = new TextBlock
            {
                Text = fileName,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D0E0FF")),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleText, 0);
            bottomGrid.Children.Add(titleText);

            var openBtn = new Button
            {
                Content = "Mở ảnh",
                FontSize = 10,
                Padding = new Thickness(8, 2, 8, 2),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3258")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            openBtn.Click += (s, e) =>
            {
                e.Handled = true;
                OpenFileExternal(filePath);
            };
            Grid.SetColumn(openBtn, 1);
            bottomGrid.Children.Add(openBtn);

            var bottomBar = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0E192D")),
                Padding = new Thickness(10, 6, 10, 6),
                Child = bottomGrid
            };
            stack.Children.Add(bottomBar);
            container.Child = stack;

            container.MouseLeftButtonUp += (s, e) => OpenFileExternal(filePath);
            container.ToolTip = "Bấm để mở ảnh bằng trình xem của máy";

            return container;
        }

        // ═══════════ GIF PREVIEW ═══════════

        private static FrameworkElement BuildGifPreview(string filePath, string fileName, Action onOpenOrSave)
        {
            var container = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#090F1B")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED")),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 8, 0, 4),
                ClipToBounds = true,
                MaxWidth = 340,
                Cursor = Cursors.Hand
            };

            var stack = new StackPanel();

            try
            {
                var media = new MediaElement
                {
                    Source = new Uri(filePath, UriKind.Absolute),
                    LoadedBehavior = MediaState.Play,
                    UnloadedBehavior = MediaState.Manual,
                    Stretch = Stretch.Uniform,
                    MaxHeight = 240,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                media.MediaEnded += (s, e) =>
                {
                    media.Position = TimeSpan.Zero;
                    media.Play();
                };

                stack.Children.Add(media);
            }
            catch
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "Không thể chạy GIF.",
                    Foreground = Brushes.Tomato,
                    Padding = new Thickness(12)
                });
            }

            var bottomGrid = new Grid();
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleText = new TextBlock
            {
                Text = $"GIF: {fileName}",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E9D5FF")),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleText, 0);
            bottomGrid.Children.Add(titleText);

            var openBtn = new Button
            {
                Content = "Mở tệp",
                FontSize = 10,
                Padding = new Thickness(8, 2, 8, 2),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#371E68")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            openBtn.Click += (s, e) =>
            {
                e.Handled = true;
                OpenFileExternal(filePath);
            };
            Grid.SetColumn(openBtn, 1);
            bottomGrid.Children.Add(openBtn);

            var bottomBar = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#15112B")),
                Padding = new Thickness(10, 6, 10, 6),
                Child = bottomGrid
            };
            stack.Children.Add(bottomBar);
            container.Child = stack;

            container.MouseLeftButtonUp += (s, e) => OpenFileExternal(filePath);
            return container;
        }

        // ═══════════ AUDIO PREVIEW (MP3 / WAV) ═══════════

        private static FrameworkElement BuildAudioPreview(string filePath, string fileName, Action onOpenOrSave)
        {
            var container = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0C1527")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00C853")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 8, 0, 4),
                Width = 320
            };

            var player = new MediaPlayer();
            player.Open(new Uri(filePath, UriKind.Absolute));

            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var playBtn = new Button
            {
                Width = 42,
                Height = 36,
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00C853")),
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Content = "Phát",
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 10, 0)
            };

            var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock
            {
                Text = fileName,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var statusText = new TextBlock
            {
                Text = "Âm thanh · Nhấn để phát",
                FontSize = 10.5,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
                Margin = new Thickness(0, 2, 0, 0)
            };
            infoStack.Children.Add(title);
            infoStack.Children.Add(statusText);

            var openBtn = new Button
            {
                Content = "Mở",
                FontSize = 10.5,
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#192A48")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            openBtn.Click += (s, e) => OpenFileExternal(filePath);

            bool isPlaying = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (s, e) =>
            {
                if (player.NaturalDuration.HasTimeSpan)
                {
                    var pos = player.Position;
                    var dur = player.NaturalDuration.TimeSpan;
                    statusText.Text = $"{pos:mm\\:ss} / {dur:mm\\:ss}";
                }
            };

            playBtn.Click += (s, e) =>
            {
                if (!isPlaying)
                {
                    player.Play();
                    isPlaying = true;
                    playBtn.Content = "Dừng";
                    timer.Start();
                }
                else
                {
                    player.Pause();
                    isPlaying = false;
                    playBtn.Content = "Phát";
                    timer.Stop();
                }
            };

            player.MediaEnded += (s, e) =>
            {
                isPlaying = false;
                playBtn.Content = "Phát";
                player.Position = TimeSpan.Zero;
                timer.Stop();
                statusText.Text = "Đã kết thúc · Nhấn để phát lại";
            };

            Grid.SetColumn(playBtn, 0);
            Grid.SetColumn(infoStack, 1);
            Grid.SetColumn(openBtn, 2);

            mainGrid.Children.Add(playBtn);
            mainGrid.Children.Add(infoStack);
            mainGrid.Children.Add(openBtn);

            container.Child = mainGrid;
            return container;
        }

        // ═══════════ VIDEO PREVIEW (MP4 / WMV / AVI) ═══════════

        private static FrameworkElement BuildVideoPreview(string filePath, string fileName, Action onOpenOrSave)
        {
            var container = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#070E1A")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 8, 0, 4),
                ClipToBounds = true,
                MaxWidth = 360
            };

            var stack = new StackPanel();

            var media = new MediaElement
            {
                Source = new Uri(filePath, UriKind.Absolute),
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Stop,
                Stretch = Stretch.Uniform,
                MaxHeight = 200,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(media);

            // Controls bar
            var controlGrid = new Grid();
            controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var playBtn = new Button
            {
                Content = "Phát",
                FontSize = 11,
                Padding = new Thickness(10, 3, 10, 3),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var titleText = new TextBlock
            {
                Text = fileName,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };

            var openBtn = new Button
            {
                Content = "Toàn màn hình",
                FontSize = 10,
                Padding = new Thickness(8, 3, 8, 3),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#17294B")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            openBtn.Click += (s, e) => OpenFileExternal(filePath);

            bool isPlaying = false;
            playBtn.Click += (s, e) =>
            {
                if (!isPlaying)
                {
                    media.Play();
                    isPlaying = true;
                    playBtn.Content = "Tạm dừng";
                }
                else
                {
                    media.Pause();
                    isPlaying = false;
                    playBtn.Content = "Phát";
                }
            };

            media.MediaEnded += (s, e) =>
            {
                isPlaying = false;
                playBtn.Content = "Phát lại";
                media.Position = TimeSpan.Zero;
            };

            Grid.SetColumn(playBtn, 0);
            Grid.SetColumn(titleText, 1);
            Grid.SetColumn(openBtn, 2);

            controlGrid.Children.Add(playBtn);
            controlGrid.Children.Add(titleText);
            controlGrid.Children.Add(openBtn);

            var controlBar = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0E1B32")),
                Padding = new Thickness(10, 6, 10, 6),
                Child = controlGrid
            };
            stack.Children.Add(controlBar);
            container.Child = stack;

            return container;
        }

        // ═══════════ GENERIC FILE PREVIEW ═══════════

        private static FrameworkElement BuildGenericFilePreview(string filePath, string fileName, Action onOpenOrSave)
        {
            var container = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0C1527")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#223B64")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 6, 0, 2),
                Width = 280
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconBadge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#15243E")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#264573")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Child = new TextBlock
                {
                    Text = "TỆP",
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4A9FFF"))
                }
            };

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var nameText = new TextBlock
            {
                Text = fileName,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            string sizeStr = "Tệp tin";
            try
            {
                if (File.Exists(filePath))
                {
                    long len = new FileInfo(filePath).Length;
                    sizeStr = len < 1024 ? $"{len} B" : (len < 1024 * 1024 ? $"{len / 1024.0:F1} KB" : $"{len / (1024.0 * 1024.0):F2} MB");
                }
            }
            catch { }

            var sizeText = new TextBlock
            {
                Text = sizeStr,
                FontSize = 10,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"))
            };
            stack.Children.Add(nameText);
            stack.Children.Add(sizeText);

            var openBtn = new Button
            {
                Content = "Mở tệp",
                FontSize = 10.5,
                Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D3358")),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            openBtn.Click += (s, e) => OpenFileExternal(filePath);

            Grid.SetColumn(iconBadge, 0);
            Grid.SetColumn(stack, 1);
            Grid.SetColumn(openBtn, 2);

            grid.Children.Add(iconBadge);
            grid.Children.Add(stack);
            grid.Children.Add(openBtn);

            container.Child = grid;
            return container;
        }

        private static void OpenFileExternal(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath)
                    {
                        UseShellExecute = true
                    });
                }
            }
            catch { }
        }
    }
}
