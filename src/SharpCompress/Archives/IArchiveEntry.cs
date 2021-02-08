﻿using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Common;

namespace SharpCompress.Archives
{
    public interface IArchiveEntry : IEntry
    {
        /// <summary>
        /// Opens the current entry as a stream that will decompress as it is read.
        /// Read the entire stream or use SkipEntry on EntryStream.
        /// </summary>
        ValueTask<Stream> OpenEntryStreamAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// The archive can find all the parts of the archive needed to extract this entry.
        /// </summary>
        bool IsComplete { get; }

        /// <summary>
        /// The archive instance this entry belongs to
        /// </summary>
        IArchive Archive { get; }
    }
}