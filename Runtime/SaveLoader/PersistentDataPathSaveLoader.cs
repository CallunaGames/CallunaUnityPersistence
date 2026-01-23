using System;
using System.Collections.Generic;
using System.IO;
using Calluna.DI;
using UnityEngine;

namespace Calluna.Persistence
{
    public class PersistentDataPathSaveLoader : SaveLoader, Injectable, Cleanable
    {
        public event Action OnClear;
        private JsonSerializer _serializer;
        private TextFileReadWriter _textFileReadWriter;
        private string _fileName;
        private string _path;

        private Dictionary<string, string> _persistedData;
        private Dictionary<string, string> persistedData => _persistedData ??= ReadOrCreateData();

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

        public void Clean()
        {
            ClearStreams();
        }

        public bool Has(string id)
        {
            return persistedData.ContainsKey(id);
        }

        public T Load<T>(string id, T defaultValue = default(T))
        {
            if (!Has(id))
                return defaultValue;
            return _serializer.Deserialize<T>(persistedData[id]);
        }

        public void Save<T>(string id, T value)
        {
            persistedData[id] = _serializer.Serialize(value);
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
            OnClear?.Invoke();
        }

        private Dictionary<string, string> ReadOrCreateData()
        {
            if (_textFileReadWriter.Has(_path))
            {
                string text = _textFileReadWriter.ReadText(GetOrOpenStreams());
                return _serializer.Deserialize<Dictionary<string, string>>(text) ??
                       new Dictionary<string, string>();
            }

            _textFileReadWriter.Create(_path);
            return new Dictionary<string, string>();
        }

        private void SaveData()
        {
            _textFileReadWriter.Overwrite(GetOrOpenStreams(), _serializer.Serialize(persistedData));
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

        public class Arguments
        {
            public string FileName;
        }
    }
}