using EQD2Viewer.Core.Data;
using EQD2Viewer.Core.Interfaces;
using EQD2Viewer.Core.Logging;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace EQD2Viewer.Registration.Services
{
    /// <summary>
    /// Reads MetaImage (.mha / .mhd) files into VolumeData or DeformationField structures.
    /// Implements IDeformationFieldLoader so SummationService can load pre-computed DVF files.
    ///
    /// For LOCAL data MHA files, the header is scanned byte-by-byte (ASCII) to find
    /// the exact byte offset at which the binary payload begins.
    /// </summary>
    public class MhaReader : IDeformationFieldLoader
    {
        // ── IDeformationFieldLoader ──────────────────────────────────────

        public DeformationField? Load(string path)
        {
            try { return ReadDeformationField(path); }
            catch (Exception ex)
            {
                SimpleLogger.Error($"MhaReader.Load failed for '{path}'", ex);
                return null;
            }
        }

        // ── Public API ───────────────────────────────────────────────────

        public VolumeData? ReadVolume(string path)
        {
            if (!File.Exists(path)) { SimpleLogger.Warning($"MhaReader: not found: {path}"); return null; }
            var (header, dataOffset) = ParseHeader(path);
            if (header == null) return null;
            try
            {
                using var data = OpenDataStream(path, header, dataOffset);
                return data != null ? BuildVolume(header, data) : null;
            }
            catch (Exception ex) { SimpleLogger.Error($"MhaReader.ReadVolume: {ex.Message}", ex); return null; }
        }

        public DeformationField? ReadDeformationField(string path)
        {
            if (!File.Exists(path)) { SimpleLogger.Warning($"MhaReader: not found: {path}"); return null; }
            var (header, dataOffset) = ParseHeader(path);
            if (header == null) return null;
            try
            {
                using var data = OpenDataStream(path, header, dataOffset);
                return data != null ? BuildDeformationField(header, data) : null;
            }
            catch (Exception ex) { SimpleLogger.Error($"MhaReader.ReadDeformationField: {ex.Message}", ex); return null; }
        }

        // ── Header ───────────────────────────────────────────────────────

        private sealed class MhaHeader
        {
            public int NDims;
            public int[] DimSize = null!;
            public double[] ElementSpacing = null!;
            public double[] Offset = null!;
            public double[] TransformMatrix = null!;
            public string ElementType = "";
            public int ElementNumberOfChannels = 1;
            public string ElementDataFile = "LOCAL";
            public bool CompressedData;
        }

        /// <summary>
        /// Reads the ASCII header from the MHA file and returns the header + the byte
        /// offset at which binary data starts (valid for LOCAL files only; 0 for MHD).
        /// Reads the file in 8 KB chunks — never loads the entire binary body.
        /// </summary>
        private static (MhaHeader? header, long dataOffset) ParseHeader(string path)
        {
            const int BUF = 8192;
            var sb = new StringBuilder();
            long dataOffset = 0;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] chunk = new byte[BUF];
            bool done = false;

            while (!done)
            {
                int n = fs.Read(chunk, 0, BUF);
                if (n == 0) break;
                string part = Encoding.ASCII.GetString(chunk, 0, n);
                sb.Append(part);

                // Check if we've now seen the ElementDataFile line
                string accumulated = sb.ToString();
                int efIdx = accumulated.IndexOf("ElementDataFile", StringComparison.OrdinalIgnoreCase);
                if (efIdx >= 0)
                {
                    int nlIdx = accumulated.IndexOf('\n', efIdx);
                    if (nlIdx >= 0)
                    {
                        // dataOffset = byte position right after the \n
                        // Since the header is ASCII, char count == byte count
                        dataOffset = nlIdx + 1;
                        done = true;
                    }
                }
            }

            if (!done) { SimpleLogger.Warning("MhaReader: ElementDataFile line not found"); return (null, 0); }

            var header = ParseHeaderLines(sb.ToString());
            return (header, dataOffset);
        }

        private static MhaHeader? ParseHeaderLines(string text)
        {
            var h = new MhaHeader();
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim('\r', ' ');
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim().ToUpperInvariant();
                string val = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "NDIMS":
                        int.TryParse(val, out h.NDims); break;
                    case "DIMSIZE":
                        h.DimSize = ParseInts(val); break;
                    case "ELEMENTSPACING":
                        h.ElementSpacing = ParseDoubles(val); break;
                    case "OFFSET":
                    case "POSITION":
                        h.Offset = ParseDoubles(val); break;
                    case "TRANSFORMMATRIX":
                    case "ROTATION":
                        h.TransformMatrix = ParseDoubles(val); break;
                    case "ELEMENTTYPE":
                        h.ElementType = val.ToUpperInvariant(); break;
                    case "ELEMENTNUMBEROFCHANNELS":
                        int.TryParse(val, out h.ElementNumberOfChannels); break;
                    case "COMPRESSEDDATA":
                        h.CompressedData = val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                    case "ELEMENTDATAFILE":
                        h.ElementDataFile = val; break;
                }
            }

            if (h.DimSize == null || h.ElementType.Length == 0)
            {
                SimpleLogger.Warning("MhaReader: incomplete header (missing DimSize or ElementType)");
                return null;
            }

            if (h.NDims == 0) h.NDims = h.DimSize.Length;
            int nd = h.NDims;

            if (h.Offset == null) h.Offset = new double[nd];
            if (h.ElementSpacing == null) { h.ElementSpacing = new double[nd]; for (int i = 0; i < nd; i++) h.ElementSpacing[i] = 1.0; }
            if (h.TransformMatrix == null)
            {
                h.TransformMatrix = new double[nd * nd];
                for (int i = 0; i < nd; i++) h.TransformMatrix[i * nd + i] = 1.0;
            }

            return h;
        }

        // ── Data stream ──────────────────────────────────────────────────

        private static Stream? OpenDataStream(string mhaPath, MhaHeader header, long dataOffset)
        {
            if (string.Equals(header.ElementDataFile, "LOCAL", StringComparison.OrdinalIgnoreCase))
            {
                var fs = new FileStream(mhaPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Seek(dataOffset, SeekOrigin.Begin);
                return fs;
            }

            // MHD: external .raw file
            string rawPath = Path.Combine(Path.GetDirectoryName(mhaPath) ?? "", header.ElementDataFile);
            if (!File.Exists(rawPath)) { SimpleLogger.Warning($"MhaReader: raw file not found: {rawPath}"); return null; }
            return new FileStream(rawPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        // ── Volume ───────────────────────────────────────────────────────

        private static VolumeData? BuildVolume(MhaHeader h, Stream data)
        {
            if (h.NDims != 3 || h.DimSize.Length < 3) return null;
            if (h.ElementNumberOfChannels != 1) return null;

            int xSize = h.DimSize[0], ySize = h.DimSize[1], zSize = h.DimSize[2];
            int bpe = ElementBytes(h.ElementType);
            if (bpe <= 0) { SimpleLogger.Warning($"MhaReader: unsupported type {h.ElementType}"); return null; }

            var voxels = new int[zSize][,];
            byte[] buf = new byte[xSize * ySize * bpe];
            for (int z = 0; z < zSize; z++)
            {
                ReadExact(data, buf);
                voxels[z] = new int[xSize, ySize];
                for (int y = 0; y < ySize; y++)
                    for (int x = 0; x < xSize; x++)
                        voxels[z][x, y] = ToInt(buf, (y * xSize + x) * bpe, h.ElementType);
            }

            return new VolumeData { Geometry = BuildGeometry(h), Voxels = voxels, HuOffset = 0 };
        }

        // ── DVF ──────────────────────────────────────────────────────────

        private static DeformationField? BuildDeformationField(MhaHeader h, Stream data)
        {
            if (h.NDims != 3 || h.DimSize.Length < 3) return null;
            if (h.ElementNumberOfChannels != 3)
            {
                SimpleLogger.Warning($"MhaReader: DVF requires 3 channels, got {h.ElementNumberOfChannels}");
                return null;
            }

            int xSize = h.DimSize[0], ySize = h.DimSize[1], zSize = h.DimSize[2];
            byte[] buf = new byte[xSize * ySize * 12]; // 3 × float32 per voxel

            var vectors = new Vec3[zSize][,];
            for (int z = 0; z < zSize; z++)
            {
                ReadExact(data, buf);
                vectors[z] = new Vec3[xSize, ySize];
                for (int y = 0; y < ySize; y++)
                    for (int x = 0; x < xSize; x++)
                    {
                        int i = (y * xSize + x) * 12;
                        vectors[z][x, y] = new Vec3(
                            BitConverter.ToSingle(buf, i),
                            BitConverter.ToSingle(buf, i + 4),
                            BitConverter.ToSingle(buf, i + 8));
                    }
            }

            var geo = BuildGeometry(h);
            return new DeformationField
            {
                XSize = xSize, YSize = ySize, ZSize = zSize,
                XRes = geo.XRes, YRes = geo.YRes, ZRes = geo.ZRes,
                Origin = geo.Origin,
                XDirection = geo.XDirection,
                YDirection = geo.YDirection,
                ZDirection = geo.ZDirection,
                Vectors = vectors
            };
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static VolumeGeometry BuildGeometry(MhaHeader h)
        {
            double[] sp = h.ElementSpacing;
            double[] off = h.Offset;
            double[] tm = h.TransformMatrix;
            return new VolumeGeometry
            {
                XSize = h.DimSize[0], YSize = h.DimSize[1], ZSize = h.DimSize[2],
                XRes = sp.Length > 0 ? sp[0] : 1.0,
                YRes = sp.Length > 1 ? sp[1] : 1.0,
                ZRes = sp.Length > 2 ? sp[2] : 1.0,
                Origin = new Vec3(
                    off.Length > 0 ? off[0] : 0,
                    off.Length > 1 ? off[1] : 0,
                    off.Length > 2 ? off[2] : 0),
                XDirection = tm.Length >= 3  ? new Vec3(tm[0], tm[1], tm[2]) : new Vec3(1, 0, 0),
                YDirection = tm.Length >= 6  ? new Vec3(tm[3], tm[4], tm[5]) : new Vec3(0, 1, 0),
                ZDirection = tm.Length >= 9  ? new Vec3(tm[6], tm[7], tm[8]) : new Vec3(0, 0, 1),
            };
        }

        private static int ElementBytes(string type) => type switch
        {
            "MET_UCHAR"  => 1, "MET_CHAR"   => 1,
            "MET_USHORT" => 2, "MET_SHORT"  => 2,
            "MET_UINT"   => 4, "MET_INT"    => 4,
            "MET_FLOAT"  => 4, "MET_DOUBLE" => 8,
            _ => 0
        };

        private static int ToInt(byte[] buf, int off, string type) => type switch
        {
            "MET_UCHAR"  => buf[off],
            "MET_CHAR"   => (sbyte)buf[off],
            "MET_USHORT" => BitConverter.ToUInt16(buf, off),
            "MET_SHORT"  => BitConverter.ToInt16(buf, off),
            "MET_UINT"   => (int)BitConverter.ToUInt32(buf, off),
            "MET_INT"    => BitConverter.ToInt32(buf, off),
            "MET_FLOAT"  => (int)BitConverter.ToSingle(buf, off),
            "MET_DOUBLE" => (int)BitConverter.ToDouble(buf, off),
            _ => 0
        };

        private static void ReadExact(Stream s, byte[] buf)
        {
            int total = buf.Length, read = 0;
            while (read < total)
            {
                int n = s.Read(buf, read, total - read);
                if (n == 0) throw new EndOfStreamException("Unexpected end of MHA binary data.");
                read += n;
            }
        }

        private static int[] ParseInts(string s)
        {
            var parts = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var r = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) r[i] = int.Parse(parts[i], CultureInfo.InvariantCulture);
            return r;
        }

        private static double[] ParseDoubles(string s)
        {
            var parts = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var r = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++) r[i] = double.Parse(parts[i], CultureInfo.InvariantCulture);
            return r;
        }
    }
}
