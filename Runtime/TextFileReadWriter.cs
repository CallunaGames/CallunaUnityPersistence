using System.IO;
using UnityEngine;

namespace Calluna.Persistence
{
    public class TextFileReadWriter
    {
        public string ReadText(string path)
        {
            if(!File.Exists(path))
                throw new FileNotFoundException("The file could not be found.", path);
            return File.ReadAllText(path);
        }
        
        public string ReadText((FileStream, StreamWriter, StreamReader) streams)
        {
            streams.Item1.Position = 0;
            return streams.Item3.ReadToEnd();
        }
        
        public bool Has(string path) => File.Exists(path);

        public void WriteText(string path, string contents)
        {
            File.WriteAllText(path, contents);
        }

        public (FileStream, StreamWriter, StreamReader) OpenStreams(string path)
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

        public void Overwrite((FileStream, StreamWriter, StreamReader) streams, string content)
        {
            streams.Item1.SetLength(0);
            streams.Item1.Position = 0;
            streams.Item2.Write(content);
            streams.Item2.Flush(); 
        }

        public void Close((FileStream, StreamWriter, StreamReader) streams)
        {
            streams.Item1.Dispose();
            Debug.Log("Streams closed");
        }
        
        public void Create(string path) => File.CreateText(path).Close();

        public void Delete(string path) => File.Delete(path);
    }
}
