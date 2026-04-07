using System.IO;

namespace Calluna.Persistence
{
    internal class TextFileReadWriter
    {
        internal string ReadText((FileStream, StreamWriter, StreamReader) streams)
        {
            streams.Item1.Position = 0;
            return streams.Item3.ReadToEnd();
        }

        internal bool Has(string path) => File.Exists(path);

        internal (FileStream, StreamWriter, StreamReader) OpenStreams(string path)
        {
            FileStream fileStream = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read);
            StreamWriter streamWriter = new StreamWriter(fileStream);
            StreamReader streamReader = new StreamReader(fileStream);
            return (fileStream, streamWriter, streamReader);
        }

        internal void Overwrite((FileStream, StreamWriter, StreamReader) streams, string content)
        {
            streams.Item1.SetLength(0);
            streams.Item1.Position = 0;
            streams.Item2.Write(content);
            streams.Item2.Flush();
        }

        internal void Close((FileStream, StreamWriter, StreamReader) streams)
        {
            streams.Item2.Dispose();
            streams.Item3.Dispose();
            streams.Item1.Dispose();
        }

        internal void Create(string path) => File.CreateText(path).Close();

        internal void Delete(string path) => File.Delete(path);
    }
}
