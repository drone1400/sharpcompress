using System;
using System.IO;

/*
 * Copyright 2001,2004-2005 The Apache Software Foundation
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

/*
 * This package is based on the work done by Keiron Liddle, Aftex Software
 * <keiron@aftexsw.com> to whom the Ant project is very grateful for his
 * great code.
 */

/*
 * Modified by drone1400, 2022-07-17
 * Adapted from CBzip2OutputStream.cs to process a single block at a time
 */

#nullable disable

namespace SharpCompress.Compressors.BZip2Old
{

    public sealed class BZip2BlockCompressor
    {
        private const int SETMASK = (1 << 21);
        private const int CLEARMASK = (~SETMASK);
        private const int GREATER_ICOST = 15;
        private const int LESSER_ICOST = 0;
        private const int SMALL_THRESH = 20;
        private const int DEPTH_THRESH = 10;

        /*
        If you are ever unlucky/improbable enough
        to get a stack overflow whilst sorting,
        increase the following constant and try
        again.  In practice I have never seen the
        stack go above 27 elems, so the following
        limit seems very generous.
        */
        private const int QSORT_STACK_SIZE = 1000;


        public int BlockCrc { get => this._blockCrc; }
        private int _blockCrc;
        private readonly CRC _mCrc = new CRC();

        public bool IsEmpty { get => this._runLastIndex == -1; }
        public bool IsFull { get => this._isFull; }
        private bool _isFull = false;

        public bool IsFinalized { get => this._isFinalized; }
        private bool _isFinalized = false;




        // internal buffers...
        private char[] _block;
        private int[] _quadrant;
        private int[] _zptr;
        private short[] _szptr;
        private int[] _ftab;

        private IBzip2BitOutputStream _output;

        private int _maxBlockSize;

        #region  Constructor and initialization

        public BZip2BlockCompressor(IBzip2BitOutputStream outputBuffer, int inBlockSize)
        {
            this._output = outputBuffer;

            this._workFactor = 50;

            if (inBlockSize > 9) inBlockSize = 9;
            if (inBlockSize < 1) inBlockSize = 1;

            this._maxBlockSize = BZip2Constants.BASE_BLOCK_SIZE * inBlockSize;

            AllocateCompressStructures();

            this._nBlocksRandomised = 0;

            this._mCrc.InitialiseCRC();

            this._runLastIndex = -1;
            this._runLength = 0;
            this._runByteValue = 0;

            for (int i = 0; i < 256; i++)
            {
                this._inUse[i] = false;
            }
        }

        private void AllocateCompressStructures()
        {
            this._block = new char[(this._maxBlockSize + 1 + BZip2Constants.NUM_OVERSHOOT_BYTES)];
            this._quadrant = new int[(this._maxBlockSize + BZip2Constants.NUM_OVERSHOOT_BYTES)];
            this._zptr = new int[this._maxBlockSize];
            this._ftab = new int[65537];

            /*
            The back end needs a place to store the MTF values
            whilst it calculates the coding tables.  We could
            put them in the zptr array.  However, these values
            will fit in a short, so we overlay szptr at the
            start of zptr, in the hope of reducing the number
            of cache misses induced by the multiple traversals
            of the MTF values when calculating coding tables.
            Seems to improve compression speed by about 1%.
            */
            //    szptr = zptr;

            this._szptr = new short[2 * this._maxBlockSize];
        }

        #endregion

        #region Public Methods
        public bool Write(byte bv)
        {
            if (this._runLastIndex + 6 >= this._maxBlockSize)
            {
                // do not allow byte to be written if writing the run would exceed max block size...
                return false;
            }

            if (this._runLength == 0)
            {
                // run length is empty, start new run
                this._runByteValue = bv;
                this._runLength = 1;
            }
            else if (this._runByteValue == bv)
            {
                // run continues
                this._runLength++;
                if (this._runLength > 254)
                {
                    // max run lenght reached
                    this.WriteRun();
                    this._runLength = 0;
                }
            }
            else
            {
                // run ended due to different value, write run and start new run
                this.WriteRun();
                this._runByteValue = bv;
                this._runLength = 1;
            }

            // byte was added to run successfully
            return true;
        }

        public int Write(byte[] buffer, int offset, int length)
        {
            int count = 0;
            while (length > 0)
            {
                if (!this.Write(buffer[offset++]))
                {
                    break;
                }
                count++;
                length--;
            }

            return count;
        }


        public void CloseBlock()
        {
            if (this._isFinalized)
            {
                throw new Exception("BZip2BlockCompressor - CompressBlock method called more than once...");
            }

            this._isFinalized = true;

            // write if ongoing run exists
            if (this._runLength > 0) this.WriteRun();

            // set final CRC
            this._blockCrc = (int)this._mCrc.GetFinalCRC();

            /* sort the block and establish posn of original string */
            DoReversibleTransformation();

            /*
            A 6-byte block header, the value chosen arbitrarily
            as 0x314159265359 :-).  A 32 bit value does not really
            give a strong enough guarantee that the value will not
            appear by chance in the compressed datastream.  Worst-case
            probability of this event, for a 900k block, is about
            2.0e-3 for 32 bits, 1.0e-5 for 40 bits and 4.0e-8 for 48 bits.
            For a compressed file of size 100Gb -- about 100000 blocks --
            only a 48-bit marker will do.  NB: normal compression/
            decompression do *not* rely on these statistical properties.
            They are only important when trying to recover blocks from
            damaged files.
            */
            this._output.WriteByte(0x31);
            this._output.WriteByte(0x41);
            this._output.WriteByte(0x59);
            this._output.WriteByte(0x26);
            this._output.WriteByte(0x53);
            this._output.WriteByte(0x59);

            /* Now the block's CRC, so it is in a known place. */
            this._output.WriteInt(this._blockCrc);

            /* Now a single bit indicating randomisation. */
            if (this._blockRandomised)
            {
                this._output.WriteBits(1, 1);
                this._nBlocksRandomised++;
            }
            else
            {
                this._output.WriteBits(1, 0);
            }

            // Encode ReversibleTransform Origin Pointer
            EncodeOriginPointer();

            // MoveToFront, generate and finally send values
            this.GenerateMtfValues();
            this.SendMtfValues();
        }

