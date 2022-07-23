// Added by drone1400, July 2022
// Location: https://github.com/drone1400/sharpcompress/tree/drone-modifications

using System;
using System.IO;
using SharpCompress.Compressors.BZip2;
using Xunit;

namespace SharpCompress.Test.BZip2
{


    public class CustomFileTests
    {
        [Fact]
        public void TestExampleFile()
        {
            string pathIn = @"E:\TEMP\s0yrzp0v\example1";
            string pathOut1 = @"E:\TEMP\s0yrzp0v\Example1A.bzip2";
            string pathOut2 = @"E:\TEMP\s0yrzp0v\Example1B.bzip2";

            int threads = Environment.ProcessorCount;

            DateTime start;
            DateTime finish;

            start = DateTime.Now;
            using (FileStream fsi = new FileStream(pathIn, FileMode.Open, FileAccess.Read))
            using (FileStream fso = new FileStream(pathOut1, FileMode.Create, FileAccess.Write))
            using (BZip2OutputStream compressor = new BZip2OutputStream(fso, false, 9))
            {
                fsi.CopyTo(compressor);
                compressor.Close();
            }
            finish = DateTime.Now;
            Console.WriteLine("  BZip2OutputStream " + (finish - start).TotalMilliseconds);

            start = DateTime.Now;
            using (FileStream fsi = new FileStream(pathIn, FileMode.Open, FileAccess.Read))
            using (FileStream fso = new FileStream(pathOut2, FileMode.Create, FileAccess.Write))
            using (BZip2ParallelOutputStream bzip2 = new BZip2ParallelOutputStream(fso, threads, true, 9))
            {
                fsi.CopyTo(bzip2);
                bzip2.Close();
            }
            finish = DateTime.Now;
            Console.WriteLine($" BZip2ParallelOutputStream - {threads} threads " + (finish - start).TotalMilliseconds);


            using (FileStream fstest1 = new FileStream(pathOut1, FileMode.Open, FileAccess.Read))
            using (FileStream fstest2 = new FileStream(pathOut2, FileMode.Open, FileAccess.Read))
            {
                if (fstest1.Length != fstest2.Length)
                {
                    Assert.True(false, "Output streams length mismatch...");
                }

                for (long i = 0; i < fstest1.Length; i++)
                {
                    int b1 = fstest1.ReadByte();
                    if (b1 != fstest2.ReadByte())
                    {
                        Assert.True(false, $"Output stream difference between Stream 1 and 2 at byte index {i}");
                    }
                }
            }

        }
    }
}
