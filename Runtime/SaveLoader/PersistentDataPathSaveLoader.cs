using System;
using System.Collections.Generic;
using System.IO;
using Calluna.DI;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Calluna.Persistence
{
    /// <summary>
    /// A <see cref="SaveLoader"/> that keeps all entries in one JSON file in
    /// <c>Application.persistentDataPath</c>. Every change rewrites the file - within a batch
    /// (see <see cref="GameDataPersistence"/>) only once, when the batch is committed.
    /// <para>
    /// The file stays open until <see cref="Clean"/>. Operations after <see cref="Clean"/> - e.g. a
    /// final save that the DI cleanup runs after this loader's cleanup - still work, but read and
    /// write the file in one go without keeping it open.
    /// </para>
    /// </summary>
    public class PersistentDataPathSaveLoader : SaveLoader, IBatchableSaveLoader, IArchivableSaveLoader, Injectable, Cleanable
    {
        public event Action OnClear;
        private JsonSerializer _serializer;
        private TextFileReadWriter _textFileReadWriter;
        private string _fileName;
        private string _path;
        private bool _isCleaned;
        private bool _inBatch;
        private bool _hasUnwrittenChanges;

        private Dictionary<string, JToken> _persistedData;
        private Dictionary<string, JToken> persistedData => _persistedData ??= ReadOrCreateData();

        private bool hasStreams => _streams is { Item1: not null, Item2: not null, Item3: not null};

        private (FileStream, StreamWriter, StreamReader) _streams;

        public void Inject(Resolver resolver)
        {
            _serializer = resolver.Resolve<JsonSerializer>();
            _textFileReadWriter = resolver.Resolve<TextFileReadWriter>();
            Arguments arguments = resolver.Resolve<Arguments>();
            _fileName = arguments.FileName;
            _path = Path.Combine(Application.persistentDataPath, _fileName);
        }

        /// <summary>
        /// Closes open file streams. Called automatically by the Calluna.DI system during cleanup.
        /// </summary>
        public void Clean()
        {
            ClearStreams();
            _isCleaned = true;
        }

        public bool Has(string id)
        {
            // Don't create the file just to answer that it has no data.
            if (_persistedData == null && !_textFileReadWriter.Has(_path))
                return false;
            return persistedData.ContainsKey(id);
        }

        public T Load<T>(string id, T defaultValue = default(T))
        {
            if (!persistedData.TryGetValue(id, out JToken token))
                return defaultValue;
            return _serializer.Deserialize<T>(token);
        }

        public void Save<T>(string id, T value)
        {
            persistedData[id] = _serializer.SerializeToToken(value);
            SaveData();
        }

        public void Delete(string id)
        {
            persistedData.Remove(id);
            SaveData();
        }

        public void Clear()
        {
            ClearStreams();
            _textFileReadWriter.Delete(_path);
            _persistedData = null;
            _hasUnwrittenChanges = false;
            OnClear?.Invoke();
        }

        void IArchivableSaveLoader.Archive(string suffix)
        {
            ClearStreams();
            if (_textFileReadWriter.Has(_path))
                File.Move(_path, _path + suffix);
            _persistedData = null;
            _hasUnwrittenChanges = false;
        }

        void IBatchableSaveLoader.BeginBatch()
        {
            _inBatch = true;
        }

        void IBatchableSaveLoader.CommitBatch()
        {
            _inBatch = false;
            if (_hasUnwrittenChanges)
                SaveData();
        }

        void IBatchableSaveLoader.RollbackBatch()
        {
            _inBatch = false;
            if (!_hasUnwrittenChanges)
                return;
            // Drop the batch's changes - the file still holds the state before the batch.
            _hasUnwrittenChanges = false;
            _persistedData = null;
        }

        private Dictionary<string, JToken> ReadOrCreateData()
        {
            if (_textFileReadWriter.Has(_path))
            {
                string text = _isCleaned
                    ? _textFileReadWriter.ReadAllText(_path)
                    : _textFileReadWriter.ReadText(GetOrOpenStreams());
                return _serializer.Deserialize<Dictionary<string, JToken>>(text) ??
                       new Dictionary<string, JToken>();
            }

            _textFileReadWriter.Create(_path);
            return new Dictionary<string, JToken>();
        }

        private void SaveData()
        {
            if (_inBatch)
            {
                _hasUnwrittenChanges = true;
                return;
            }

            _hasUnwrittenChanges = false;
            string content = _serializer.Serialize(persistedData);
            if (_isCleaned)
                _textFileReadWriter.WriteAllText(_path, content);
            else
                _textFileReadWriter.Overwrite(GetOrOpenStreams(), content);
        }

        private (FileStream, StreamWriter, StreamReader) GetOrOpenStreams()
        {
            if (!hasStreams)
                _streams = _textFileReadWriter.OpenStreams(_path);
            return _streams;
        }

        private void ClearStreams()
        {
            if(!hasStreams)
                return;
            _textFileReadWriter.Close(_streams);
            _streams.Item1 = null;
            _streams.Item2 = null;
            _streams.Item3 = null;
        }

        internal class Arguments
        {
            public string FileName;
        }
    }
}
