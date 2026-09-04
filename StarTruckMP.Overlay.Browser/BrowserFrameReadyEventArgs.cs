using System.Buffers;

namespace StarTruckMP.Overlay.Browser;

public sealed class BrowserFrameReadyEventArgs : EventArgs
{
    public BrowserFrameReadyEventArgs(byte[] buffer, int width, int height, int stride)
    {
        Buffer = buffer;
        Width = width;
        Height = height;
        Stride = stride;
    }

    /// <summary>Rented from the shared pool and possibly longer than the frame; <see cref="Length"/> is what is valid.</summary>
    public byte[] Buffer { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }

    public int Length => Height * Stride;

    /// <summary>Gives the pixels back to the pool. Once, by whoever drew them last.</summary>
    public void Release()
    {
        ArrayPool<byte>.Shared.Return(Buffer);
    }
}
