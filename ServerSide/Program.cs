// See https://aka.ms/new-console-template for more information
using System.Net;
using System.Net.Sockets;
using System.Text;

const int PORT = 8080;

List<TcpClient> clients = [];
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
    var buffer = new byte[1024];

    try
    {
        while (true)
        {
            int byteCount = await stream.ReadAsync(buffer);
            if (byteCount <= 0) break;

            string message = Encoding.UTF8.GetString(buffer, 0, byteCount);
            Console.WriteLine($"[SERVER] Received: {message}");

            await BroadcastAsync(message, client, tcpClients);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] {ex.Message}");
    }
    finally
    {
        clients.Remove(client);
        client.Close();
        Console.WriteLine("[SERVER] Client disconnected.");
    }
}

async Task BroadcastAsync(string message, TcpClient sender, List<TcpClient> clients)
{
    byte[] data = Encoding.UTF8.GetBytes(message);

    foreach (var client in clients.ToList()) // ToList biar aman saat remove
    {
        if (client == sender) continue;

        try
        {
            await client.GetStream().WriteAsync(data);
        }
        catch
        {
            clients.Remove(client);
        }
    }
}