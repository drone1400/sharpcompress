// Added by drone1400, July 2022
// Location: https://github.com/drone1400/sharpcompress/tree/drone-modifications

using System;
using System.IO;
using SharpCompress.Compressors.BZip2;
using Xunit;
using Xunit.Abstractions;

namespace SharpCompress.Test.BZip2
{

    public class Bzip2ParallelOutputStream {
        private readonly ITestOutputHelper _console;
        private readonly int _threads;
        private readonly Random _random;

        public Bzip2ParallelOutputStream(ITestOutputHelper console)
        {
            this._random = new Random();
            this._threads = Environment.ProcessorCount;
            this._console = console;
        }


        [Fact]
        public void RandomSingleByteLongTestX1() => this.RandomSingleByteLongTestX(1,100000000);
        [Fact]
        public void RandomSingleByteLongTestX10() => this.RandomSingleByteLongTestX(10,100000000);

        [Fact]
        public void RandomLongTestX1() => this.RandomLongTestX(1);
        [Fact]
        public void RandomLongTestX10() => this.RandomLongTestX(10);
        [Fact]
        public void RandomLongTestX100() => this.RandomLongTestX(100);
        [Fact]
        public void RandomLongTestX1000() => this.RandomLongTestX(1000);

        [Fact]
        public void RandomLongTestWithRepeatedValuesX1() => this.RandomLongTestWithRepeatedValuesX(1);

        [Fact]
        public void RandomLongTestWithRepeatedValuesX10() => this.RandomLongTestWithRepeatedValuesX(10);

        [Fact]
        public void RandomLongTestWithRepeatedValuesX100() => this.RandomLongTestWithRepeatedValuesX(100);

        [Fact]
        public void RandomLongTestWithRepeatedValuesX1000() => this.RandomLongTestWithRepeatedValuesX(1000);

        private void RandomSingleByteLongTestX(int x, int len = 9000000) {
            TimeSpan totalCompressionTime = TimeSpan.Zero;
            TimeSpan totalDecompressionTime = TimeSpan.Zero;
            for (int r = 0; r < x; r++) {

                byte[] bigBufferI = new byte[len];
                byte[] bigBufferO = new byte[len];

                byte value = (byte)(_random.Next() & 0xFF);
                for (int i = 0; i < len; i++) {
                    bigBufferI[i] = value;
                }

                (var comp, var decomp) = this.RandomTestCommon(bigBufferI, bigBufferO);
                totalCompressionTime += comp;
                totalDecompressionTime += decomp;
            }
            this._console.WriteLine($"AVERAGE {totalCompressionTime.TotalMilliseconds / x} ms compression time... ");
            this._console.WriteLine($"AVERAGE {totalDecompressionTime.TotalMilliseconds / x} ms decompression time... ");
        }

        private void RandomLongTestX(int repeat, int len = 9000000)
        {
            TimeSpan totalCompressionTime = TimeSpan.Zero;
            TimeSpan totalDecompressionTime = TimeSpan.Zero;
            for (int r = 0; r < repeat; r++) {

                byte[] bigBufferI = new byte[len];
                byte[] bigBufferO = new byte[len];

                // random input bytes
                _random.NextBytes(bigBufferI);

                (var comp, var decomp) = this.RandomTestCommon(bigBufferI, bigBufferO);

                totalCompressionTime += comp;
                totalDecompressionTime += decomp;
            }
            this._console.WriteLine($"AVERAGE {totalCompressionTime.TotalMilliseconds / repeat} ms compression time... ");
            this._console.WriteLine($"AVERAGE {totalDecompressionTime.TotalMilliseconds / repeat} ms decompression time... ");
        }

        private void RandomLongTestWithRepeatedValuesX(int repeat, int len = 9000000) {
            TimeSpan totalCompressionTime = TimeSpan.Zero;
            TimeSpan totalDecompressionTime = TimeSpan.Zero;
            for (int r = 0; r < repeat; r++) {
                int repeatStreaks = 64;
                byte[] bigBufferI = new byte[len];
                byte[] bigBufferO = new byte[len];

                // random input bytes
                this._random.NextBytes(bigBufferI);

                int offset = 0;
                for (int rs = 0; rs < repeatStreaks; rs++) {
                    int newoffset = this._random.Next(0, (len - 10000) / repeatStreaks);
                    offset += newoffset;
                    int count = this._random.Next(0, 512);
                    byte val = bigBufferI[offset++];
                    for (int i = 0; i < count; i++) {
                        bigBufferI[offset++] = val;
                    }
                }


                (var comp, var decomp) = this.RandomTestCommon(bigBufferI, bigBufferO);
                totalCompressionTime += comp;
                totalDecompressionTime += decomp;
            }
            this._console.WriteLine($"AVERAGE {totalCompressionTime.TotalMilliseconds / repeat} ms compression time... ");
            this._console.WriteLine($"AVERAGE {totalDecompressionTime.TotalMilliseconds / repeat} ms decompression time... ");
        }

        private (TimeSpan, TimeSpan) RandomTestCommon(byte[] inputBuffer, byte[] outputBuffer)
        {
            using MemoryStream input = new MemoryStream(inputBuffer);
            using MemoryStream output = new MemoryStream();
            using MemoryStream output2 = new MemoryStream(outputBuffer);

            DateTime end;
            DateTime start;
            TimeSpan compressionTime = TimeSpan.Zero;
            TimeSpan decompressionTime = TimeSpan.Zero;

            try {

                start = DateTime.Now;
                using BZip2ParallelOutputStream compressor = new BZip2ParallelOutputStream(output, this._threads, false, 9);
                input.CopyTo(compressor, 8640000);
                compressor.Close();
                end = DateTime.Now;
                compressionTime = end - start;
                this._console.WriteLine($"{compressionTime.TotalMilliseconds} ms compression time... ");

                start = DateTime.Now;
                output.Position = 0;
                using BZip2InputStream decompressor = new BZip2InputStream(output, false);
                decompressor.CopyTo(output2);
                end = DateTime.Now;
                decompressionTime = end - start;
                this._console.WriteLine($"{decompressionTime.TotalMilliseconds} ms decompression time");
            } catch (Exception ex) {
                string randomFile = Path.GetRandomFileName();
                using FileStream fs = new FileStream(randomFile, FileMode.Create, FileAccess.Write);
                input.Position = 0;
                input.CopyTo(fs);
                fs.Flush();
                fs.Close();
                Assert.True(false, $"Exception was thrown... {ex}");
            }

            for (int i = 0; i < inputBuffer.Length; i++) {
                if (inputBuffer[i] != outputBuffer[i]) {
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
    }

}