        #endregion


        #region RLE first pass

        /*
        index of the last char in the block, so
        the block size == last + 1.
        */
        private int _runLastIndex;
        private int _runByteValue;
        private int _runLength;

        private void WriteRun()
        {
            if ((this._runLastIndex + (this._runLength < 4 ? this._runLength + 1 : 5)) > this._maxBlockSize)
            {
                throw new Exception("BZip2BlockCompressor internal error during WriteRun, Maximum BlockSize exceeded");
            }

            // byte value is in use
            this._inUse[this._runByteValue] = true;

            // update CRC
            for (int i = 0; i < this._runLength; i++)
            {
                this._mCrc.UpdateCRC((byte)this._runByteValue);
            }

            switch (this._runLength)
            {
                case 1:
                    this._runLastIndex++;
                    this._block[this._runLastIndex + 1] = (char)this._runByteValue;
                    break;
                case 2:
                    this._runLastIndex++;
                    this._block[this._runLastIndex + 1] = (char)this._runByteValue;
                    this._runLastIndex++;
                    this._block[this._runLastIndex + 1] = (char)this._runByteValue;
                    break;
                case 3:
                    this._runLastIndex++;
                    this._block[this._runLastIndex + 1] = (char)this._runByteValue;
                    this._runLastIndex++;
                    this._block[this._runLastIndex + 1] = (char)this._runByteValue;
                    this._runLastIndex++;
                    this._block[this._runLastIndex + 1] = (char)this._runByteValue;
                    break;
                default:
                    this._inUse[this._runLength - 4] = true;
                    this._runLastIndex++;
                    this._block[this._runLastIndex + 1] = (char)this._runByteValue;
                    this._runLastIndex++;
                    this._block[this._runLastIndex + 1] = (char)this._runByteValue;
                    this._runLastIndex++;
                    this._block[this._runLastIndex + 1] = (char)this._runByteValue;
                    this._runLastIndex++;
                    this._block[this._runLastIndex + 1] = (char)this._runByteValue;
                    this._runLastIndex++;
                    this._block[this._runLastIndex + 1] = (char)(this._runLength - 4);
                    break;
            }
        }

        #endregion

        #region HB related

        private static void HbMakeCodeLengths(char[] len, int[] freq,
                                                int alphaSize, int maxLen)
        {
            /*
            Nodes and heap entries run from 1.  Entry 0
            for both the heap and nodes is a sentinel.
            */
            int nNodes, nHeap, n1, n2, i, j, k;
            bool tooLong;

            Span<int> heap = stackalloc int[BZip2Constants.MAX_ALPHA_SIZE + 2]; // 1040 bytes
            Span<int> weight = stackalloc int[BZip2Constants.MAX_ALPHA_SIZE * 2];  // 1040 bytes
            Span<int> parent = stackalloc int[BZip2Constants.MAX_ALPHA_SIZE * 2];  // 1040 bytes

            for (i = 0; i < alphaSize; i++)
            {
                weight[i + 1] = (freq[i] == 0 ? 1 : freq[i]) << 8;
            }

            while (true)
            {
                nNodes = alphaSize;
                nHeap = 0;

                heap[0] = 0;
                weight[0] = 0;
                parent[0] = -2;

                for (i = 1; i <= alphaSize; i++)
                {
                    parent[i] = -1;
                    nHeap++;
                    heap[nHeap] = i;
                    {
                        int zz, tmp;
                        zz = nHeap;
                        tmp = heap[zz];
                        while (weight[tmp] < weight[heap[zz >> 1]])
                        {
                            heap[zz] = heap[zz >> 1];
                            zz >>= 1;
                        }
                        heap[zz] = tmp;
                    }
                }
                if (!(nHeap < (BZip2Constants.MAX_ALPHA_SIZE + 2)))
                {
                    throw new Exception("BZip2BlockCompressor internal error during HbMakeCodeLengths");
                }

                while (nHeap > 1)
                {
                    n1 = heap[1];
                    heap[1] = heap[nHeap];
                    nHeap--;
                    {
                        int zz = 0, yy = 0, tmp = 0;
                        zz = 1;
                        tmp = heap[zz];
                        while (true)
                        {
                            yy = zz << 1;
                            if (yy > nHeap)
                            {
                                break;
                            }
                            if (yy < nHeap
                                && weight[heap[yy + 1]] < weight[heap[yy]])
                            {
                                yy++;
                            }
                            if (weight[tmp] < weight[heap[yy]])
                            {
                                break;
                            }
                            heap[zz] = heap[yy];
                            zz = yy;
                        }
                        heap[zz] = tmp;
                    }
                    n2 = heap[1];
                    heap[1] = heap[nHeap];
                    nHeap--;
                    {
                        int zz = 0, yy = 0, tmp = 0;
                        zz = 1;
                        tmp = heap[zz];
                        while (true)
                        {
                            yy = zz << 1;
                            if (yy > nHeap)
                            {
                                break;
                            }
                            if (yy < nHeap
                                && weight[heap[yy + 1]] < weight[heap[yy]])
                            {
                                yy++;
                            }
                            if (weight[tmp] < weight[heap[yy]])
                            {
                                break;
                            }
                            heap[zz] = heap[yy];
                            zz = yy;
                        }
                        heap[zz] = tmp;
                    }
                    nNodes++;
                    parent[n1] = parent[n2] = nNodes;

                    weight[nNodes] = (int)((uint)((weight[n1] & 0xffffff00)
                                                  + (weight[n2] & 0xffffff00))
                                           | (uint)(1 + (((weight[n1] & 0x000000ff) >
                                                          (weight[n2] & 0x000000ff))
                                                             ? (weight[n1] & 0x000000ff)
                                                             : (weight[n2] & 0x000000ff))));

                    parent[nNodes] = -1;
                    nHeap++;
                    heap[nHeap] = nNodes;
                    {
                        int zz = 0, tmp = 0;
                        zz = nHeap;
                        tmp = heap[zz];
                        while (weight[tmp] < weight[heap[zz >> 1]])
                        {
                            heap[zz] = heap[zz >> 1];
                            zz >>= 1;
                        }
                        heap[zz] = tmp;
                    }
                }
                if (!(nNodes < (BZip2Constants.MAX_ALPHA_SIZE * 2)))
                {
                    throw new Exception("BZip2BlockCompressor internal error during HbMakeCodeLengths");
                }

                tooLong = false;
                for (i = 1; i <= alphaSize; i++)
                {
                    j = 0;
                    k = i;
                    while (parent[k] >= 0)
                    {
                        k = parent[k];
                        j++;
                    }
                    len[i - 1] = (char)j;
                    if (j > maxLen)
                    {
                        tooLong = true;
                    }
                }

                if (!tooLong)
                {
                    break;
                }

                for (i = 1; i < alphaSize; i++)
                {
                    j = weight[i] >> 8;
                    j = 1 + (j / 2);
                    weight[i] = j << 8;
                }
            }
        }

