using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

const int PORT = 8080;

List<TcpClient> clients = [];
Dictionary<TcpClient, string> clientNicknames = [];
HashSet<string> usedNicknames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
object clientsLock = new object();
TcpListener listener = new(IPAddress.Any, PORT);

listener.Start();
LoggingService.LogInfo($"Server started and listening on port: {PORT}");

// Cleanup old log files on startup
_ = Task.Run(async () => await LoggingService.CleanupOldLogsAsync());

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();
    lock (clientsLock)
    {
        clients.Add(client);
    }
    LoggingService.LogConnection(client.Client.RemoteEndPoint?.ToString() ?? "Unknown", "Client connected");

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

            LoggingService.LogInfo($"Received message from {client.Client.RemoteEndPoint}: {frame}");

            var msg = JsonSerializer.Deserialize<ChatMessage>(frame);
            if (msg == null) continue;

            switch (msg.Type)
            {
                case "join":
                    string originalRequestedName = msg.From ?? "Unknown";
                    string validatedUsername;
                    
                    lock (clientsLock)
                    {
                        validatedUsername = ValidateAndEnsureUniqueUsername(originalRequestedName, client);
                        // Add to used nicknames set
                        usedNicknames.Add(validatedUsername);
                        clientNicknames[client] = validatedUsername;
                    }
                    
                    LoggingService.LogInfo($"User '{validatedUsername}' joined (requested: '{originalRequestedName}') from {client.Client.RemoteEndPoint}");

                    // Send username confirmation to client
                    await SendUsernameConfirmationAsync(client, originalRequestedName, validatedUsername);

                    // kirim user list ke client baru
                    await SendUserListToNewClient(client, validatedUsername);

                    // broadcast join ke semua client lain
                    await BroadcastAsync(new ChatMessage
                    {
                        Type = "sys",
                        Text = $"{validatedUsername} joined the chat",
                        Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    }, client, tcpClients);

                    // broadcast updated user list to all existing clients (except new one)
                    await BroadcastUpdatedUserListToExistingClients(client, tcpClients);
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

                case "typing":
                    await BroadcastTypingIndicatorAsync(msg, client, tcpClients);
                    break;

                case "stop_typing":
                    await BroadcastStopTypingIndicatorAsync(msg, client, tcpClients);
                    break;

                default:
                    LoggingService.LogWarning($"Unknown message type received: {msg.Type} from {client.Client.RemoteEndPoint}");
                    break;
            }
        }
    }
    catch (Exception ex)
    {
        LoggingService.LogError($"Error handling client {client.Client.RemoteEndPoint}", ex);
    }
    finally
    {
        string disconnectedUser = "Unknown";
        lock (clientsLock)
        {
            disconnectedUser = clientNicknames.TryGetValue(client, out string? nick) ? nick : "Unknown";
            clients.Remove(client);
            clientNicknames.Remove(client);
            
            // Remove from used nicknames set
            if (disconnectedUser != "Unknown")
            {
                usedNicknames.Remove(disconnectedUser);
            }
        }
        
        client.Close();
        LoggingService.LogConnection($"{disconnectedUser} ({client.Client.RemoteEndPoint})", "Client disconnected");

        if (disconnectedUser != "Unknown")
        {
            LoggingService.LogInfo($"Broadcasting disconnect for user: {disconnectedUser}");
            
            // Broadcast leave message
            await BroadcastAsync(new ChatMessage
            {
                Type = "sys",
                Text = $"{disconnectedUser} left the chat",
                Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }, client, tcpClients);

            // Small delay to ensure leave message is processed first
            await Task.Delay(100);

            // Broadcast updated user list to remaining clients
            LoggingService.LogInfo($"Broadcasting updated user list after {disconnectedUser} disconnect");
            await BroadcastUpdatedUserListToRemainingClients(tcpClients);
        }
    }
}

// === Helper Methods ===

