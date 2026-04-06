# Calluna Unity Persistence

A Unity UPM package for game data persistence. Provides serialization, versioning, and migration of save data with two storage backends: PlayerPrefs and persistent data path (file-based).

---

## SaveLoader

`SaveLoader` is the low-level key-value persistence interface. Use it directly when you need to save and load individual, independent values by string key.

**Class hierarchy**

```
interface SaveLoader
    class PlayerPrefsSaveLoader : SaveLoader, Injectable
    class PersistentDataPathSaveLoader : SaveLoader, Injectable, Cleanable
```

**Interface**

```csharp
public interface SaveLoader
{
    event Action OnClear;

    bool Has(string id);
    T Load<T>(string id, T defaultValue = default);
    void Save<T>(string id, T value);
    void Delete(string id);
    void Clear();
}
```

### PlayerPrefsSaveLoader

Stores primitives (`int`, `float`, `bool`, `string`) directly in `PlayerPrefs`. Complex types are JSON-serialised and stored as strings.

### PersistentDataPathSaveLoader

Stores all data as a `Dictionary<string, JToken>` in a single JSON file under `Application.persistentDataPath`. File streams are kept open for performance and closed when `Clean()` is called. Configure the file name via `PersistentDataPathSaveLoader.Arguments`:

```csharp
binder.Bind<SaveLoader>()
    .ToNew<PersistentDataPathSaveLoader>()
    .WithArgument(new PersistentDataPathSaveLoader.Arguments { FileName = "MyData.txt" })
    .AsSingle();
```

### Usage

```csharp
public class MyComponent : MonoBehaviour, Injectable
{
    private SaveLoader _saveLoader;

    public void Inject(Resolver resolver)
    {
        _saveLoader = resolver.Resolve<SaveLoader>();
    }

    void SaveData()
    {
        _saveLoader.Save("score", 9001);
        _saveLoader.Save("playerName", "Ada");
    }

    void LoadData()
    {
        int score = _saveLoader.Load("score", 0);
        string name = _saveLoader.Load("playerName", "Unknown");
    }
}
```

---

## VersionedDataSaveLoader

`VersionedDataSaveLoader` wraps a `SaveLoader` to add automatic version-based migration for a single typed value. Use it when a single save slot needs to evolve across game versions.

**Class hierarchy**

```
class VersionedDataSaveLoader : Injectable
```

### Usage

```csharp
// Save at the current version
_versionedSaveLoader.Save("myData", myValue, version: 2);

// Load — migrations from the stored version up to the current version are applied automatically
MyData loaded = _versionedSaveLoader.Load<MyData>("myData", defaultValue: new MyData());
```

---

## VersionedDataMigrator and VersionedDataMigrationStep

`VersionedDataMigrator<T>` and `VersionedDataMigrationStep` define a sequential migration chain for a single typed value. Used exclusively with `VersionedDataSaveLoader`.

**Class hierarchy**

```
abstract class VersionedDataMigrator
    class VersionedDataMigrator<T> : VersionedDataMigrator

abstract class VersionedDataMigrationStep
    abstract class VersionedDataMigrationStep<TFrom, TTo> : VersionedDataMigrationStep, Injectable
```

Each `VersionedDataMigrationStep` declares its `TargetVersion` and converts from one data shape to the next. Steps are applied in ascending version order until the data reaches the current version.

### Usage

```csharp
// Define migration steps
public class DataV0ToV1 : VersionedDataMigrationStep<DataV0, DataV1>
{
    public override int TargetVersion => 1;

    protected override DataV1 Migrate(DataV0 data)
    {
        return new DataV1 { Value = data.Value + 0.34f };
    }
}

public class DataV1ToV2 : VersionedDataMigrationStep<DataV1, DataV2>
{
    public override int TargetVersion => 2;

    protected override DataV2 Migrate(DataV1 data)
    {
        return new DataV2 { Value = "Data Value: " + data.Value };
    }
}

// Assemble the migrator (typically inside a MonoInstaller)
var steps = new List<VersionedDataMigrationStep>
{
    resolver.Resolve<DataV0ToV1>(),
    resolver.Resolve<DataV1ToV2>(),
};
VersionedDataMigrator migrator = new VersionedDataMigrator<DataV2>(steps);

// Bind the migrator so VersionedDataSaveLoader can pick it up
binder.Bind<IEnumerable<VersionedDataMigrator>>()
    .To<List<VersionedDataMigrator>>()
    .FromMethod(() => new List<VersionedDataMigrator> { migrator })
    .AsSingle();
```

---

## GameData System

The `GameData` system is the high-level persistence layer. It aggregates data from multiple `DataSaveLoader` instances into a single versioned save blob. Use it when your game has multiple distinct data domains that must be saved, loaded, and migrated together.

### GameDataPersistence

`GameDataPersistence` is the main orchestrator. It coordinates load, save, version checking, and migration across all registered `DataSaveLoader` instances.

**Class hierarchy**

```
class GameDataPersistence : Injectable, Initializable
```

**Public API**

```csharp
ReadonlyObservable<bool> LoadingFailed { get; }
ReadonlyObservable<bool> DataWasReset  { get; }

void Load();
void Save();
void OverrideSave(Dictionary<string, JToken> data, int version);
```

- `Load()` deserialises the saved blob, applies any pending `GameDataMigrator` steps, and dispatches data to each registered `DataSaveLoader`. If the stored version is below `MinSupportedVersion` the entire blob is discarded, defaults are used, and `DataWasReset` fires.
- `Save()` is a no-op when `LoadingFailed` is `true`.
- `OverrideSave()` writes a custom data dictionary and version; use with care.

