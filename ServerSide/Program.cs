// See https://aka.ms/new-console-template for more information
using System.Net;
using System.Net.Sockets;
using System.Text;

const int PORT = 8080;

List<TcpClient> clients = [];
Dictionary<TcpClient, string> clientNicknames = new();
TcpListener listener = new(IPAddress.Any, PORT);

// starting to listen client
listener.Start();
Console.WriteLine($"[SERVER] listening on port: {PORT}");

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();
    clients.Add(client);
    Console.WriteLine($"[SERVER] New client connected");

    _ = HandleClientAsync(client, clients);
}

async Task HandleClientAsync(TcpClient client, List<TcpClient> tcpClients)
{
    var stream = client.GetStream();
    string? clientNickname = null;
    const int MaxFrameSize = 64 * 1024; // 64 KB safety limit

    try
    {
        while (true)
        {
            // Read frame using same protocol as client
            string frame = await ReadFrameAsync(stream, MaxFrameSize);
            if (frame == string.Empty) break; // remote closed gracefully

            Console.WriteLine($"[SERVER] Received frame: {frame}");

            if (frame.StartsWith("NICK:"))
            {
                // Store client nickname
                clientNickname = frame.Substring(5);
                clientNicknames[client] = clientNickname;
                Console.WriteLine($"[SERVER] Client registered as: {clientNickname}");
                
                // Send list of existing users to the newly connected client
                await SendExistingUsersToNewClient(client, clientNickname);
                
                // Broadcast user joined notification to all OTHER users
                await BroadcastNotificationAsync($"{clientNickname} joined the chat", client, tcpClients);
            }
            else if (frame.StartsWith("MSG:"))
            {
                // Broadcast message with sender's nickname
                string messageContent = frame.Substring(4);
                string senderName = clientNicknames.TryGetValue(client, out string? nick) ? nick : "Unknown";
                await BroadcastMessageAsync(messageContent, senderName, client, tcpClients);
            }
            else
            {
                // Handle other message types or raw messages
                Console.WriteLine($"[SERVER] Unknown frame type: {frame}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] {ex.Message}");
    }
    finally
    {
        // Handle disconnection
        string disconnectedUser = clientNicknames.TryGetValue(client, out string? nick) ? nick : "Unknown user";
        clients.Remove(client);
        clientNicknames.Remove(client);
        client.Close();
        Console.WriteLine($"[SERVER] {disconnectedUser} disconnected.");
        
        // Broadcast user left notification
        if (!string.IsNullOrEmpty(disconnectedUser) && disconnectedUser != "Unknown user")
        {
            await BroadcastNotificationAsync($"{disconnectedUser} left the chat", client, tcpClients);
        }
    }
}

// Broadcast a chat message with sender's nickname
async Task BroadcastMessageAsync(string message, string senderName, TcpClient sender, List<TcpClient> clients)
{
    string formattedMessage = $"MSG:[{senderName}] {message}";
    byte[] frameData = CreateFrame(formattedMessage);

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
    Console.WriteLine($"[SERVER] Broadcasted: [{senderName}] {message}");
}

// Broadcast system notifications (join/leave)
async Task BroadcastNotificationAsync(string notification, TcpClient sender, List<TcpClient> clients)
{
    string systemMessage = $"SYSTEM:{notification}";
    byte[] frameData = CreateFrame(systemMessage);

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
    Console.WriteLine($"[SERVER] System notification: {notification}");
}

// Send existing users list to newly connected client
async Task SendExistingUsersToNewClient(TcpClient newClient, string newClientNickname)
{
    var existingUsers = clientNicknames.Where(kvp => kvp.Key != newClient).Select(kvp => kvp.Value).ToList();
    
    if (existingUsers.Count > 0)
    {
        string userListMessage = $"SYSTEM:Users online: {string.Join(", ", existingUsers)}";
        byte[] frameData = CreateFrame(userListMessage);
        
        try
        {
            await newClient.GetStream().WriteAsync(frameData);
            Console.WriteLine($"[SERVER] Sent user list to {newClientNickname}: {string.Join(", ", existingUsers)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to send user list to {newClientNickname}: {ex.Message}");
        }
    }
    else
    {
        // First user connecting
        string welcomeMessage = $"SYSTEM:Welcome {newClientNickname}! You are the first user online.";
        byte[] frameData = CreateFrame(welcomeMessage);
        
        try
        {
            await newClient.GetStream().WriteAsync(frameData);
            Console.WriteLine($"[SERVER] Sent welcome message to {newClientNickname}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to send welcome message to {newClientNickname}: {ex.Message}");
        }
    }
}

// Create frame with 4-byte length prefix (same as client)
byte[] CreateFrame(string payload)
{
    byte[] data = Encoding.UTF8.GetBytes(payload);
    byte[] lengthPrefix = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data.Length));
    
    byte[] frame = new byte[4 + data.Length];
    Array.Copy(lengthPrefix, 0, frame, 0, 4);
    Array.Copy(data, 0, frame, 4, data.Length);
    
    return frame;
}

// Read frame using same protocol as client
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
        int read = await stream.ReadAsync(buffer, offset + total, count - total);
        if (read <= 0)
        {
            throw new IOException("Remote closed connection during frame read.");
        }
        total += read;
    }
}