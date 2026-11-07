using System.IO;
using System.Text;

namespace Calluna.Persistence
{
    /// <summary>
    /// File access of <see cref="PersistentDataPathSaveLoader"/>: one-shot reads, and atomic writes - the
    /// content goes to a temporary file next to the target first, which then replaces the target. A crash
    /// in the middle of a write leaves the previous file (at worst plus a stale temporary file), never an
    /// empty or cut-off one. No file is kept open, so other programs (cloud sync, an editor) can read and
    /// replace it while the game runs.
    /// </summary>
    internal class TextFileReadWriter
    {
        internal const string TempSuffix = ".tmp";

        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);

        internal bool Has(string path) => File.Exists(path);

        internal string ReadAllText(string path) => File.ReadAllText(path);

        internal void WriteAtomic(string path, string content)
        {
            string tempPath = path + TempSuffix;
            byte[] bytes = Utf8WithoutBom.GetBytes(content);
            using (FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                // On disk before the swap - otherwise a power loss right after it can leave the new
                // file empty although the old one is already gone.
                stream.Flush(true);
            }

            if (File.Exists(path))
                File.Replace(tempPath, path, null, true);
            else
                File.Move(tempPath, path);
        }

        internal void Move(string sourcePath, string destinationPath) => File.Move(sourcePath, destinationPath);

        internal void Delete(string path) => File.Delete(path);
    }
}