        private static void HbAssignCodes(int[] code, char[] length, int minLen,
                                   int maxLen, int alphaSize)
        {
            int n, vec, i;

            vec = 0;
            for (n = minLen; n <= maxLen; n++)
            {
                for (i = 0; i < alphaSize; i++)
                {
                    if (length[i] == n)
                    {
                        code[i] = vec;
                        vec++;
                    }
                }
                ;
                vec <<= 1;
            }
        }

        #endregion

        #region MTF related

        // MTF related Fields

        private readonly bool[] _inUse = new bool[256];
        private int _nInUse;

        private int _nMtf;
        private readonly int[] _mtfFreq = new int[BZip2Constants.MAX_ALPHA_SIZE];

        private readonly char[] _seqToUnseq = new char[256];
        private readonly char[] _unseqToSeq = new char[256];

        private readonly char[] _selector = new char[BZip2Constants.MAX_SELECTORS];
        private readonly char[] _selectorMtf = new char[BZip2Constants.MAX_SELECTORS];

        private void MakeMaps()
        {
            int i;
            this._nInUse = 0;
            for (i = 0; i < 256; i++)
            {
                if (this._inUse[i])
                {
                    this._seqToUnseq[this._nInUse] = (char)i;
                    this._unseqToSeq[i] = (char)this._nInUse;
                    this._nInUse++;
                }
            }
        }

        private void GenerateMtfValues()
        {
            char[] yy = new char[256];
            int i, j;
            char tmp;
            char tmp2;
            int zPend;
            int wr;
            int eob;

            MakeMaps();
            eob = this._nInUse + 1;

            for (i = 0; i <= eob; i++)
            {
                this._mtfFreq[i] = 0;
            }

            wr = 0;
            zPend = 0;
            for (i = 0; i < this._nInUse; i++)
            {
                yy[i] = (char)i;
            }

            for (i = 0; i <= this._runLastIndex; i++)
            {
                char llI;

                llI = this._unseqToSeq[this._block[this._zptr[i]]];

                j = 0;
                tmp = yy[j];
                while (llI != tmp)
                {
                    j++;
                    tmp2 = tmp;
                    tmp = yy[j];
                    yy[j] = tmp2;
                }
                ;
                yy[0] = tmp;

                if (j == 0)
                {
                    zPend++;
                }
                else
                {
                    if (zPend > 0)
                    {
                        zPend--;
                        while (true)
                        {
                            switch (zPend % 2)
                            {
                                case 0:
                                    this._szptr[wr] = BZip2Constants.RUNA;
                                    wr++;
                                    this._mtfFreq[BZip2Constants.RUNA]++;
                                    break;
                                case 1:
                                    this._szptr[wr] = BZip2Constants.RUNB;
                                    wr++;
                                    this._mtfFreq[BZip2Constants.RUNB]++;
                                    break;
                            }
                            ;
                            if (zPend < 2)
                            {
                                break;
                            }
                            zPend = (zPend - 2) / 2;
                        }
                        ;
                        zPend = 0;
                    }
                    this._szptr[wr] = (short)(j + 1);
                    wr++;
                    this._mtfFreq[j + 1]++;
                }
            }

            if (zPend > 0)
            {
                zPend--;
                while (true)
                {
                    switch (zPend % 2)
                    {
                        case 0:
                            this._szptr[wr] = BZip2Constants.RUNA;
                            wr++;
                            this._mtfFreq[BZip2Constants.RUNA]++;
                            break;
                        case 1:
                            this._szptr[wr] = BZip2Constants.RUNB;
                            wr++;
                            this._mtfFreq[BZip2Constants.RUNB]++;
                            break;
                    }
                    if (zPend < 2)
                    {
                        break;
                    }
                    zPend = (zPend - 2) / 2;
                }
            }

            this._szptr[wr] = (short)eob;
            wr++;
            this._mtfFreq[eob]++;

            this._nMtf = wr;
        }

