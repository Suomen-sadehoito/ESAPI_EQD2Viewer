using EQD2Viewer.Core.Serialization;
using FluentAssertions;
using System.IO;
using System.Linq;
using System.Text;

namespace EQD2Viewer.Tests.Serialization
{
    /// <summary>
    /// Tests for the small BOM-stripping helper that every JSON read path
    /// shares. Two scenarios matter: a UTF-8 file written with a BOM (the
    /// .NET default) and one written without (some hand-rolled exporters).
    /// </summary>
    public class JsonFileHelperTests
    {
        [Fact]
        public void ReadStripBom_FileWithUtf8Bom_StripsBom()
        {
            string path = Path.GetTempFileName();
            try
            {
                // Encoding.UTF8 emits the BOM on write; verify that bytes start with EF BB BF.
                File.WriteAllText(path, "{\"x\":1}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                File.ReadAllBytes(path).Take(3).Should().Equal(new byte[] { 0xEF, 0xBB, 0xBF });

                string text = JsonFileHelper.ReadStripBom(path);

                text.Should().Be("{\"x\":1}");
                text[0].Should().Be('{', "BOM character must be removed before downstream JSON deserialisation");
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ReadStripBom_FileWithoutBom_PassesThroughUnchanged()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "{\"x\":2}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                string text = JsonFileHelper.ReadStripBom(path);

                text.Should().Be("{\"x\":2}");
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ReadStripBom_EmptyFile_ReturnsEmptyString()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "");
                JsonFileHelper.ReadStripBom(path).Should().BeEmpty();
            }
            finally { File.Delete(path); }
        }
    }
}
