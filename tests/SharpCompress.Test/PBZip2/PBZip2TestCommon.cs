using System;
using System.IO;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.PBZip2;
using Xunit;
using Xunit.Abstractions;
namespace SharpCompress.Test.PBZip2;

public static class PBZip2TestCommon
{
    public enum TestRandomDataMode
    {
        SingleByteZero,
        SingleByte,
        RandomBytes,
        RandomBytesRepeatedValues,
    }

    public static (TimeSpan, TimeSpan) CompressAndDecompress(ITestOutputHelper console, int threads, bool useOldBzip2Decompress, byte[] inputBuffer, byte[] outputBuffer)
    {
        using MemoryStream input = new MemoryStream(inputBuffer);
        using MemoryStream output = new MemoryStream();
        using MemoryStream output2 = new MemoryStream(outputBuffer);

        DateTime end;
        DateTime start;
        TimeSpan compressionTime = TimeSpan.Zero;
        TimeSpan decompressionTime = TimeSpan.Zero;

        try
        {
            // Compress using parallel bzip2
            start = DateTime.Now;
            using BZip2ParallelOutputStream compressor = new BZip2ParallelOutputStream(output, threads, false, 9);
            input.CopyTo(compressor, 8640000);
            compressor.Close();
            end = DateTime.Now;
            compressionTime = end - start;
            //console.WriteLine($"{compressionTime.TotalMilliseconds} ms compression time... ");

            if (useOldBzip2Decompress)
            {
                // decompress using original SharpCompress BZip2 decompressor
                start = DateTime.Now;
                output.Position = 0;
                using BZip2Stream decompressor = new BZip2Stream(output, CompressionMode.Decompress, false);
                decompressor.CopyTo(output2);
                end = DateTime.Now;
                decompressionTime = end - start;
                //console.WriteLine($"{decompressionTime.TotalMilliseconds} ms decompression time");
            }
            else
            {
                // decompress using new BZip2 decompressor
                start = DateTime.Now;
                output.Position = 0;
                using BZip2InputStream decompressor = new BZip2InputStream(output, false);
                decompressor.CopyTo(output2);
                end = DateTime.Now;
                decompressionTime = end - start;
                //console.WriteLine($"{decompressionTime.TotalMilliseconds} ms decompression time");
            }
        }
        catch (Exception ex)
        {
            string randomFile = Path.GetRandomFileName();
            using FileStream fs = new FileStream(randomFile, FileMode.Create, FileAccess.Write);
            input.Position = 0;
            input.CopyTo(fs);
            fs.Flush();
            fs.Close();
            console.WriteLine("Exception processing random test data... Test data written to file: " + randomFile);
            Assert.True(false, $"Exception was thrown... {ex}");
        }

        for (int i = 0; i < inputBuffer.Length; i++)
        {
            if (inputBuffer[i] != outputBuffer[i])
            {
                string randomFile = Path.GetRandomFileName();
                using FileStream fs = new FileStream(randomFile, FileMode.Create, FileAccess.Write);
                input.Position = 0;
                input.CopyTo(fs);
                fs.Flush();
                fs.Close();
                Assert.True(false, $"bytes differ at position {i}");
            }
        }

        return (compressionTime, decompressionTime);
    }

    public static void RandomTestRepeat(TestRandomDataMode mode, ITestOutputHelper console, int threads, bool useOldBzip2Decompress, int repeat, int len = 9000000)
    {
        Random random = new Random();
        TimeSpan totalCompressionTime = TimeSpan.Zero;
        TimeSpan totalDecompressionTime = TimeSpan.Zero;
        TimeSpan maxCompressionTime = TimeSpan.MinValue;
        TimeSpan minCompressionTime = TimeSpan.MaxValue;
        TimeSpan maxDecompressionTime = TimeSpan.MinValue;
        TimeSpan minDecompressionTime = TimeSpan.MaxValue;

        for (int r = 0; r < repeat; r++)
        {
            byte[] bigBufferI = new byte[len];
            byte[] bigBufferO = new byte[len];

            // fill input buffer with random data...
            switch (mode)
            {
                case TestRandomDataMode.SingleByteZero:
                {
                    for (int i = 0; i < len; i++)
                    {
                        bigBufferI[i] = 0x00;
                    }
                    break;
                }
                case TestRandomDataMode.SingleByte:
                {
                    byte value = (byte)(random.Next() & 0xFF);
                    for (int i = 0; i < len; i++)
                    {
                        bigBufferI[i] = value;
                    }
                    break;
                }
                case TestRandomDataMode.RandomBytes:
                {
                    random.NextBytes(bigBufferI);
                    break;
                }
                case TestRandomDataMode.RandomBytesRepeatedValues:
                {
                    random.NextBytes(bigBufferI);

                    int repeatStreaks = 64;

                    int offset = 0;
                    for (int rs = 0; rs < repeatStreaks; rs++)
                    {
                        int newoffset = random.Next(0, (len - 10000) / repeatStreaks);
                        offset += newoffset;
                        int count = random.Next(0, 512);
                        byte val = bigBufferI[offset++];
                        for (int i = 0; i < count; i++)
                        {
                            bigBufferI[offset++] = val;
                        }
                    }
                    break;
                }
            }

            (var comp, var decomp) = CompressAndDecompress(console, threads, useOldBzip2Decompress, bigBufferI, bigBufferO);

            totalCompressionTime += comp;
            totalDecompressionTime += decomp;

            if (comp < minCompressionTime) minCompressionTime = comp;
            if (comp > maxCompressionTime) maxCompressionTime = comp;
            if (decomp < minDecompressionTime) minDecompressionTime = decomp;
            if (decomp > maxDecompressionTime) maxDecompressionTime = decomp;

            // no need to run single byte zero tests more than once...
            if (mode == TestRandomDataMode.SingleByteZero) break;
        }
        console.WriteLine($"AVERAGE {totalCompressionTime.TotalMilliseconds / repeat} ms compression time... ");
        console.WriteLine($"MINIMUM {minCompressionTime.TotalMilliseconds } ms compression time... ");
        console.WriteLine($"MAXIMUM {maxCompressionTime.TotalMilliseconds } ms compression time... ");
        console.WriteLine($"AVERAGE {totalDecompressionTime.TotalMilliseconds / repeat} ms decompression time... ");
        console.WriteLine($"MINIMUM {minDecompressionTime.TotalMilliseconds } ms decompression time... ");
        console.WriteLine($"MAXIMUM {maxDecompressionTime.TotalMilliseconds } ms decompression time... ");
    }
}