        private void SendMtfValues()
        {
            char[][] len = CBZip2InputStream.InitCharArray(BZip2Constants.N_GROUPS, BZip2Constants.MAX_ALPHA_SIZE);

            int v, t, i, j, gs, ge, totc, bt, bc, iter;
            int nSelectors = 0, alphaSize, minLen, maxLen, selCtr;
            int nGroups; //, nBytes;

            alphaSize = this._nInUse + 2;
            for (t = 0; t < BZip2Constants.N_GROUPS; t++)
            {
                for (v = 0; v < alphaSize; v++)
                {
                    len[t][v] = (char)GREATER_ICOST;
                }
            }

            /* Decide how many coding tables to use */
            if (this._nMtf <= 0)
            {
                throw new Exception("BZip2BlockCompressor internal error during SendMtfValues");
            }

            if (this._nMtf < 200)
            {
                nGroups = 2;
            }
            else if (this._nMtf < 600)
            {
                nGroups = 3;
            }
            else if (this._nMtf < 1200)
            {
                nGroups = 4;
            }
            else if (this._nMtf < 2400)
            {
                nGroups = 5;
            }
            else
            {
                nGroups = 6;
            }

            /* Generate an initial set of coding tables */
            {
                int nPart, remF, tFreq, aFreq;

                nPart = nGroups;
                remF = this._nMtf;
                gs = 0;
                while (nPart > 0)
                {
                    tFreq = remF / nPart;
                    ge = gs - 1;
                    aFreq = 0;
                    while (aFreq < tFreq && ge < alphaSize - 1)
                    {
                        ge++;
                        aFreq += this._mtfFreq[ge];
                    }

                    if (ge > gs && nPart != nGroups && nPart != 1
                        && ((nGroups - nPart) % 2 == 1))
                    {
                        aFreq -= this._mtfFreq[ge];
                        ge--;
                    }

                    for (v = 0; v < alphaSize; v++)
                    {
                        if (v >= gs && v <= ge)
                        {
                            len[nPart - 1][v] = (char)LESSER_ICOST;
                        }
                        else
                        {
                            len[nPart - 1][v] = (char)GREATER_ICOST;
                        }
                    }

                    nPart--;
                    gs = ge + 1;
                    remF -= aFreq;
                }
            }

            int[][] rfreq = CBZip2InputStream.InitIntArray(BZip2Constants.N_GROUPS, BZip2Constants.MAX_ALPHA_SIZE);
            int[] fave = new int[BZip2Constants.N_GROUPS];
            short[] cost = new short[BZip2Constants.N_GROUPS];
            /*
            Iterate up to N_ITERS times to improve the tables.
            */
            for (iter = 0; iter < BZip2Constants.N_ITERS; iter++)
            {
                for (t = 0; t < nGroups; t++)
                {
                    fave[t] = 0;
                }

                for (t = 0; t < nGroups; t++)
                {
                    for (v = 0; v < alphaSize; v++)
                    {
                        rfreq[t][v] = 0;
                    }
                }

                nSelectors = 0;
                totc = 0;
                gs = 0;
                while (true)
                {
                    /* Set group start & end marks. */
                    if (gs >= this._nMtf)
                    {
                        break;
                    }
                    ge = gs + BZip2Constants.G_SIZE - 1;
                    if (ge >= this._nMtf)
                    {
                        ge = this._nMtf - 1;
                    }

                    /*
                    Calculate the cost of this group as coded
                    by each of the coding tables.
                    */
                    for (t = 0; t < nGroups; t++)
                    {
                        cost[t] = 0;
                    }

                    if (nGroups == 6)
                    {
                        short cost0, cost1, cost2, cost3, cost4, cost5;
                        cost0 = cost1 = cost2 = cost3 = cost4 = cost5 = 0;
                        for (i = gs; i <= ge; i++)
                        {
                            short icv = this._szptr[i];
                            cost0 += (short)len[0][icv];
                            cost1 += (short)len[1][icv];
                            cost2 += (short)len[2][icv];
                            cost3 += (short)len[3][icv];
                            cost4 += (short)len[4][icv];
                            cost5 += (short)len[5][icv];
                        }
                        cost[0] = cost0;
                        cost[1] = cost1;
                        cost[2] = cost2;
                        cost[3] = cost3;
                        cost[4] = cost4;
                        cost[5] = cost5;
                    }
                    else
                    {
                        for (i = gs; i <= ge; i++)
                        {
                            short icv = this._szptr[i];
                            for (t = 0; t < nGroups; t++)
                            {
                                cost[t] += (short)len[t][icv];
                            }
                        }
                    }

                    /*
                    Find the coding table which is best for this group,
                    and record its identity in the selector table.
                    */
                    bc = 999999999;
                    bt = -1;
                    for (t = 0; t < nGroups; t++)
                    {
                        if (cost[t] < bc)
                        {
                            bc = cost[t];
                            bt = t;
                        }
                    }
                    ;
                    totc += bc;
                    fave[bt]++;
                    this._selector[nSelectors] = (char)bt;
                    nSelectors++;

                    /*
                    Increment the symbol frequencies for the selected table.
                    */
                    for (i = gs; i <= ge; i++)
                    {
                        rfreq[bt][this._szptr[i]]++;
                    }

                    gs = ge + 1;
                }

                /*
                Recompute the tables based on the accumulated frequencies.
                */
                for (t = 0; t < nGroups; t++)
                {
                    HbMakeCodeLengths(len[t], rfreq[t], alphaSize, 20);
                }
            }

            rfreq = null;
            fave = null;
            cost = null;

            if (!(nGroups < 8))
            {
                throw new Exception("BZip2BlockCompressor internal error during SendMtfValues");
            }
            if (!(nSelectors < 32768 && nSelectors <= (2 + (900000 / BZip2Constants.G_SIZE))))
            {
                throw new Exception("BZip2BlockCompressor internal error during SendMtfValues");
            }

            /* Compute MTF values for the selectors. */
            {
                char[] pos = new char[BZip2Constants.N_GROUPS];
                char llI, tmp2, tmp;
                for (i = 0; i < nGroups; i++)
                {
                    pos[i] = (char)i;
                }
                for (i = 0; i < nSelectors; i++)
                {
                    llI = this._selector[i];
                    j = 0;
                    tmp = pos[j];
                    while (llI != tmp)
                    {
                        j++;
                        tmp2 = tmp;
                        tmp = pos[j];
                        pos[j] = tmp2;
                    }
                    pos[0] = tmp;
                    this._selectorMtf[i] = (char)j;
                }
            }

            int[][] code = CBZip2InputStream.InitIntArray(BZip2Constants.N_GROUPS, BZip2Constants.MAX_ALPHA_SIZE);

            /* Assign actual codes for the tables. */
            for (t = 0; t < nGroups; t++)
            {
                minLen = 32;
                maxLen = 0;
                for (i = 0; i < alphaSize; i++)
                {
                    if (len[t][i] > maxLen)
                    {
                        maxLen = len[t][i];
                    }
                    if (len[t][i] < minLen)
                    {
                        minLen = len[t][i];
                    }
                }
                if (maxLen > 20)
                {
                    throw new Exception("BZip2BlockCompressor internal error during SendMtfValues");
                }
                if (minLen < 1)
                {
                    throw new Exception("BZip2BlockCompressor internal error during SendMtfValues");
                }
                HbAssignCodes(code[t], len[t], minLen, maxLen, alphaSize);
            }

            /* Transmit the mapping table. */
            {
                bool[] inUse16 = new bool[16];
                for (i = 0; i < 16; i++)
                {
                    inUse16[i] = false;
                    for (j = 0; j < 16; j++)
                    {
                        if (this._inUse[i * 16 + j])
                        {
                            inUse16[i] = true;
                        }
                    }
                }

                //nBytes = bytesOut;
                for (i = 0; i < 16; i++)
                {
                    if (inUse16[i])
                    {
                        this._output.WriteBits(1, 1);
                    }
                    else
                    {
                        this._output.WriteBits(1, 0);
                    }
                }

                for (i = 0; i < 16; i++)
                {
                    if (inUse16[i])
                    {
                        for (j = 0; j < 16; j++)
                        {
                            if (this._inUse[i * 16 + j])
                            {
                                this._output.WriteBits(1, 1);
                            }
                            else
                            {
                                this._output.WriteBits(1, 0);
                            }
                        }
                    }
                }
            }

            /* Now the selectors. */
            //nBytes = bytesOut;
            this._output.WriteBits(3, nGroups);
            this._output.WriteBits(15, nSelectors);
            for (i = 0; i < nSelectors; i++)
            {
                for (j = 0; j < this._selectorMtf[i]; j++)
                {
                    this._output.WriteBits(1, 1);
                }
                this._output.WriteBits(1, 0);
            }

            /* Now the coding tables. */
            //nBytes = bytesOut;

            for (t = 0; t < nGroups; t++)
            {
                int curr = len[t][0];
                this._output.WriteBits(5, curr);
                for (i = 0; i < alphaSize; i++)
                {
                    while (curr < len[t][i])
                    {
                        this._output.WriteBits(2, 2);
                        curr++; /* 10 */
                    }
                    while (curr > len[t][i])
                    {
                        this._output.WriteBits(2, 3);
                        curr--; /* 11 */
                    }
                    this._output.WriteBits(1, 0);
                }
            }

            /* And finally, the block data proper */
            //nBytes = bytesOut;
            selCtr = 0;
            gs = 0;
            while (true)
            {
                if (gs >= this._nMtf)
                {
                    break;
                }
                ge = gs + BZip2Constants.G_SIZE - 1;
                if (ge >= this._nMtf)
                {
                    ge = this._nMtf - 1;
                }
                for (i = gs; i <= ge; i++)
                {
                    this._output.WriteBits((byte)len[this._selector[selCtr]][this._szptr[i]],
                                           code[this._selector[selCtr]][this._szptr[i]]);
                }

                gs = ge + 1;
                selCtr++;
            }
            if (!(selCtr == nSelectors))
            {
                throw new Exception("BZip2BlockCompressor internal error during SendMtfValues");
            }
        }

