namespace DuetDiagram.Poc.McpTransport;

/// <summary>
/// 只读的抓包流：转发内层流的读取，同时把读到的字节记下来。
/// </summary>
/// <remarks>
/// <para>
/// 用来取协议内容的原始证据。之所以需要它：服务端的消息过滤器只覆盖会话建立之后的业务请求，
/// 初始化那一次交互在过滤器之前就由会话层处理掉了。要判断"客户端在初始化请求里写的自定义字段
/// 到底有没有上线"，只能退到传输层看字节。
/// </para>
/// <para>
/// 只实现读：它包在内层流的读侧，写路径不经过它。未用到的成员直接抛不支持，
/// 这样一旦用法超出预期会立刻报错，而不是悄悄返回一个空结果让取证结论失真。
/// </para>
/// </remarks>
internal sealed class RecordingReadStream : Stream
{
    private readonly Stream _inner;
    private readonly MemoryStream _captured = new();

    public RecordingReadStream(Stream inner) => _inner = inner;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>把记录到的字节解码成文本，供断言直接查找。</summary>
    public string CapturedText() => System.Text.Encoding.UTF8.GetString(_captured.ToArray());

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);

        if (read > 0)
        {
            _captured.Write(buffer, offset, read);
        }

        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

        if (read > 0)
        {
            _captured.Write(buffer.Span[..read]);
        }

        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _captured.Dispose();
        }

        base.Dispose(disposing);
    }

    public override void Flush() => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