### GameDataInstaller

`GameDataInstaller` is the scene-level `MonoInstaller` that wires the `GameData` system. Add it to your scene and configure it in the Inspector.

**Inspector fields**

| Field | Default | Description |
|---|---|---|
| Game Data Id | `__GameData__` | The key used to store the blob in the underlying `SaveLoader`. |
| Current Version | `0` | The version number written on every `Save()`. |
| Min Supported Version | `0` | Saves below this version are discarded and data is reset. |
| Migrators | *(empty)* | `GameDataMigrator` MonoBehaviours applied on `Load()`. |
| Save Loaders | *(empty)* | `DataSaveLoader` MonoBehaviours whose data is included in the blob. |
| Load Data On Init | `true` | Automatically calls `Load()` during DI initialisation. |
| Save Data On Clean | `true` | Automatically calls `Save()` during DI cleanup. |

### DataSaveLoader

`DataSaveLoader<TData>` is the base class for per-domain save/load logic. Subclass it once per data type and register the instance in `GameDataInstaller`.

**Class hierarchy**

```
abstract class DataSaveLoader : MonoBehaviour
    abstract class DataSaveLoader<TData> : DataSaveLoader, Injectable
```

**Members to implement**

```csharp
public abstract string DataId { get; }           // unique key within the GameData blob
protected abstract TData GetDefaultData();        // returned when no saved data exists
protected abstract void HandleLoadedData(TData data); // called after deserialisation
protected abstract TData GetData();               // called on every Save()
```

**Usage**

```csharp
public class PlayerSaveLoader : DataSaveLoader<PlayerData>
{
    public override string DataId => "player";

    protected override PlayerData GetDefaultData() => new PlayerData { Level = 1 };

    protected override void HandleLoadedData(PlayerData data)
    {
        // apply data to runtime state
        PlayerManager.Instance.ApplyData(data);
    }

    protected override PlayerData GetData()
    {
        return PlayerManager.Instance.CollectData();
    }
}
```

### GameDataMigrator

`GameDataMigrator` migrates the raw composite data dictionary when the stored version is older than the current version. Subclass it, assign a `Version`, and register the instance in `GameDataInstaller._migrators`. Use `_serializer.SerializeToToken` when writing modified values back to the dictionary.

**Class hierarchy**

```
abstract class GameDataMigrator : MonoBehaviour, Injectable
```

**Usage**

```csharp
public class PlayerDataMigrator_v0Tov1 : GameDataMigrator
{
    public override int Version => 1;

    public override void Migrate(Dictionary<string, JToken> data)
    {
        PlayerData_v0 old = _serializer.Deserialize<PlayerData_v0>(data["player"]);
        PlayerData_v1 next = new PlayerData_v1 { Level = Mathf.RoundToInt(old.LevelFloat) };
        data["player"] = _serializer.SerializeToToken(next);
    }
}
```

Each migrator is applied only when `storedVersion < migrator.Version <= currentVersion`. Multiple migrators are sorted and applied in ascending version order.

---

## JsonSerializer

`JsonSerializer` is an injectable wrapper around Newtonsoft.Json. Inject it wherever serialisation is needed instead of calling Newtonsoft directly.

**Class hierarchy**

```
class JsonSerializer : Injectable
```

**Public API**

```csharp
string Serialize<T>(T value);
JToken SerializeToToken<T>(T value);
T Deserialize<T>(string stringData);
T Deserialize<T>(JToken token);
```

Use `SerializeToToken` / `Deserialize<T>(JToken)` when working inside `GameDataMigrator` or `PersistentDataPathSaveLoader` to avoid unnecessary string round-trips.

Optionally bind a `JsonSerializerSettings` or a `Newtonsoft.Json.JsonSerializer` instance before `JsonSerializer` is injected to customise formatting, culture, or converters:

```csharp
binder.BindToNewSelf<JsonSerializer>()
    .WithArgument(new JsonSerializerSettings { Formatting = Formatting.Indented })
    .AsSingle();
```

---

## Editor Tooling

### Game Data Viewer

An editor window for inspecting and editing raw `GameData` JSON stored in PlayerPrefs. Open it via **Calluna > Game Data Viewer**.

Features:
- Load the JSON blob from any PlayerPrefs key (defaults to `__GameData__`).
- Edit the text in-window and copy it to the clipboard.
- Write clipboard content back to a PlayerPrefs key.

---

## ClearPlayerDataButton

`ClearPlayerDataButton` is a convenience MonoBehaviour that calls `SaveLoader.Clear()` when a UI `Button` is clicked. Attach it to a GameObject alongside a `Button` and wire the reference in the Inspector.

**Class hierarchy**

```
class ClearPlayerDataButton : MonoBehaviour, Injectable
```

---

## Samples

The following samples are available in the Package Manager under **Samples**.

| Sample | Description |
|---|---|
| **Player Prefs Sample** | Demonstrates `PlayerPrefsSaveLoader` and `VersionedDataSaveLoader` with a two-step migration chain (`DataV0 -> DataV1 -> DataV2`). |
| **Persistent Data Path Sample** | Demonstrates `PersistentDataPathSaveLoader` with file-based storage. |
| **Game Data Sample** | Demonstrates the full `GameData` system with multiple `DataSaveLoader` instances and `GameDataMigrator` steps. |
| **Failed Game Data Loading Sample** | Demonstrates how `GameDataPersistence.LoadingFailed` and `DataWasReset` observables behave when loading encounters an error or an unsupported version. |
