using System;
using System.Collections.Generic;
using System.IO;
using Calluna.DI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Calluna.Persistence
{
    /// <summary>
    /// A <see cref="SaveLoader"/> that keeps all entries in one JSON file in
    /// <c>Application.persistentDataPath</c>. Every change rewrites the file - within a batch
    /// (see <see cref="GameDataPersistence"/>) only once, when the batch is committed.
    /// <para>
    /// The file is read once, on first access, and written atomically: to <c>&lt;file&gt;.tmp</c> first,
    /// which then replaces the file. A crash while writing leaves the previous file. No file is kept open,
    /// so cloud sync or a player's editor can read and replace it while the game runs - but the loader
    /// doesn't notice outside changes and overwrites them with its next write.
    /// </para>
    /// <para>
    /// A file that exists but is empty or isn't a JSON object can't be read: <see cref="Has"/> and
    /// <see cref="Load{T}"/> throw (<see cref="GameDataPersistence"/> reports it as a
    /// <see cref="GameDataLoadFailure.Storage"/> failure) instead of silently starting without data.
    /// </para>
    /// <para>Thread-safe: <see cref="GameDataPersistence.SaveAsync"/> writes on a background thread.</para>
    /// </summary>
    public class PersistentDataPathSaveLoader : SaveLoader, IBatchableSaveLoader, IArchivableSaveLoader, Injectable, Cleanable
    {
        public event Action OnClear;
        private JsonSerializer _serializer;
        private TextFileReadWriter _textFileReadWriter;
        private string _path;
        private bool _indented;
        private bool _inBatch;
        private bool _hasUnwrittenChanges;

        private readonly object _lock = new object();

        private Dictionary<string, JToken> _persistedData;
        private Dictionary<string, JToken> persistedData => _persistedData ??= ReadData();

        public void Inject(Resolver resolver)
        {
            _serializer = resolver.Resolve<JsonSerializer>();
            _textFileReadWriter = resolver.Resolve<TextFileReadWriter>();
            Arguments arguments = resolver.Resolve<Arguments>();
            _path = Path.Combine(Application.persistentDataPath, arguments.FileName);
            _indented = arguments.Indented;
        }

        /// <summary>
        /// Nothing to release: no file is kept open. Operations after <see cref="Clean"/> - e.g. a final
        /// save that the DI cleanup runs after this loader's cleanup - still work.
        /// </summary>
        public void Clean()
        {
        }

        public bool Has(string id)
        {
            lock (_lock)
            {
                // Don't read (or create) the file just to answer that it has no data.
                if (_persistedData == null && !_textFileReadWriter.Has(_path))
                    return false;
                return persistedData.ContainsKey(id);
            }
        }

        public T Load<T>(string id, T defaultValue = default(T))
        {
            lock (_lock)
            {
                if (!persistedData.TryGetValue(id, out JToken token))
                    return defaultValue;
                return _serializer.Deserialize<T>(token);
            }
        }

        public void Save<T>(string id, T value)
        {
            lock (_lock)
            {
                persistedData[id] = _serializer.SerializeToToken(value);
                SaveData();
            }
        }

        public void Delete(string id)
        {
            lock (_lock)
            {
                persistedData.Remove(id);
                SaveData();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                DeleteIfExists(_path);
                DeleteIfExists(_path + TextFileReadWriter.TempSuffix);
                _persistedData = null;
                _hasUnwrittenChanges = false;
            }
            OnClear?.Invoke();
        }

        void IArchivableSaveLoader.Archive(string suffix)
        {
            lock (_lock)
            {
                if (_textFileReadWriter.Has(_path))
                    _textFileReadWriter.Move(_path, _path + suffix);
                _persistedData = null;
                _hasUnwrittenChanges = false;
            }
        }

        void IBatchableSaveLoader.BeginBatch()
        {
            lock (_lock)
                _inBatch = true;
        }

        void IBatchableSaveLoader.CommitBatch()
        {
            lock (_lock)
            {
                _inBatch = false;
                if (_hasUnwrittenChanges)
                    SaveData();
            }
        }

        void IBatchableSaveLoader.RollbackBatch()
        {
            lock (_lock)
            {
                _inBatch = false;
                if (!_hasUnwrittenChanges)
                    return;
                // Drop the batch's changes - the file still holds the state before the batch.
                _hasUnwrittenChanges = false;
                _persistedData = null;
            }
        }

        // A missing file is no data. An existing one must be a JSON object: an empty file, "null" or
        // garbage throw - silently starting over would overwrite the data with defaults on the next save.
        private Dictionary<string, JToken> ReadData()
        {
            if (!_textFileReadWriter.Has(_path))
                return new Dictionary<string, JToken>();

            string text = _textFileReadWriter.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidDataException($"'{_path}' is empty.");
            return _serializer.Deserialize<Dictionary<string, JToken>>(text)
                   ?? throw new InvalidDataException($"'{_path}' doesn't contain a JSON object.");
        }

        private void SaveData()
        {
            if (_inBatch)
            {
                _hasUnwrittenChanges = true;
                return;
            }

            _hasUnwrittenChanges = false;
            string content = _indented
                ? _serializer.Serialize(persistedData, Formatting.Indented)
                : _serializer.Serialize(persistedData);
            _textFileReadWriter.WriteAtomic(_path, content);
        }

        private void DeleteIfExists(string path)
        {
            if (_textFileReadWriter.Has(path))
                _textFileReadWriter.Delete(path);
        }

        internal class Arguments
        {
            public string FileName;

            /// <summary>Writes indented, human-readable JSON instead of a single line.</summary>
            public bool Indented;
        }
    }
}
