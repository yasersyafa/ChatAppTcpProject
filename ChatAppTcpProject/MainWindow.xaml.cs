using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ChatAppTcpProject
{
    public partial class MainWindow : Window
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cts;
        private bool _connected;
        private bool _handshakeSent;
        private string? _lastHost;
        private int _lastPort;
        private const int MaxFrameSize = 64 * 1024; // 64 KB safety limit

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // optional auto-connect
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_connected)
            {
                await DisconnectAsync();
            }
            else
            {
                await ConnectAsync();
            }
        }

        private async void ReconnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_connected) return;
            if (string.IsNullOrWhiteSpace(NicknameTextBox.Text))
            {
                AppendSystem("Enter nickname before reconnecting.");
                return;
            }
            await ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            string host = ServerHostTextBox.Text.Trim();
            if (!int.TryParse(ServerPortTextBox.Text, out int port))
            {
                AppendSystem("Invalid port.");
                return;
            }

            string nickname = NicknameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(nickname))
            {
                AppendSystem("Nickname required before connecting.");
                return;
            }

            _client = new TcpClient();
            _cts = new CancellationTokenSource();

            try
            {
                SetStatus("Connecting...", Colors.Orange);
                await _client.ConnectAsync(host, port);
                _stream = _client.GetStream();
                _connected = true;
                _handshakeSent = false;
                _lastHost = host;
                _lastPort = port;

                ConnectButton.Content = "Disconnect";
                ReconnectButton.IsEnabled = false;
                SendButton.IsEnabled = true;
                SetStatus("Connected", Colors.Green);
                AppendSystem($"Connected to {host}:{port}");

                await SendNicknameHandshakeAsync();

                _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                SetStatus("Failed", Colors.Red);
                AppendSystem($"Connect failed: {ex.Message}");
                await DisconnectAsync();
            }
        }

        private async Task SendNicknameHandshakeAsync()
        {
            if (_stream == null || _handshakeSent) return;

            string nick = NicknameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(nick)) return;

            var joinMsg = new ChatMessage
            {
                Type = "join",
                From = nick,
                Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            string json = JsonSerializer.Serialize(joinMsg);
            await SendFrameAsync(json);

            _handshakeSent = true;
            AppendSystem($"Handshake sent as '{nick}'");
        }

        private async Task DisconnectAsync()
        {
            try
            {
                _cts?.Cancel();
                _stream?.Close();
                _client?.Close();
            }
            catch { }
            finally
            {
                _connected = false;
                _handshakeSent = false;
                ConnectButton.Content = "Connect";
                SendButton.IsEnabled = false;
                ReconnectButton.IsEnabled = !string.IsNullOrWhiteSpace(_lastHost);
                SetStatus("Disconnected", Colors.Gray);
                AppendSystem("Disconnected.");
            }
            await Task.CompletedTask;
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            if (_stream == null) return;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    string frame = await ReadFrameAsync(_stream, token);
                    if (frame == string.Empty) break;

                    var msg = JsonSerializer.Deserialize<ChatMessage>(frame);
                    if (msg == null) continue;

                    Dispatcher.Invoke(() =>
                    {
                        switch (msg.Type)
                        {
                            case "sys":
                                AppendSystem(msg.Text ?? "");
                                break;

                            case "msg":
                                ChatList.Items.Add($"[{msg.From}] {msg.Text}");
                                ScrollToEnd();
                                break;

                            case "pm":
                                ChatList.Items.Add($"[PM from {msg.From}] {msg.Text}");
                                ScrollToEnd();
                                break;

                            default:
                                ChatList.Items.Add($"[Unknown] {frame}");
                                ScrollToEnd();
                                break;
                        }
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException ioEx)
            {
                Dispatcher.Invoke(() => AppendSystem($"Connection closed: {ioEx.Message}"));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendSystem($"Receive error: {ex.Message}"));
            }
            finally
            {
                Dispatcher.Invoke(async () => await DisconnectAsync());
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            await SendCurrentMessageAsync();
        }

        private async Task SendCurrentMessageAsync()
        {
            string text = MessageInput.Text.Trim();
            if (string.IsNullOrEmpty(text) || _stream == null || !_connected) return;

            try
            {
                string nick = NicknameTextBox.Text.Trim();

                ChatMessage msg;
                if (text.StartsWith("/w "))
                {
                    // Format: /w target pesan
                    var parts = text.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 3)
                    {
                        AppendSystem("Usage: /w <user> <message>");
                        return;
                    }

                    msg = new ChatMessage
                    {
                        Type = "pm",
                        From = nick,
                        To = parts[1],
                        Text = parts[2],
                        Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    };
                }
                else
                {
                    msg = new ChatMessage
                    {
                        Type = "msg",
                        From = nick,
                        Text = text,
                        Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    };
                }

                string json = JsonSerializer.Serialize(msg);
                await SendFrameAsync(json);

                ChatList.Items.Add($"[You] {text}");
                ScrollToEnd();
                MessageInput.Clear();
                MessageInput.Focus();
            }
            catch (Exception ex)
            {
                AppendSystem($"Send failed: {ex.Message}");
                await DisconnectAsync();
            }
        }

        // === Frame Handling ===
        private async Task SendFrameAsync(string payload)
        {
            if (_stream == null) return;
            byte[] data = Encoding.UTF8.GetBytes(payload);
            int len = data.Length;
            if (len > MaxFrameSize)
                throw new InvalidOperationException("Message exceeds maximum frame size.");

            byte[] lengthPrefix = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(len));
            await _stream.WriteAsync(lengthPrefix, 0, 4);
            await _stream.WriteAsync(data, 0, data.Length);
        }

        private static async Task<string> ReadFrameAsync(NetworkStream stream, CancellationToken token)
        {
            byte[] lenBuf = new byte[4];
            await ReadExactAsync(stream, lenBuf, 0, 4, token);
            int len = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lenBuf, 0));
            if (len < 0) throw new IOException("Negative frame length.");
            if (len == 0) return string.Empty;
            if (len > MaxFrameSize) throw new IOException($"Frame too large ({len} bytes).");

            byte[] payload = new byte[len];
            await ReadExactAsync(stream, payload, 0, len, token);
            return Encoding.UTF8.GetString(payload);
        }

        private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken token)
        {
            int total = 0;
            while (total < count)
            {
                int read = await stream.ReadAsync(buffer, offset + total, count - total, token);
                if (read <= 0)
                {
                    throw new IOException("Remote closed connection during frame read.");
                }
                total += read;
            }
        }

        // === UI Helpers ===
        private void MessageInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                _ = SendCurrentMessageAsync();
            }
        }

        private void ScrollToEnd()
        {
            if (ChatList.Items.Count > 0)
            {
                ChatList.ScrollIntoView(ChatList.Items[^1]);
            }
        }

        private void AppendSystem(string msg)
        {
            ChatList.Items.Add($"[System] {msg}");
            ScrollToEnd();
        }

        private void SetStatus(string text, Color color)
        {
            StatusTextBlock.Text = text;
            StatusTextBlock.Foreground = new SolidColorBrush(color);
        }

        private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            await DisconnectAsync();
        }
    }

    // === DTO for JSON messages ===
    public class ChatMessage
    {
        public string? Type { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public string? Text { get; set; }
        public long Ts { get; set; }
    }
}