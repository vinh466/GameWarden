using System.Text;
using System.Text.Json;

namespace GameWarden.Contracts.Runner;

/// <summary>Newline-delimited JSON over a duplex stream. Sends are serialized; one reader at a time.</summary>
public sealed class PipeChannel : IAsyncDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public PipeChannel(Stream stream)
    {
        _reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        _writer = new StreamWriter(stream, Utf8, bufferSize: 4096, leaveOpen: true) { NewLine = "\n" };
    }

    public async Task SendAsync(PipeMessage message, CancellationToken cancellationToken = default)
    {
        var line = JsonSerializer.Serialize(message, ContractJson.Options);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            await _writer.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Next message, or <c>null</c> when the other side closed the pipe.</summary>
    public async Task<PipeMessage?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        var line = await _reader.ReadLineAsync(cancellationToken);
        if (line is null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<PipeMessage>(line, ContractJson.Options)
            ?? throw new InvalidDataException("Empty message on the Runner pipe.");
    }

    public async ValueTask DisposeAsync()
    {
        _reader.Dispose();
        try
        {
            await _writer.DisposeAsync();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The other side is gone; nothing left to flush to.
        }

        _writeLock.Dispose();
    }
}
