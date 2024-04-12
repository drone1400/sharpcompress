using System;
using Xunit;
using Xunit.Abstractions;

namespace SharpCompress.Test.PBZip2
{

    // Compress and decompress random data over and over and over and over again...
    // uses the original BZip2 decompression from SharpCompress
    public class PBZip2OldParallelOutputStream {
        private readonly ITestOutputHelper _console;
        private readonly int _threads;

        public PBZip2OldParallelOutputStream(ITestOutputHelper console)
        {
            this._threads = Environment.ProcessorCount;
            this._console = console;
        }

        [Fact]
        public void RandomSingleByteZeroLongTestX1() =>
            PBZip2TestCommon.RandomTestRepeat(
                                              PBZip2TestCommon.TestRandomDataMode.SingleByte,
                                              this._console,
                                              this._threads,
                                              true,
                                              1,
                                              1000000000);

        [Fact]
        public void RandomSingleByteLongTestX1() =>
            PBZip2TestCommon.RandomTestRepeat(
                                              PBZip2TestCommon.TestRandomDataMode.SingleByte,
                                              this._console,
                                              this._threads,
                                              true,
                                              1,
                                              100000000);

        [Fact]
        public void RandomSingleByteLongTestX10() =>
            PBZip2TestCommon.RandomTestRepeat(
                                              PBZip2TestCommon.TestRandomDataMode.SingleByte,
                                              this._console,
                                              this._threads,
                                              true,
                                              10,
                                              100000000);

        [Fact]
        public void RandomLongTestX1() =>
            PBZip2TestCommon.RandomTestRepeat(
                                              PBZip2TestCommon.TestRandomDataMode.RandomBytes,
                                              this._console,
                                              this._threads,
                                              true,
                                              1);
        [Fact]
        public void RandomLongTestX10() =>
            PBZip2TestCommon.RandomTestRepeat(
                                              PBZip2TestCommon.TestRandomDataMode.RandomBytes,
                                              this._console,
                                              this._threads,
                                              true,
                                              10);
        // [Fact]
        // public void RandomLongTestX100() =>
        //     PBZip2TestCommon.RandomTestRepeat(
        //                                       PBZip2TestCommon.TestRandomDataMode.RandomBytes,
        //                                       this._console,
        //                                       this._threads,
        //                                       true,
        //                                       100);

        // [Fact]
        // public void RandomLongTestX1000() =>
        //     PBZip2TestCommon.RandomTestRepeat(
        //                                       PBZip2TestCommon.TestRandomDataMode.RandomBytes,
        //                                       this._console,
        //                                       this._threads,
        //                                       true,
        //                                       1000);

        [Fact]
        public void RandomLongTestWithRepeatedValuesX1() =>
            PBZip2TestCommon.RandomTestRepeat(
                                              PBZip2TestCommon.TestRandomDataMode.RandomBytesRepeatedValues,
                                              this._console,
                                              this._threads,
                                              true,
                                              1);

        [Fact]
        public void RandomLongTestWithRepeatedValuesX10() =>
            PBZip2TestCommon.RandomTestRepeat(
                                              PBZip2TestCommon.TestRandomDataMode.RandomBytesRepeatedValues,
                                              this._console,
                                              this._threads,
                                              true,
                                              10);

        // [Fact]
        // public void RandomLongTestWithRepeatedValuesX100() =>
        //     PBZip2TestCommon.RandomTestRepeat(
        //                                       PBZip2TestCommon.TestRandomDataMode.RandomBytesRepeatedValues,
        //                                       this._console,
        //                                       this._threads,
        //                                       true,
        //                                       100);

        // [Fact]
        // public void RandomLongTestWithRepeatedValuesX1000() =>
        //     PBZip2TestCommon.RandomTestRepeat(
        //                                       PBZip2TestCommon.TestRandomDataMode.RandomBytesRepeatedValues,
        //                                       this._console,
        //                                       this._threads,
        //                                       true,
        //                                       1000);

    }
}
