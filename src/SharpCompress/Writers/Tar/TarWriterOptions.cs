using SharpCompress.Common;

namespace SharpCompress.Writers.Tar;

public class TarWriterOptions : WriterOptions
{
    /// <summary>
    /// Indicates if archive should be finalized (by 2 empty blocks) on close.
    /// </summary>
    public bool FinalizeArchiveOnClose { get; }

    public TarHeaderWriteFormat HeaderFormat { get; }

    public TarWriterOptions(CompressionType compressionType, bool finalizeArchiveOnClose, TarHeaderWriteFormat headerFormat = TarHeaderWriteFormat.GnuTar_LongLink)
        : base(compressionType)
    {
        FinalizeArchiveOnClose = finalizeArchiveOnClose;
        HeaderFormat = headerFormat;
    }

    internal TarWriterOptions(WriterOptions options)
        : this(options.CompressionType, true) => ArchiveEncoding = options.ArchiveEncoding;
}
