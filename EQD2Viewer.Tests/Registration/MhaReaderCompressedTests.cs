using EQD2Viewer.Registration.Services;
using FluentAssertions;
using System.IO;
using System.Text;

namespace EQD2Viewer.Tests.Registration
{
    /// <summary>
    /// Regression: MhaReader must not silently accept CompressedData=true headers.
    /// The DEFLATE-compressed body cannot be decoded by this reader, and processing the
    /// raw compressed bytes as if they were little-endian floats would produce garbage
    /// that looks like a plausible but wrong DVF.
    /// </summary>
    public class MhaReaderCompressedTests
    {
        private readonly MhaReader _reader = new MhaReader();

        private static string WriteTempMha(string header, byte[] payload)
        {
            string path = Path.GetTempFileName() + ".mha";
            var hb = Encoding.ASCII.GetBytes(header);
            var all = new byte[hb.Length + payload.Length];
            hb.CopyTo(all, 0);
            payload.CopyTo(all, hb.Length);
            File.WriteAllBytes(path, all);
            return path;
        }

        [Fact]
        public void ReadDeformationField_CompressedDataTrue_ReturnsNull()
        {
            // Valid-looking header except CompressedData=True.
            string header =
                "ObjectType = Image\r\n" +
                "NDims = 3\r\n" +
                "DimSize = 1 1 1\r\n" +
                "ElementType = MET_FLOAT\r\n" +
                "ElementNumberOfChannels = 3\r\n" +
                "ElementSpacing = 1 1 1\r\n" +
                "CompressedData = True\r\n" +
                "ElementDataFile = LOCAL\r\n";
            // Payload can be anything — reader should reject at header stage.
            string path = WriteTempMha(header, new byte[12]);
            try
            {
                _reader.ReadDeformationField(path).Should().BeNull(
                    "compressed MHA files are not supported; reader must refuse rather than " +
                    "silently emit nonsense vectors");
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ReadVolume_CompressedDataTrue_ReturnsNull()
        {
            string header =
                "ObjectType = Image\r\n" +
                "NDims = 3\r\n" +
                "DimSize = 1 1 1\r\n" +
                "ElementType = MET_SHORT\r\n" +
                "ElementSpacing = 1 1 1\r\n" +
                "CompressedData = True\r\n" +
                "ElementDataFile = LOCAL\r\n";
            string path = WriteTempMha(header, new byte[2]);
            try
            {
                _reader.ReadVolume(path).Should().BeNull();
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ReadDeformationField_CompressedDataFalse_ParsesNormally()
        {
            // Just confirming our reject check doesn't false-trigger on the explicit "False" value.
            string header =
                "ObjectType = Image\r\n" +
                "NDims = 3\r\n" +
                "DimSize = 1 1 1\r\n" +
                "ElementType = MET_FLOAT\r\n" +
                "ElementNumberOfChannels = 3\r\n" +
                "ElementSpacing = 1 1 1\r\n" +
                "CompressedData = False\r\n" +
                "ElementDataFile = LOCAL\r\n";
            var payload = new byte[12];
            System.BitConverter.GetBytes(1.0f).CopyTo(payload, 0);
            System.BitConverter.GetBytes(2.0f).CopyTo(payload, 4);
            System.BitConverter.GetBytes(3.0f).CopyTo(payload, 8);
            string path = WriteTempMha(header, payload);
            try
            {
                var dvf = _reader.ReadDeformationField(path);
                dvf.Should().NotBeNull();
                dvf!.Vectors[0][0, 0].X.Should().BeApproximately(1.0, 1e-6);
            }
            finally { File.Delete(path); }
        }
    }
}