        #endregion

        #region Reversible Transform & Main Sort

        /*
         * Used when sorting.  If too many long comparisons
         * happen, we stop sorting, randomise the block
         * slightly, and try again.
         */
        private readonly int _workFactor;
        private int _workDone;
        private int _workLimit;
        private bool _firstAttempt;
        private bool _blockRandomised;
        private int _nBlocksRandomised;

        private void EncodeOriginPointer()
        {
            // index in zptr[] of original string after sorting.

            int origPtr = -1;
            for (int i = 0; i <= this._runLastIndex; i++)
            {
                if (this._zptr[i] == 0)
                {
                    origPtr = i;
                    break;
                }
            }

            if (origPtr == -1)
            {
                throw new Exception("BZip2BlockCompressor internal error during DoReversibleTransformation, Original Pointer could not be calculated");
            }

            this._output.WriteBits(24, origPtr);
        }

        private void DoReversibleTransformation()
        {
            this._workLimit = this._workFactor * this._runLastIndex;
            this._workDone = 0;
            this._blockRandomised = false;
            this._firstAttempt = true;

            MainSort();

            if (this._workDone > this._workLimit && this._firstAttempt)
            {
                RandomiseBlock();
                this._workLimit = this._workDone = 0;
                this._blockRandomised = true;
                this._firstAttempt = false;
                MainSort();
            }
        }

