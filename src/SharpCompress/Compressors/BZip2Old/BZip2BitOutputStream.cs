// Added by drone1400, July 2022
// Location: https://github.com/drone1400/sharpcompress/tree/drone-modifications

using System;
using System.IO;

#nullable disable

namespace SharpCompress.Compressors.BZip2Old;


public class BZip2BitOutputStream : Stream, IBzip2BitOutputStream
{
    private Stream _stream;
    private bool _isStreamOwner = false;
    private int _bitCount = 0;
    private int _bitBuff = 0;
    private bool _isFinished = false;



    public BZip2BitOutputStream(Stream stream, bool isOwner = true)
    {
        this._stream = stream;
        this._bitCount = 0;
        this._bitBuff = 0;
        this._isStreamOwner = isOwner;
    }

    public void WriteBits(int n, int v)
    {
        while (this._bitCount >= 8)
        {
            int data = (this._bitBuff >> 24);
            this._stream.WriteByte((byte)data); // write 8-bit
            this._bitBuff <<= 8;
            this._bitCount -= 8;
        }
        this._bitCount += n;
        this._bitBuff |= v << (32 - this._bitCount);

    }

    public void WriteBit(bool isOne)
    {
        while (this._bitCount >= 8)
        {
            int data = (this._bitBuff >> 24);
            this._stream.WriteByte((byte)data); // write 8-bit
            this._bitBuff <<= 8;
            this._bitCount -= 8;
        }

        this._bitCount += 1;

        if (isOne)
        {
            this._bitBuff |= 1 << (32 - this._bitCount);
        }
    }

    public override void WriteByte(byte data)
    {
        this.WriteBits(8, data);
    }

    public void WriteInt(int v)
    {
        this.WriteBits(8, (v >> 24) & 0xff);
        this.WriteBits(8, (v >> 16) & 0xff);
        this.WriteBits(8, (v >> 8) & 0xff);
        this.WriteBits(8, v & 0xff);
    }

    protected override void Dispose(bool disposing)
    {
        if (this._isFinished)
        {
            return;
        }

        this._isFinished = true;

        // flush remaining data bits
        while (this._bitCount > 0)
        {
            int data = (this._bitBuff >> 24);
            this._stream.WriteByte((byte)data); // write 8-bit
            this._bitBuff <<= 8;
            this._bitCount -= 8;
        }

        this._stream.Flush();
        if (this._isStreamOwner)
        {
            this._stream.Close();
        }
    }

    public override void Flush()
    {
        while (this._bitCount > 0)
        {
            int data = (this._bitBuff >> 24);
            this._stream.WriteByte((byte)data); // write 8-bit
            this._bitBuff <<= 8;
            this._bitCount -= 8;
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();
    public override void SetLength(long value)
        => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count)
    {
        for (int i = 0; i < count; i++)
        {
            this.WriteByte(buffer[offset++]);
        }
    }


    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => this._stream.Length;
    public override long Position
    {
        get => this._stream.Position;
        set => throw new NotSupportedException();
    }
}
