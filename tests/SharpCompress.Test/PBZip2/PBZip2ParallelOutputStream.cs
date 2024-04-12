using System;
using Xunit;
using Xunit.Abstractions;

namespace SharpCompress.Test.PBZip2
{
    // The purpose of these tests is to find some random edge case where the Parallel BZip2 compression fails
    // Most likely, the tests repeating for 100x or 1000x times are overkill, but they were useful back in 2022
    //      when i was investigating this issue: https://github.com/MateuszBartosiewicz/bzip2/issues/1
    // so far, I have not found any evidence of anything like this happening in the current version

    // Compress and decompress random data over and over and over and over again...
    public class PBZip2ParallelOutputStream {
        private readonly ITestOutputHelper _console;
        private readonly int _threads;

        public PBZip2ParallelOutputStream(ITestOutputHelper console)
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
                                              false,
                                              1,
                                              1000000000);

        [Fact]
        public void RandomSingleByteLongTestX1() =>
            PBZip2TestCommon.RandomTestRepeat(
                                              PBZip2TestCommon.TestRandomDataMode.SingleByte,
                                              this._console,
                                              this._threads,
                                              false,
                                              1,
                                              100000000);

        [Fact]
        public void RandomSingleByteLongTestX10() =>
            PBZip2TestCommon.RandomTestRepeat(
                                              PBZip2TestCommon.TestRandomDataMode.SingleByte,
                                              this._console,
                                              this._threads,
                                              false,
                                              10,
                                              100000000);

        [Fact]
        public void RandomLongTestX1() =>
            PBZip2TestCommon.RandomTestRepeat(
                                              PBZip2TestCommon.TestRandomDataMode.RandomBytes,
                                              this._console,
                                              this._threads,
                                              false,
                                              1);
        [Fact]
        public void RandomLongTestX10() =>
            PBZip2TestCommon.RandomTestRepeat(
                                              PBZip2TestCommon.TestRandomDataMode.RandomBytes,
                                              this._console,
                                              this._threads,
                                              false,
                                              10);
        // [Fact]
        // public void RandomLongTestX100() =>
        //     PBZip2TestCommon.RandomTestRepeat(
        //                                       PBZip2TestCommon.TestRandomDataMode.RandomBytes,
        //                                       this._console,
        //                                       this._threads,
        //                                       false,
        //                                       100);

        // [Fact]
        // public void RandomLongTestX1000() =>
        //     PBZip2TestCommon.RandomTestRepeat(
        //                                       PBZip2TestCommon.TestRandomDataMode.RandomBytes,
        //                                       this._console,
        //                                       this._threads,
        //                                       false,
        //                                       1000);

        [Fact]
        public void RandomLongTestWithRepeatedValuesX1() =>
            PBZip2TestCommon.RandomTestRepeat(
                                              PBZip2TestCommon.TestRandomDataMode.RandomBytesRepeatedValues,
                                              this._console,
                                              this._threads,
                                              false,
                                              1);

        [Fact]
        public void RandomLongTestWithRepeatedValuesX10() =>
            PBZip2TestCommon.RandomTestRepeat(
                                              PBZip2TestCommon.TestRandomDataMode.RandomBytesRepeatedValues,
                                              this._console,
                                              this._threads,
                                              false,
                                              10);

        // [Fact]
        // public void RandomLongTestWithRepeatedValuesX100() =>
        //     PBZip2TestCommon.RandomTestRepeat(
        //                                       PBZip2TestCommon.TestRandomDataMode.RandomBytesRepeatedValues,
        //                                       this._console,
        //                                       this._threads,
        //                                       false,
        //                                       100);

        // [Fact]
        // public void RandomLongTestWithRepeatedValuesX1000() =>
        //     PBZip2TestCommon.RandomTestRepeat(
        //                                       PBZip2TestCommon.TestRandomDataMode.RandomBytesRepeatedValues,
        //                                       this._console,
        //                                       this._threads,
        //                                       false,
        //                                       1000);
    }

}