        private void RandomiseBlock()
        {
            int i;
            int rNToGo = 0;
            int rTPos = 0;
            for (i = 0; i < 256; i++)
            {
                this._inUse[i] = false;
            }

            for (i = 0; i <= this._runLastIndex; i++)
            {
                if (rNToGo == 0)
                {
                    rNToGo = (char)BZip2Constants.RAND_NUMS[rTPos];
                    rTPos++;
                    if (rTPos == 512)
                    {
                        rTPos = 0;
                    }
                }
                rNToGo--;
                this._block[i + 1] ^= (char)((rNToGo == 1) ? 1 : 0);

                // handle 16 bit signed numbers
                this._block[i + 1] &= (char)0xFF;

                this._inUse[this._block[i + 1]] = true;
            }
        }

        private void MainSort()
        {
            int i, j, ss, sb;
            Span<int> runningOrder = stackalloc int[256];
            Span<int> copy = stackalloc int[256];
            bool[] bigDone = new bool[256];
            int c1, c2;
            int numQSorted;

            /*
            In the various block-sized structures, live data runs
            from 0 to last+NUM_OVERSHOOT_BYTES inclusive.  First,
            set up the overshoot area for block.
            */

            //   if (verbosity >= 4) fprintf ( stderr, "   sort initialise ...\n" );
            for (i = 0; i < BZip2Constants.NUM_OVERSHOOT_BYTES; i++)
            {
                this._block[this._runLastIndex + i + 2] = this._block[(i % (this._runLastIndex + 1)) + 1];
            }
            for (i = 0; i <= this._runLastIndex + BZip2Constants.NUM_OVERSHOOT_BYTES; i++)
            {
                this._quadrant[i] = 0;
            }

            this._block[0] = this._block[this._runLastIndex + 1];

            if (this._runLastIndex < 4000)
            {
                /*
                Use SimpleSort(), since the full sorting mechanism
                has quite a large constant overhead.
                */
                for (i = 0; i <= this._runLastIndex; i++)
                {
                    this._zptr[i] = i;
                }
                this._firstAttempt = false;
                this._workDone = this._workLimit = 0;
                SimpleSort(0, this._runLastIndex, 0);
            }
            else
            {
                numQSorted = 0;
                for (i = 0; i <= 255; i++)
                {
                    bigDone[i] = false;
                }

                for (i = 0; i <= 65536; i++)
                {
                    this._ftab[i] = 0;
                }

                c1 = this._block[0];
                for (i = 0; i <= this._runLastIndex; i++)
                {
                    c2 = this._block[i + 1];
                    this._ftab[(c1 << 8) + c2]++;
                    c1 = c2;
                }

                for (i = 1; i <= 65536; i++)
                {
                    this._ftab[i] += this._ftab[i - 1];
                }

                c1 = this._block[1];
                for (i = 0; i < this._runLastIndex; i++)
                {
                    c2 = this._block[i + 2];
                    j = (c1 << 8) + c2;
                    c1 = c2;
                    this._ftab[j]--;
                    this._zptr[this._ftab[j]] = i;
                }

                j = ((this._block[this._runLastIndex + 1]) << 8) + (this._block[1]);
                this._ftab[j]--;
                this._zptr[this._ftab[j]] = this._runLastIndex;

                /*
                Now ftab contains the first loc of every small bucket.
                Calculate the running order, from smallest to largest
                big bucket.
                */

                for (i = 0; i <= 255; i++)
                {
                    runningOrder[i] = i;
                }

                {
                    int vv;
                    int h = 1;
                    do
                    {
                        h = 3 * h + 1;
                    }
                    while (h <= 256);
                    do
                    {
                        h = h / 3;
                        for (i = h; i <= 255; i++)
                        {
                            vv = runningOrder[i];
                            j = i;
                            while ((this._ftab[((runningOrder[j - h]) + 1) << 8]
                                    - this._ftab[(runningOrder[j - h]) << 8]) >
                                   (this._ftab[((vv) + 1) << 8] - this._ftab[(vv) << 8]))
                            {
                                runningOrder[j] = runningOrder[j - h];
                                j = j - h;
                                if (j <= (h - 1))
                                {
                                    break;
                                }
                            }
                            runningOrder[j] = vv;
                        }
                    }
                    while (h != 1);
                }

                /*
                The main sorting loop.
                */
                for (i = 0; i <= 255; i++)
                {
                    /*
                    Process big buckets, starting with the least full.
                    */
                    ss = runningOrder[i];

                    /*
                    Complete the big bucket [ss] by quicksorting
                    any unsorted small buckets [ss, j].  Hopefully
                    previous pointer-scanning phases have already
                    completed many of the small buckets [ss, j], so
                    we don't have to sort them at all.
                    */
                    for (j = 0; j <= 255; j++)
                    {
                        sb = (ss << 8) + j;
                        if (!((this._ftab[sb] & SETMASK) == SETMASK))
                        {
                            int lo = this._ftab[sb] & CLEARMASK;
                            int hi = (this._ftab[sb + 1] & CLEARMASK) - 1;
                            if (hi > lo)
                            {
                                QSort3(lo, hi, 2);
                                numQSorted += (hi - lo + 1);
                                if (this._workDone > this._workLimit && this._firstAttempt)
                                {
                                    return;
                                }
                            }
                            this._ftab[sb] |= SETMASK;
                        }
                    }

                    /*
                    The ss big bucket is now done.  Record this fact,
                    and update the quadrant descriptors.  Remember to
                    update quadrants in the overshoot area too, if
                    necessary.  The "if (i < 255)" test merely skips
                    this updating for the last bucket processed, since
                    updating for the last bucket is pointless.
                    */
                    bigDone[ss] = true;

                    if (i < 255)
                    {
                        int bbStart = this._ftab[ss << 8] & CLEARMASK;
                        int bbSize = (this._ftab[(ss + 1) << 8] & CLEARMASK) - bbStart;
                        int shifts = 0;

                        while ((bbSize >> shifts) > 65534)
                        {
                            shifts++;
                        }

                        for (j = 0; j < bbSize; j++)
                        {
                            int a2Update = this._zptr[bbStart + j];
                            int qVal = (j >> shifts);
                            this._quadrant[a2Update] = qVal;
                            if (a2Update < BZip2Constants.NUM_OVERSHOOT_BYTES)
                            {
                                this._quadrant[a2Update + this._runLastIndex + 1] = qVal;
                            }
                        }

                        if (!(((bbSize - 1) >> shifts) <= 65535))
                        {
                            throw new Exception("BZip2BlockCompressor internal error during MainSort");
                        }
                    }

                    /*
                    Now scan this big bucket so as to synthesise the
                    sorted order for small buckets [t, ss] for all t != ss.
                    */
                    for (j = 0; j <= 255; j++)
                    {
                        copy[j] = this._ftab[(j << 8) + ss] & CLEARMASK;
                    }

                    for (j = this._ftab[ss << 8] & CLEARMASK;
                         j < (this._ftab[(ss + 1) << 8] & CLEARMASK);
                         j++)
                    {
                        c1 = this._block[this._zptr[j]];
                        if (!bigDone[c1])
                        {
                            this._zptr[copy[c1]] = this._zptr[j] == 0 ? this._runLastIndex : this._zptr[j] - 1;
                            copy[c1]++;
                        }
                    }

                    for (j = 0; j <= 255; j++)
                    {
                        this._ftab[(j << 8) + ss] |= SETMASK;
                    }
                }
            }
        }

