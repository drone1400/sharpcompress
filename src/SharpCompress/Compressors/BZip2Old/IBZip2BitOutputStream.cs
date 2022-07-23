// Added by drone1400, July 2022
// Location: https://github.com/drone1400/sharpcompress/tree/drone-modifications


namespace SharpCompress.Compressors.BZip2Old;

public interface IBzip2BitOutputStream
{
    void WriteBits(int n, int v);
    void WriteBit(bool isOne);
    void WriteByte(byte data);
    void WriteInt(int v);
}