// Validate and ensure unique username
string ValidateAndEnsureUniqueUsername(string? requestedUsername, TcpClient client)
{
    if (string.IsNullOrWhiteSpace(requestedUsername))
    {
        return GenerateUniqueUsername("User");
    }

    // Clean username: remove leading/trailing spaces, replace multiple spaces with single space
    string cleanUsername = Regex.Replace(requestedUsername.Trim(), @"\s+", " ");
    
    // Validate username: alphanumeric, spaces, hyphens, underscores only
    if (!Regex.IsMatch(cleanUsername, @"^[a-zA-Z0-9\s\-_]+$"))
    {
        return GenerateUniqueUsername("User");
    }

    // Check length (3-20 characters)
    if (cleanUsername.Length < 3 || cleanUsername.Length > 20)
    {
        return GenerateUniqueUsername("User");
    }

    // Check if username is already taken
    if (usedNicknames.Contains(cleanUsername))
    {
        return GenerateUniqueUsername(cleanUsername);
    }

    return cleanUsername;
}

// Generate unique username by appending number
string GenerateUniqueUsername(string baseName)
{
    string baseClean = Regex.Replace(baseName.Trim(), @"\s+", " ");
    string candidate = baseClean;
    int counter = 1;

    while (usedNicknames.Contains(candidate))
    {
        candidate = $"{baseClean}{counter}";
        counter++;
    }

    return candidate;
}

// Send username confirmation to client
async Task SendUsernameConfirmationAsync(TcpClient client, string originalRequestedName, string validatedUsername)
{
    var confirmationMsg = new ChatMessage
    {
        Type = "username_confirmed",
        From = validatedUsername,
        Text = originalRequestedName != validatedUsername 
            ? $"Username '{originalRequestedName}' was not available. You are now known as '{validatedUsername}'."
            : $"Welcome, {validatedUsername}!",
        Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };

    string json = JsonSerializer.Serialize(confirmationMsg);
    byte[] frameData = CreateFrame(json);

    try
    {
        await client.GetStream().WriteAsync(frameData);
        LoggingService.LogInfo($"Sent username confirmation to {validatedUsername}");
    }
    catch (Exception ex)
    {
        LoggingService.LogError($"Failed to send username confirmation to {validatedUsername}", ex);
    }
}

// Broadcast message to all clients except sender
async Task BroadcastAsync(ChatMessage msg, TcpClient sender, List<TcpClient> clients)
{
    string json = JsonSerializer.Serialize(msg);
    byte[] frameData = CreateFrame(json);

    List<TcpClient> clientsCopy;
    lock (clientsLock)
    {
        clientsCopy = clients.ToList();
    }

    foreach (var client in clientsCopy)
    {
        if (client == sender) continue;
        try
        {
            await client.GetStream().WriteAsync(frameData);
        }
        catch
        {
            lock (clientsLock)
            {
                clients.Remove(client);
                clientNicknames.Remove(client);
            }
        }
    }

    LoggingService.LogInfo($"Broadcasted message: {json}");
}

// Send private message
async Task SendPrivateAsync(ChatMessage msg, TcpClient sender, List<TcpClient> clients)
{
    string? targetUser = msg.To;
    if (string.IsNullOrEmpty(targetUser)) return;

    TcpClient? targetClient;
    lock (clientsLock)
    {
        targetClient = clientNicknames.FirstOrDefault(kvp => kvp.Value == targetUser).Key;
    }
    
    if (targetClient == null) return;

    string json = JsonSerializer.Serialize(msg);
    byte[] frameData = CreateFrame(json);

    try
    {
        await targetClient.GetStream().WriteAsync(frameData);
        LoggingService.LogInfo($"Private message from {msg.From} to {msg.To}: {msg.Text}");
    }
    catch
    {
        lock (clientsLock)
        {
            clients.Remove(targetClient);
            clientNicknames.Remove(targetClient);
        }
    }
}

// Broadcast typing indicator
async Task BroadcastTypingIndicatorAsync(ChatMessage msg, TcpClient sender, List<TcpClient> clients)
{
    var typingMsg = new ChatMessage
    {
        Type = "typing",
        From = msg.From,
        To = msg.To, // For private chat typing
        Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };

    if (!string.IsNullOrEmpty(msg.To))
    {
        // Private chat typing - only send to target user
        await SendPrivateAsync(typingMsg, sender, clients);
    }
    else
    {
        // General chat typing - broadcast to all except sender
        await BroadcastAsync(typingMsg, sender, clients);
    }
}

