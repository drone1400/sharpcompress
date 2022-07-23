using System;
using System.IO;
using SharpCompress.Common;
using SharpCompress.Common.Tar.Headers;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Compressors.LZMA;
using SharpCompress.IO;
using BZip2ParallelOutputStream = SharpCompress.Compressors.BZip2.BZip2ParallelOutputStream;

namespace SharpCompress.Writers.Tar
{
    public class TarWriter : AbstractWriter
    {
        private readonly bool finalizeArchiveOnClose;

        public TarWriter(Stream destination, TarWriterOptions options)
            : base(ArchiveType.Tar, options)
        {
            finalizeArchiveOnClose = options.FinalizeArchiveOnClose;

            if (!destination.CanWrite)
            {
                throw new ArgumentException("Tars require writable streams.");
            }
            if (WriterOptions.LeaveStreamOpen)
            {
                destination = NonDisposingStream.Create(destination);
            }
            switch (options.CompressionType)
            {
                case CompressionType.None:
                    break;
                case CompressionType.BZip2:
                    {
                        int threads = Environment.ProcessorCount;
                        //destination = new BZip2Stream(destination, CompressionMode.Compress, false);
                        destination = new BZip2ParallelOutputStream(destination, threads, true, 9);
                    }
                    break;
                case CompressionType.GZip:
                    {
                        destination = new GZipStream(destination, CompressionMode.Compress);
                    }
                    break;
                case CompressionType.LZip:
                    {
                        destination = new LZipStream(destination, CompressionMode.Compress);
                    }
                    break;
                default:
                    {
                        throw new InvalidFormatException("Tar does not support compression: " + options.CompressionType);
                    }
            }
            InitalizeStream(destination);
        }

        public override void Write(string filename, Stream source, DateTime? modificationTime)
        {
            Write(filename, source, modificationTime, null);
        }

        private string NormalizeFilename(string filename)
        {
            filename = filename.Replace('\\', '/');

            int pos = filename.IndexOf(':');
            if (pos >= 0)
            {
                filename = filename.Remove(0, pos + 1);
            }

            return filename.Trim('/');
        }

        public void Write(string filename, Stream source, DateTime? modificationTime, long? size)
        {
            if (!source.CanSeek && size is null)
            {
                throw new ArgumentException("Seekable stream is required if no size is given.");
            }

            long realSize = size ?? source.Length;

            TarHeader header = new TarHeader(WriterOptions.ArchiveEncoding);

            header.LastModifiedTime = modificationTime ?? TarHeader.EPOCH;
            header.Name = NormalizeFilename(filename);
            header.Size = realSize;
            header.Write(OutputStream);

            size = source.TransferTo(OutputStream);
            PadTo512(size.Value);
        }

        private void PadTo512(long size)
        {
            int zeros = unchecked((int)(((size + 511L) & ~511L) - size));

            OutputStream.Write(stackalloc byte[zeros]);
        }

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                if (finalizeArchiveOnClose)
                {
                    OutputStream.Write(stackalloc byte[1024]);
                }
                switch (OutputStream)
                {
                    case BZip2ParallelOutputStream b:
                        {
                            b.Close();
                            break;
                        }
                    case BZip2OutputStream b:
                        {
                            b.Close();
                            break;
                        }
                    // case BZip2Stream b:
                    //     {
                    //         b.Finish();
                    //         break;
                    //     }
                    case LZipStream l:
                        {
                            l.Finish();
                            break;
                        }
                }
            }
            base.Dispose(isDisposing);
        }
    }
}
