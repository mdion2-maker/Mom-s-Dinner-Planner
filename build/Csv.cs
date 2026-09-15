using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Dish
{
    // Quote-aware streaming CSV reader (RFC4180-ish, handles embedded newlines + doubled quotes).
    public class CsvReader : IDisposable
    {
        private readonly TextReader _r;
        private readonly char[] _buf = new char[1 << 16];
        private int _len, _pos;

        public CsvReader(TextReader r) { _r = r; }

        private int Peek()
        {
            if (_pos >= _len)
            {
                _len = _r.Read(_buf, 0, _buf.Length);
                _pos = 0;
                if (_len <= 0) return -1;
            }
            return _buf[_pos];
        }

        private int Next()
        {
            int c = Peek();
            if (c >= 0) _pos++;
            return c;
        }

        public string[] ReadRow()
        {
            if (Peek() < 0) return null;
            List<string> fields = new List<string>(40);
            StringBuilder sb = new StringBuilder(256);
            bool inQuotes = false;
            while (true)
            {
                int ci = Next();
                if (ci < 0)
                {
                    fields.Add(sb.ToString());
                    return fields.Count == 1 && fields[0].Length == 0 ? null : fields.ToArray();
                }
                char c = (char)ci;
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (Peek() == '"') { _pos++; sb.Append('"'); }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { fields.Add(sb.ToString()); sb.Length = 0; }
                    else if (c == '\r') { /* skip */ }
                    else if (c == '\n')
                    {
                        fields.Add(sb.ToString());
                        return fields.ToArray();
                    }
                    else sb.Append(c);
                }
            }
        }

        public void Dispose() { _r.Dispose(); }

        public static CsvReader FromZip(string zipPath, string entryName, out ZipArchive archive)
        {
            archive = ZipFile.OpenRead(zipPath);
            ZipArchiveEntry e = archive.GetEntry(entryName);
            Stream s = e.Open();
            return new CsvReader(new StreamReader(s, new UTF8Encoding(false), true, 1 << 20));
        }
    }
}