        #endregion

        #region SimpleSort

        /*
        Knuth's increments seem to work better
        than Incerpi-Sedgewick here.  Possibly
        because the number of elems to sort is
        usually small, typically <= 20.
        */

        private readonly int[] _incs =
        {
            1, 4, 13, 40, 121, 364, 1093, 3280,
            9841, 29524, 88573, 265720,
            797161, 2391484
        };

        private void SimpleSort(int lo, int hi, int d)
        {
            int i, j, h, bigN, hp;
            int v;

            bigN = hi - lo + 1;
            if (bigN < 2)
            {
                return;
            }

            hp = 0;
            while (this._incs[hp] < bigN)
            {
                hp++;
            }
            hp--;

            for (; hp >= 0; hp--)
            {
                h = this._incs[hp];

                i = lo + h;
                while (true)
                {
                    /* copy 1 */
                    if (i > hi)
                    {
                        break;
                    }
                    v = this._zptr[i];
                    j = i;
                    while (FullGtU(this._zptr[j - h] + d, v + d))
                    {
                        this._zptr[j] = this._zptr[j - h];
                        j = j - h;
                        if (j <= (lo + h - 1))
                        {
                            break;
                        }
                    }
                    this._zptr[j] = v;
                    i++;

                    /* copy 2 */
                    if (i > hi)
                    {
                        break;
                    }
                    v = this._zptr[i];
                    j = i;
                    while (FullGtU(this._zptr[j - h] + d, v + d))
                    {
                        this._zptr[j] = this._zptr[j - h];
                        j = j - h;
                        if (j <= (lo + h - 1))
                        {
                            break;
                        }
                    }
                    this._zptr[j] = v;
                    i++;

                    /* copy 3 */
                    if (i > hi)
                    {
                        break;
                    }
                    v = this._zptr[i];
                    j = i;
                    while (FullGtU(this._zptr[j - h] + d, v + d))
                    {
                        this._zptr[j] = this._zptr[j - h];
                        j = j - h;
                        if (j <= (lo + h - 1))
                        {
                            break;
                        }
                    }
                    this._zptr[j] = v;
                    i++;

                    if (this._workDone > this._workLimit && this._firstAttempt)
                    {
                        return;
                    }
                }
            }
        }

        private bool FullGtU(int i1, int i2)
        {
            int k;
            char c1, c2;
            int s1, s2;

            c1 = this._block[i1 + 1];
            c2 = this._block[i2 + 1];
            if (c1 != c2)
            {
                return (c1 > c2);
            }
            i1++;
            i2++;

            c1 = this._block[i1 + 1];
            c2 = this._block[i2 + 1];
            if (c1 != c2)
            {
                return (c1 > c2);
            }
            i1++;
            i2++;

            c1 = this._block[i1 + 1];
            c2 = this._block[i2 + 1];
            if (c1 != c2)
            {
                return (c1 > c2);
            }
            i1++;
            i2++;

            c1 = this._block[i1 + 1];
            c2 = this._block[i2 + 1];
            if (c1 != c2)
            {
                return (c1 > c2);
            }
            i1++;
            i2++;

            c1 = this._block[i1 + 1];
            c2 = this._block[i2 + 1];
            if (c1 != c2)
            {
                return (c1 > c2);
            }
            i1++;
            i2++;

            c1 = this._block[i1 + 1];
            c2 = this._block[i2 + 1];
            if (c1 != c2)
            {
                return (c1 > c2);
            }
            i1++;
            i2++;

            k = this._runLastIndex + 1;

            do
            {
                c1 = this._block[i1 + 1];
                c2 = this._block[i2 + 1];
                if (c1 != c2)
                {
                    return (c1 > c2);
                }
                s1 = this._quadrant[i1];
                s2 = this._quadrant[i2];
                if (s1 != s2)
                {
                    return (s1 > s2);
                }
                i1++;
                i2++;

                c1 = this._block[i1 + 1];
                c2 = this._block[i2 + 1];
                if (c1 != c2)
                {
                    return (c1 > c2);
                }
                s1 = this._quadrant[i1];
                s2 = this._quadrant[i2];
                if (s1 != s2)
                {
                    return (s1 > s2);
                }
                i1++;
                i2++;

                c1 = this._block[i1 + 1];
                c2 = this._block[i2 + 1];
                if (c1 != c2)
                {
                    return (c1 > c2);
                }
                s1 = this._quadrant[i1];
                s2 = this._quadrant[i2];
                if (s1 != s2)
                {
                    return (s1 > s2);
                }
                i1++;
                i2++;

                c1 = this._block[i1 + 1];
                c2 = this._block[i2 + 1];
                if (c1 != c2)
                {
                    return (c1 > c2);
                }
                s1 = this._quadrant[i1];
                s2 = this._quadrant[i2];
                if (s1 != s2)
                {
                    return (s1 > s2);
                }
                i1++;
                i2++;

                if (i1 > this._runLastIndex)
                {
                    i1 -= this._runLastIndex;
                    i1--;
                }
                ;
                if (i2 > this._runLastIndex)
                {
                    i2 -= this._runLastIndex;
                    i2--;
                }
                ;

                k -= 4;
                this._workDone++;
            }
            while (k >= 0);

            return false;
        }

