using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace OrderFlow.IntegrationTests.Messaging;

// Transport fault, not a mocked publish success: forward AMQP except server publisher confirms.
// Detect the real broker ACK frame, then drop it while leaving the connection open until client timeout.
internal sealed class ConfirmDroppingProxy : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly Task run;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public TaskCompletionSource ConfirmDropped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ConfirmDroppingProxy(string host, int port)
    {
        listener.Start(); run = RunAsync(host, port);
    }
    private async Task RunAsync(string host, int port)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync(stop.Token);
            using var broker = new TcpClient(); await broker.ConnectAsync(host, port, stop.Token);
            var upstream = client.GetStream().CopyToAsync(broker.GetStream(), stop.Token);
            var downstream = ForwardFramesAsync(broker.GetStream(), client.GetStream());
            await Task.WhenAny(upstream, downstream);
            stop.Cancel();
            try { await Task.WhenAll(upstream, downstream); }
            catch (Exception error) when (error is IOException or OperationCanceledException or SocketException) { }
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or SocketException) { }
    }
    private async Task ForwardFramesAsync(NetworkStream input, NetworkStream output)
    {
        var header = new byte[7];
        while (!stop.IsCancellationRequested)
        {
            await input.ReadExactlyAsync(header, stop.Token);
            var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(3)));
            if (size > 16 * 1024 * 1024) throw new InvalidOperationException("Unexpected test AMQP frame size.");
            var payload = new byte[size + 1]; await input.ReadExactlyAsync(payload, stop.Token);
            if (header[0] == 1 && size >= 4 && BinaryPrimitives.ReadUInt16BigEndian(payload) == 60
                && BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(2)) is 80 or 120)
            { ConfirmDropped.TrySetResult(); continue; }
            await output.WriteAsync(header, stop.Token); await output.WriteAsync(payload, stop.Token);
        }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); listener.Stop();
        await run.WaitAsync(TimeSpan.FromSeconds(10)); stop.Dispose();
    }
}
