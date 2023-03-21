using System.IO;
using System.Text;

namespace Helper
{
    internal class XorWriter
    {
        // From: https://github.com/Thesola10/YC_English/blob/master/inucode.py
        // Inucode (c) Karim Vergnes <me@thesola.io>
        // it's just a XOR cipher with exact-match policy.
        // thx Joseph John
        public readonly byte[] key = Encoding.ASCII.GetBytes("hogehoge66");

        public void Write(Stream inStream, Stream outStream)
        {
            var reader = new BinaryReader(inStream);
            var writer = new BinaryWriter(outStream);
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                long pos = reader.BaseStream.Position;
                byte readerByte = reader.ReadByte();
                byte keyByte = key[pos % key.Length];
                if (readerByte != 0 && readerByte != keyByte) { readerByte ^= keyByte; }
                writer.Write(readerByte);
            }
            reader.Close();
            writer.Close();
        }

        public void Write(string inPath, string outPath)
        {
            Write(File.OpenRead(inPath), File.Create(outPath));
        }
    }
}