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
        
        public bool Has(string path) => File.Exists(path);

        public void WriteText(string path, string contents, bool append = false)
        {
            StreamWriter stream = Has(path) ? new StreamWriter(path, append) : File.CreateText(path);
            stream.Write(contents);
            stream.Close();
        }
        
        public void Create(string path) => File.CreateText(path).Close();

        public void Delete(string path) => File.Delete(path);
    }
}
