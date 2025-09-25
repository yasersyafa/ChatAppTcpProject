using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

const int PORT = 8080;

List<TcpClient> clients = [];
Dictionary<TcpClient, string> clientNicknames = new();
TcpListener listener = new(IPAddress.Any, PORT);

listener.Start();
Console.WriteLine($"[SERVER] Listening on port: {PORT}");

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();
    clients.Add(client);
    Console.WriteLine("[SERVER] New client connected");

    _ = HandleClientAsync(client, clients);
}

async Task HandleClientAsync(TcpClient client, List<TcpClient> tcpClients)
{
    var stream = client.GetStream();
    const int MaxFrameSize = 64 * 1024; // 64 KB safety limit

    try
    {
        while (true)
        {
            string frame = await ReadFrameAsync(stream, MaxFrameSize);
            if (frame == string.Empty) break;

            Console.WriteLine($"[SERVER] Received JSON: {frame}");

            var msg = JsonSerializer.Deserialize<ChatMessage>(frame);
            if (msg == null) continue;

            switch (msg.Type)
            {
                case "join":
                    clientNicknames[client] = msg.From ?? "Unknown";
                    Console.WriteLine($"[SERVER] {msg.From} joined.");

                    // kirim user list ke client baru
                    await SendUserListToNewClient(client, msg.From);

                    // broadcast join ke semua client lain
                    await BroadcastAsync(new ChatMessage
                    {
                        Type = "sys",
                        Text = $"{msg.From} joined the chat",
                        Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    }, client, tcpClients);
                    break;

                case "msg":
                    await BroadcastAsync(new ChatMessage
                    {
                        Type = "msg",
                        From = msg.From,
                        Text = msg.Text,
                        Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    }, client, tcpClients);
                    break;

                case "pm":
                    if (!string.IsNullOrEmpty(msg.To))
                    {
                        await SendPrivateAsync(msg, client, tcpClients);
                    }
                    break;

                default:
                    Console.WriteLine($"[SERVER] Unknown message type: {msg.Type}");
                    break;
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] {ex.Message}");
    }
    finally
    {
        string disconnectedUser = clientNicknames.TryGetValue(client, out string? nick) ? nick : "Unknown";
        clients.Remove(client);
        client.Close();
        Console.WriteLine($"[SERVER] {disconnectedUser} disconnected.");

        if (disconnectedUser != "Unknown")
        {
            await BroadcastAsync(new ChatMessage
            {
                Type = "sys",
                Text = $"{disconnectedUser} left the chat",
                Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }, client, tcpClients);
        }
    }
}

// === Helper Methods ===

// Broadcast message to all clients except sender
async Task BroadcastAsync(ChatMessage msg, TcpClient sender, List<TcpClient> clients)
{
    string json = JsonSerializer.Serialize(msg);
    byte[] frameData = CreateFrame(json);

    foreach (var client in clients.ToList())
    {
        if (client == sender) continue;
        try
        {
            await client.GetStream().WriteAsync(frameData);
        }
        catch
        {
            clients.Remove(client);
            clientNicknames.Remove(client);
        }
    }

    Console.WriteLine($"[SERVER] Broadcasted: {json}");
}

// Send private message
async Task SendPrivateAsync(ChatMessage msg, TcpClient sender, List<TcpClient> clients)
{
    string? targetUser = msg.To;
    if (string.IsNullOrEmpty(targetUser)) return;

    var targetClient = clientNicknames.FirstOrDefault(kvp => kvp.Value == targetUser).Key;
    if (targetClient == null) return;

    string json = JsonSerializer.Serialize(msg);
    byte[] frameData = CreateFrame(json);

    try
    {
        await targetClient.GetStream().WriteAsync(frameData);
        Console.WriteLine($"[SERVER] PM from {msg.From} to {msg.To}: {msg.Text}");
    }
    catch
    {
        clients.Remove(targetClient);
        clientNicknames.Remove(targetClient);
    }
}

// Send list of users to new client
async Task SendUserListToNewClient(TcpClient newClient, string? newClientNickname)
{
    var existingUsers = clientNicknames.Where(kvp => kvp.Key != newClient).Select(kvp => kvp.Value).ToList();

    var sysMsg = new ChatMessage
    {
        Type = "sys",
        Text = existingUsers.Count > 0
            ? $"Users online: {string.Join(", ", existingUsers)}"
            : "You are the first user online.",
        Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };

    string json = JsonSerializer.Serialize(sysMsg);
    byte[] frameData = CreateFrame(json);

    try
    {
        await newClient.GetStream().WriteAsync(frameData);
        Console.WriteLine($"[SERVER] Sent user list to {newClientNickname}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Failed to send user list: {ex.Message}");
    }
}

// === Framing (same as client) ===
byte[] CreateFrame(string payload)
{
    byte[] data = Encoding.UTF8.GetBytes(payload);
    byte[] lengthPrefix = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data.Length));

    byte[] frame = new byte[4 + data.Length];
    Array.Copy(lengthPrefix, 0, frame, 0, 4);
    Array.Copy(data, 0, frame, 4, data.Length);

    return frame;
}

async Task<string> ReadFrameAsync(NetworkStream stream, int maxFrameSize)
{
    byte[] lenBuf = new byte[4];
    await ReadExactAsync(stream, lenBuf, 0, 4);
    int len = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lenBuf, 0));

    if (len < 0) throw new IOException("Negative frame length.");
    if (len == 0) return string.Empty;
    if (len > maxFrameSize) throw new IOException($"Frame too large ({len} bytes).");

    byte[] payload = new byte[len];
    await ReadExactAsync(stream, payload, 0, len);
    return Encoding.UTF8.GetString(payload);
}

async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count)
{
    int total = 0;
    while (total < count)
    {
        int read = await stream.ReadAsync(buffer.AsMemory(offset + total, count - total));
        if (read <= 0) throw new IOException("Remote closed connection during frame read.");
        total += read;
    }
}

// === ChatMessage DTO ===
public class ChatMessage
{
    public string? Type { get; set; }   // msg, join, leave, pm, sys
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Text { get; set; }
    public long Ts { get; set; }
}