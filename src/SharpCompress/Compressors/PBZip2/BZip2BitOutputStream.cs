// Bzip2 library for .net
// Modified by drone1400
// Location: https://github.com/drone1400/bzip2
// Ported from the Java implementation by Matthew Francis: https://github.com/MateuszBartosiewicz/bzip2
// Modified from the .net implementation by Jaime Olivares: http://github.com/jaime-olivares/bzip2

using System.IO;

namespace SharpCompress.Compressors.PBZip2
{
    /// <summary>Implements a bit-wise output stream</summary>
    /// <remarks>
    /// Allows the writing of single bit booleans, unary numbers, bit
    /// strings of arbitrary length(up to 24 bits), and bit aligned 32-bit integers.A single byte at a
    /// time is written to the wrapped stream when sufficient bits have been accumulated
    /// </remarks>
    internal class BZip2BitOutputStream : IBZip2BitOutputStream
    {
        #region Private fields
        // The stream to which bits are written
		private readonly Stream outputStream;

		// A buffer of bits waiting to be written to the output stream
		private uint bitBuffer;

		// The number of bits currently buffered in bitBuffer
		private int bitCount;

		#endregion

        /// <summary>
        /// Public constructor
        /// </summary>
        /// <param name="outputStream">The OutputStream to wrap</param>
        public BZip2BitOutputStream(Stream outputStream)
        {
            this.outputStream = outputStream;
        }

        #region IBZip2BitOutputStream implementation

		public void WriteBoolean (bool value)
        {
            this.bitCount++;
			this.bitBuffer |= ((value ? 1u : 0u) << (32 - bitCount));

			if (bitCount == 8)
            {
				this.outputStream.WriteByte((byte)(bitBuffer >> 24));
				bitBuffer = 0;
				bitCount = 0;
			}
		}

		public void WriteUnary (int value)
        {
			while (value-- > 0)
            {
				this.WriteBoolean (true);
			}
			this.WriteBoolean (false);
		}

		public void WriteBits (int count,  uint value)
        {
			this.bitBuffer |= ((value << (32 - count)) >> bitCount);
			this.bitCount += count;

			while (bitCount >= 8)
            {
				this.outputStream.WriteByte((byte)(bitBuffer >> 24));
				bitBuffer <<= 8;
				bitCount -= 8;
			}
		}

		public void WriteInteger (uint value)
        {
			this.WriteBits (16, (value >> 16) & 0xffff);
			this.WriteBits (16, value & 0xffff);
		}

		public void Flush()
        {
			if (this.bitCount > 0)
				this.WriteBits (8 - this.bitCount, 0);
		}
        #endregion
    }
}