// Broadcast stop typing indicator
async Task BroadcastStopTypingIndicatorAsync(ChatMessage msg, TcpClient sender, List<TcpClient> clients)
{
    var stopTypingMsg = new ChatMessage
    {
        Type = "stop_typing",
        From = msg.From,
        To = msg.To, // For private chat typing
        Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };

    if (!string.IsNullOrEmpty(msg.To))
    {
        // Private chat stop typing - only send to target user
        await SendPrivateAsync(stopTypingMsg, sender, clients);
    }
    else
    {
        // General chat stop typing - broadcast to all except sender
        await BroadcastAsync(stopTypingMsg, sender, clients);
    }
}

// Send list of users to new client
async Task SendUserListToNewClient(TcpClient newClient, string? newClientNickname)
{
    List<string> existingUsers;
    lock (clientsLock)
    {
        existingUsers = clientNicknames.Where(kvp => kvp.Key != newClient).Select(kvp => kvp.Value).ToList();
    }

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
        LoggingService.LogInfo($"Sent user list to {newClientNickname}");
    }
    catch (Exception ex)
    {
        LoggingService.LogError($"Failed to send user list to {newClientNickname}", ex);
    }
}

// Broadcast updated user list to existing clients when new user joins
async Task BroadcastUpdatedUserListToExistingClients(TcpClient newClient, List<TcpClient> clients)
{
    List<string> allUsers;
    List<TcpClient> clientsCopy;
    
    lock (clientsLock)
    {
        // Get all users including the new one
        allUsers = clientNicknames.Select(kvp => kvp.Value).ToList();
        clientsCopy = clients.ToList();
    }
    
    if (allUsers.Count <= 1) return; // No need to broadcast if only 1 user

    var sysMsg = new ChatMessage
    {
        Type = "sys", 
        Text = $"Users online: {string.Join(", ", allUsers)}",
        Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };

    string json = JsonSerializer.Serialize(sysMsg);
    byte[] frameData = CreateFrame(json);

    // Send to all clients EXCEPT the new one (they already got their list)
    foreach (var client in clientsCopy)
    {
        if (client == newClient) continue;
        
        try
        {
            await client.GetStream().WriteAsync(frameData);
        }
        catch
        {
            lock (clientsLock)
            {
                clients.Remove(client);
                clientNicknames.Remove(client);
            }
        }
    }

    LoggingService.LogInfo($"Broadcasted updated user list: {string.Join(", ", allUsers)}");
}

// Broadcast updated user list to remaining clients when user leaves
async Task BroadcastUpdatedUserListToRemainingClients(List<TcpClient> clients)
{
    List<TcpClient> clientsCopy;
    List<string> remainingUsers;
    
    lock (clientsLock)
    {
        if (clients.Count == 0) return; // No clients left
        clientsCopy = clients.ToList();
        remainingUsers = clientNicknames.Select(kvp => kvp.Value).ToList();
    }
    
    string userListText = remainingUsers.Count > 0 
        ? $"Users online: {string.Join(", ", remainingUsers)}"
        : "No users online";

    var sysMsg = new ChatMessage
    {
        Type = "sys",
        Text = userListText,
        Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };

    string json = JsonSerializer.Serialize(sysMsg);
    byte[] frameData = CreateFrame(json);

    foreach (var client in clientsCopy)
    {
        try
        {
            await client.GetStream().WriteAsync(frameData);
        }
        catch
        {
            lock (clientsLock)
            {
                clients.Remove(client);
                clientNicknames.Remove(client);
            }
        }
    }

    LoggingService.LogInfo($"Broadcasted updated user list after disconnect: {string.Join(", ", remainingUsers)}");
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
    public string? Type { get; set; }   // msg, join, leave, pm, sys, typing, stop_typing, username_confirmed
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Text { get; set; }
    public long Ts { get; set; }
}