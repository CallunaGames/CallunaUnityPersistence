using System;

namespace Calluna.Persistence
{
    [Serializable]
    internal class GameData
    {
        public int Version;
        public GameDataEntry[] Data;
    }
}