        #endregion

        #region  QSort3
        internal class StackElem
        {
            internal int ll;
            internal int hh;
            internal int dd;
        }

        private void QSort3(int loSt, int hiSt, int dSt)
        {
            int unLo, unHi, ltLo, gtHi, med, n, m;
            int sp, lo, hi, d;
            StackElem[] stack = new StackElem[QSORT_STACK_SIZE];
            for (int count = 0; count < QSORT_STACK_SIZE; count++)
            {
                stack[count] = new StackElem();
            }

            sp = 0;

            stack[sp].ll = loSt;
            stack[sp].hh = hiSt;
            stack[sp].dd = dSt;
            sp++;

            while (sp > 0)
            {
                if (sp >= QSORT_STACK_SIZE)
                {
                    throw new Exception("BZip2BlockCompressor internal error during QSort3, QSORT_STACK_SIZE exceeded");
                }

                sp--;
                lo = stack[sp].ll;
                hi = stack[sp].hh;
                d = stack[sp].dd;

                if (hi - lo < SMALL_THRESH || d > DEPTH_THRESH)
                {
                    SimpleSort(lo, hi, d);
                    if (this._workDone > this._workLimit && this._firstAttempt)
                    {
                        return;
                    }
                    continue;
                }

                med = Med3(this._block[this._zptr[lo] + d + 1],
                           this._block[this._zptr[hi] + d + 1],
                           this._block[this._zptr[(lo + hi) >> 1] + d + 1]);

                unLo = ltLo = lo;
                unHi = gtHi = hi;

                while (true)
                {
                    while (true)
                    {
                        if (unLo > unHi)
                        {
                            break;
                        }
                        n = this._block[this._zptr[unLo] + d + 1] - med;
                        if (n == 0)
                        {
                            int temp = 0;
                            temp = this._zptr[unLo];
                            this._zptr[unLo] = this._zptr[ltLo];
                            this._zptr[ltLo] = temp;
                            ltLo++;
                            unLo++;
                            continue;
                        }
                        ;
                        if (n > 0)
                        {
                            break;
                        }
                        unLo++;
                    }
                    while (true)
                    {
                        if (unLo > unHi)
                        {
                            break;
                        }
                        n = this._block[this._zptr[unHi] + d + 1] - med;
                        if (n == 0)
                        {
                            int temp = 0;
                            temp = this._zptr[unHi];
                            this._zptr[unHi] = this._zptr[gtHi];
                            this._zptr[gtHi] = temp;
                            gtHi--;
                            unHi--;
                            continue;
                        }
                        ;
                        if (n < 0)
                        {
                            break;
                        }
                        unHi--;
                    }
                    if (unLo > unHi)
                    {
                        break;
                    }
                    int tempx = this._zptr[unLo];
                    this._zptr[unLo] = this._zptr[unHi];
                    this._zptr[unHi] = tempx;
                    unLo++;
                    unHi--;
                }

                if (gtHi < ltLo)
                {
                    stack[sp].ll = lo;
                    stack[sp].hh = hi;
                    stack[sp].dd = d + 1;
                    sp++;
                    continue;
                }

                n = ((ltLo - lo) < (unLo - ltLo)) ? (ltLo - lo) : (unLo - ltLo);
                Vswap(lo, unLo - n, n);
                m = ((hi - gtHi) < (gtHi - unHi)) ? (hi - gtHi) : (gtHi - unHi);
                Vswap(unLo, hi - m + 1, m);

                n = lo + unLo - ltLo - 1;
                m = hi - (gtHi - unHi) + 1;

                stack[sp].ll = lo;
                stack[sp].hh = n;
                stack[sp].dd = d;
                sp++;

                stack[sp].ll = n + 1;
                stack[sp].hh = m - 1;
                stack[sp].dd = d + 1;
                sp++;

                stack[sp].ll = m;
                stack[sp].hh = hi;
                stack[sp].dd = d;
                sp++;
            }
        }

        private void Vswap(int p1, int p2, int n)
        {
            int temp = 0;
            while (n > 0)
            {
                temp = this._zptr[p1];
                this._zptr[p1] = this._zptr[p2];
                this._zptr[p2] = temp;
                p1++;
                p2++;
                n--;
            }
        }

        private char Med3(char a, char b, char c)
        {
            char t;
            if (a > b)
            {
                t = a;
                a = b;
                b = t;
            }
            if (b > c)
            {
                t = b;
                b = c;
                c = t;
            }
            if (a > b)
            {
                b = a;
            }
            return b;
        }

        #endregion
    }
}
