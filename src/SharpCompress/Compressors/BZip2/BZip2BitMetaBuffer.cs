// Added by drone1400, July 2022
// Location: https://github.com/drone1400/bzip2

using System;
using System.Collections.Generic;
using System.IO;


namespace SharpCompress.Compressors.BZip2
{
    /// <summary>A collection of bit output data</summary>
    /// <remarks>
    /// Allows the writing of single bit booleans, unary numbers, bit
    /// strings of arbitrary length(up to 24 bits), and bit aligned 32-bit integers.A single byte at a
    /// time is written to a list of structures that serves as a buffer for use in parallelized
    /// execution of block compression
    /// </remarks>
    internal class BZip2BitMetaBuffer : IBZip2BitOutputStream
    {
        private struct Bzip2BitDataPair
        {
            public byte BitN { get;}
            public uint BitV { get; }
            public Bzip2BitDataPair(byte bitN, uint bitV)
            {
                this.BitN = bitN;
                this.BitV = bitV;
            }
        }

        public int DataPairCount { get => this._data.Count; }

        private List<Bzip2BitDataPair> _data;


        /// <summary>
        /// Compressed block CRC to be stored here when block is finished
        /// </summary>
        public uint BlockCrc { get => this._blockCrc; }
        private uint _blockCrc = 0;

        /// <summary>
        /// Indicates that the compression block is full
        /// </summary>
        public bool IsFull { get => this._isFull; }
        private bool _isFull = false;

        /// <summary>
        /// Block numeric id for distinguishing blocks
        /// </summary>
        public int BlockId { get => this._blockId; }
        private int _blockId;

        /// <summary>
        /// Number of bytes loaded into the block compressor
        /// </summary>
        public int LoadedBytes { get => this._loadedBytes; }
        private int _loadedBytes = 0;
        private BZip2BlockCompressor? _compressor;

        /// <summary>
        /// Public constructor
        /// </summary>
        /// <param name="blockSizeBytes"><see cref="BZip2BlockCompressor"/> block size in bytes, also initial internal buffer list capacity</param>
        /// <param name="blockId">Block number id, used to distinguish blocks in multithreadding</param>
        public BZip2BitMetaBuffer(int blockSizeBytes, int blockId)
        {
            this._data = new List<Bzip2BitDataPair>(blockSizeBytes);
            this._blockId = blockId;
            this._compressor = new BZip2BlockCompressor(this, blockSizeBytes);
        }

        /// <summary>
        /// Loads a byte into the <see cref="BZip2BlockCompressor"/>'s first RLE stage
        /// </summary>
        /// <param name="value">Byte</param>
        /// <returns>True if byte was loaded, false if byte could not be loaded because block compressor is full</returns>
        public bool LoadByte(byte value) {
            if (this._compressor == null)
            {
                return false;
            }

            if (this._compressor.Write(value)) {
                this._loadedBytes++;
                return true;
            }

            // could not load the byte, means block is full
            this._isFull = true;

            return false;
        }

        /// <summary>
        /// Loads bytes from a buffer into the <see cref="BZip2BlockCompressor"/>'s first RLE stage
        /// </summary>
        /// <param name="buff">Byte buffer</param>
        /// <param name="offset">Byte buffer offset</param>
        /// <param name="length">Number of bytes to load</param>
        /// <returns>Number of bytes actually loaded</returns>
        public int LoadBytes(byte[] buff, int offset, int length) {
            if (this._compressor == null)
            {
                return 0;
            }

            int count = this._compressor.Write(buff, offset, length);
            this._loadedBytes += count;
            if (count < length) {
                // could not load all the bytes, means block is full
                this._isFull = true;
            }
            return count;
        }

        /// <summary>
        /// Starts the actual compression
        /// </summary>
        public void CompressBytes() {
            if (this._compressor == null)
            {
                return;
            }

            this._compressor.CloseBlock();
            this._blockCrc = this._compressor.CRC;

            // set compressor to null so it can be garbage collected later
            this._compressor = null;
        }

        /// <summary>
        /// Writes all the buffer data to the real <see cref="BZip2BitOutputStream"/>
        /// </summary>
        /// <param name="stream">The real bit output stream</param>
        /// <exception cref="Exception">if an error occurs writing to the stream</exception>
        public void WriteToRealOutputStream(BZip2BitOutputStream stream) {
            for (int i = 0; i < this._data.Count; i++)
            {
                stream.WriteBits(this._data[i].BitN, this._data[i].BitV);
            }
        }

        #region IBZip2BitOutputStream implementation

        public void WriteBoolean (bool value)
        {
            this._data.Add(new Bzip2BitDataPair(1, value ? (uint)1 : (uint)0));
        }

        public void WriteUnary (int value)
        {
            while (value >= 8) {
                this._data.Add(new Bzip2BitDataPair(8, 0xFF));
                value -= 8;
            }
            switch (value) {
                case 7: this._data.Add(new Bzip2BitDataPair(7, 0x7F)); break;
                case 6: this._data.Add(new Bzip2BitDataPair(6, 0x3F)); break;
                case 5: this._data.Add(new Bzip2BitDataPair(5, 0x1F)); break;
                case 4: this._data.Add(new Bzip2BitDataPair(4, 0x0F)); break;
                case 3: this._data.Add(new Bzip2BitDataPair(3, 0x07)); break;
                case 2: this._data.Add(new Bzip2BitDataPair(2, 0x03)); break;
                case 1: this._data.Add(new Bzip2BitDataPair(1, 0x01)); break;
            }

            this._data.Add(new Bzip2BitDataPair(1, 0x00));
        }

        public void WriteBits (int count,  uint value)
        {
            this._data.Add(new Bzip2BitDataPair((byte)count, value));
        }

        public void WriteInteger (uint value)
        {
            this.WriteBits (16, (value >> 16) & 0xffff);
            this.WriteBits (16, value & 0xffff);
        }

        /// <summary>
        /// For compliance with interface, doesn't do anything
        /// </summary>
        public void Flush() {

        }

        #endregion
    }
}
