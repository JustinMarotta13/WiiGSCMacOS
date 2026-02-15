// Copyright 2010 Nejat Dilek  <imruon@gmail.com>
// Licensed under the terms of the GNU GPL, version 2
// http://www.gnu.org/licenses/old-licenses/gpl-2.0.txt
// Ported to .NET 8 with cross-platform compression

using System;
using System.IO;
using System.IO.Compression;

namespace Org.Irduco.CommonHelpers
{
    public static class ZlibHelper
    {
        /// <summary>
        /// Compresses data using Zlib/Deflate compression (cross-platform implementation)
        /// </summary>
        /// <param name="inFile">Input data to compress</param>
        /// <returns>Compressed data with Zlib header</returns>
        public static byte[] Compress(byte[] inFile)
        {
            try
            {
                using (var outputStream = new MemoryStream())
                {
                    // Write Zlib header (RFC 1950)
                    // CMF byte: 0x78 (deflate with 32K window)
                    // FLG byte: 0x9C (default compression, no preset dict, checksum)
                    outputStream.WriteByte(0x78);
                    outputStream.WriteByte(0x9C);

                    // Compress the data using DeflateStream
                    using (var deflateStream = new DeflateStream(outputStream, CompressionLevel.Optimal, true))
                    {
                        deflateStream.Write(inFile, 0, inFile.Length);
                    }

                    // Write Adler-32 checksum (4 bytes, big-endian)
                    uint adler = ComputeAdler32(inFile);
                    outputStream.WriteByte((byte)(adler >> 24));
                    outputStream.WriteByte((byte)(adler >> 16));
                    outputStream.WriteByte((byte)(adler >> 8));
                    outputStream.WriteByte((byte)adler);

                    return outputStream.ToArray();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("An error occurred while compressing: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Computes Adler-32 checksum for Zlib format
        /// </summary>
        private static uint ComputeAdler32(byte[] data)
        {
            const uint MOD_ADLER = 65521;
            uint a = 1, b = 0;

            foreach (byte byteValue in data)
            {
                a = (a + byteValue) % MOD_ADLER;
                b = (b + a) % MOD_ADLER;
            }

            return (b << 16) | a;
        }
    }
}
