using System.IO;
using System.Text;

namespace EQD2Viewer.Core.Serialization
{
    /// <summary>
    /// Small utilities shared by every JSON read path that consumes files
    /// written by the standard .NET <c>File.WriteAllText</c> helpers (which
    /// emit a UTF-8 BOM by default) or by hand-rolled exporters that do or
    /// do not. <c>System.Text.Json</c> rejects a leading BOM, so every
    /// caller must strip it before deserialising.
    /// </summary>
    public static class JsonFileHelper
    {
        /// <summary>
        /// Reads the file at <paramref name="path"/> as UTF-8 text and strips
        /// a leading UTF-8 BOM (U+FEFF) if present. Returns the resulting
        /// string ready for <c>JsonSerializer.Deserialize</c>.
        /// </summary>
        public static string ReadStripBom(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            // Strip UTF-8 BOM if present (EF BB BF → char 0xFEFF)
            if (text.Length > 0 && text[0] == '﻿')
                text = text.Substring(1);
            return text;
        }
    }
}
