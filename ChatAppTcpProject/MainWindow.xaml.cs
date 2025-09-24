using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace ChatAppTcpProject
{
    public partial class MainWindow : Window
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cts;
        private bool _connected;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Optionally auto-connect; comment out if you prefer manual.
            // await ConnectAsync();
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

        private async Task ConnectAsync()
        {
            string host = ServerHostTextBox.Text.Trim();
            if (!int.TryParse(ServerPortTextBox.Text, out int port))
            {
                AppendSystem("Invalid port.");
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
                ConnectButton.Content = "Disconnect";
                SendButton.IsEnabled = true;
                SetStatus("Connected", Colors.Green);
                AppendSystem($"Connected to {host}:{port}");

                _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                SetStatus("Failed", Colors.Red);
                AppendSystem($"Connect failed: {ex.Message}");
                await DisconnectAsync();
            }
        }

        private async Task DisconnectAsync()
        {
            try
            {
                _cts?.Cancel();
                _stream?.Close();
                _client?.Close();
            }
            catch { /* ignore */ }
            finally
            {
                _connected = false;
                ConnectButton.Content = "Connect";
                SendButton.IsEnabled = false;
                SetStatus("Disconnected", Colors.Gray);
                AppendSystem("Disconnected.");
            }
            await Task.CompletedTask;
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            if (_stream == null) return;
            byte[] buffer = new byte[1024];

            try
            {
                while (!token.IsCancellationRequested)
                {
                    int read = await _stream.ReadAsync(buffer, 0, buffer.Length, token);
                    if (read <= 0) break;

                    string message = Encoding.UTF8.GetString(buffer, 0, read);
                    Dispatcher.Invoke(() =>
                    {
                        ChatList.Items.Add($"[Peer] {message}");
                        ScrollToEnd();
                    });
                }
            }
            catch (OperationCanceledException) { }
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
                byte[] data = Encoding.UTF8.GetBytes(text);
                await _stream.WriteAsync(data, 0, data.Length);
                // Server does not echo sender, so we show it locally:
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

        private void SetStatus(string text, System.Windows.Media.Color color)
        {
            StatusTextBlock.Text = text;
            StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(color);
        }

        private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            await DisconnectAsync();
        }
    }
}