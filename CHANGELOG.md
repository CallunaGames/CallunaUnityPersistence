## [1.6.0] - 2026-04-08

### Breaking Changes
- `GameDataPersistence` no longer stores all save data as a single JSON blob. Each `DataSaveLoader` is now saved as its own key in the underlying `SaveLoader`, and the data version is stored under the reserved key `__version__`. Existing save files in the old single-blob format are automatically migrated to the new format on the first `Load()` call — no data loss occurs. The `GameDataId` field on `GameDataInstaller` must still match the value used before upgrading so the migration can find the old blob.
- `GameDataPersistence.OverrideSave` now writes each entry in the supplied dictionary as its own key and writes the version under `__version__`, rather than saving a single `GameData` blob.

### Added
- `SqliteSaveLoader`: a new `SaveLoader` backed by a local SQLite database. Uses sqlite-net (MIT, included as source in `Runtime/ThirdParty/SQLite.cs`) and the native sqlite3 library. The native `sqlite3.dll` for Windows x64 is bundled with this package; macOS, Linux, iOS, and Android provide sqlite3 as a system library. Each key-value pair is a separate row, so only rows for changed data are written on each save. Not supported on WebGL.
- `SqliteSaveLoaderInstaller`: a `MonoInstaller` that binds `SqliteSaveLoader`. Configure the database file name via the `File Name` Inspector field (default: `SaveData.db`).
- `IDataSaveLoader.IsDirty` (default: `true`) and `IDataSaveLoader.MarkClean()` (default: no-op): opt-in dirty tracking. `GameDataPersistence.Save()` skips serialization for loaders that report `IsDirty == false`, reducing CPU overhead when most data has not changed. All loaders are marked clean after a successful save. Existing `IDataSaveLoader` implementations are unaffected — the defaults preserve always-dirty behaviour.
- `DataSaveLoader.IsDirty` and `DataSaveLoader.MarkClean()` are exposed as `virtual` so `MonoBehaviour`-based subclasses can `override` them with standard C# syntax to opt into dirty tracking.
- `GameDataPersistence.Save()` now collects serialized data from all dirty loaders before writing anything. If any `GetSerializedData()` call throws, no data is written to storage, preventing partial saves that would leave data in an inconsistent state.
- SQLite sample (`Samples~/SqliteSample`) demonstrating save, load, and clear via `SqliteSaveLoader`.

## [1.5.0] - 2026-04-07

### Breaking Changes
- `DataMigrator` renamed to `VersionedDataMigrator` and `DataMigrator<T>` renamed to `VersionedDataMigrator<T>`. Update all references and subclasses.
- `DataMigrationStep` renamed to `VersionedDataMigrationStep` and `DataMigrationStep<TFrom, TTo>` renamed to `VersionedDataMigrationStep<TFrom, TTo>`. Update all references and subclasses.
- `VersionedDataMigrator.DataType` and `VersionedDataMigrator.Migrate()` are now `internal`; external code that overrode or called these members must be removed.
- `VersionedSaveData` is now `internal`; callers that referenced this type directly must remove those references.
- `GameData` and `GameDataEntry` are now `internal`; callers that referenced these types directly must remove those references.
- `LoadGameDataOnInit` and `SaveGameDataOnClean` are now `internal`; these components are managed automatically by `GameDataInstaller` and must not be added to scenes or referenced from user code directly.
- `TextFileReadWriter` is now `internal`; users who referenced this class directly must remove those usages and rely on `PersistentDataPathSaveLoaderInstaller` to wire the dependency instead.
- `GameDataPersistence.Arguments` is now `internal`; callers that constructed or referenced this class directly must configure persistence through `GameDataInstaller` in the Inspector instead.
- `VersionedDataMigrator<T>` now validates at construction time that migration steps form a contiguous chain from version 1 to the highest registered version. A missing step for any intermediate version throws `InvalidOperationException` immediately rather than failing silently at runtime.

### Added
- `PlayerPrefsSaveLoaderInstaller`: a new `MonoInstaller` that binds `PlayerPrefsSaveLoader` as the `SaveLoader` and wires a `JsonSerializer`. Add this to your `MonoContext` instead of assembling the bindings manually.
- `PersistentDataPathSaveLoaderInstaller`: a new `MonoInstaller` that binds `PersistentDataPathSaveLoader` as the `SaveLoader`, wires `TextFileReadWriter` and `JsonSerializer`, and exposes a `FileName` field in the Inspector. Add this to your `MonoContext` instead of assembling the bindings manually.

### Fixed
- `GameData` save data was silently discarded on load because `internal` fields on `GameData` and `GameDataEntry` were invisible to Newtonsoft.Json serialization. All affected fields now carry `[JsonProperty]` and round-trip correctly.

## [1.4.0] - 2026-04-06

### Breaking Changes
- `DataMigrator` renamed to `VersionedDataMigrator`. Update all references and subclasses.
- `DataMigrator<T>` renamed to `VersionedDataMigrator<T>`. Update all generic usage and subclasses.
- `DataMigrationStep` renamed to `VersionedDataMigrationStep`. Update all references and subclasses.
- `DataMigrationStep<TFrom, TTo>` renamed to `VersionedDataMigrationStep<TFrom, TTo>`. Update all generic usage and subclasses.
- `VersionedDataMigrator.DataType` is now `internal abstract`; external code that overrode this property must be removed.
- `GameDataPersistence.Arguments` is now `internal`; callers that constructed or referenced this class directly must switch to configuring persistence through `GameDataInstaller` in the Inspector.
- `GameDataEntry.Data` has been renamed to `GameDataEntry.Payload` and its type changed from `string` to `JToken`. Any code that read or wrote `GameDataEntry.Data` as a serialized JSON string must be updated to use `Payload` as a `JToken`. `DataSaveLoader.Load` now accepts a `JToken` instead of a `string`, and `DataSaveLoader.GetSerializedData` now returns a `JToken`. `GameDataPersistence.OverrideSave` now takes `Dictionary<string, JToken>` instead of `Dictionary<string, string>`.

### Added
- `GameDataPersistence.DataWasReset` observable: becomes `true` when loaded save data is older than the configured `LastSupportedVersion`, allowing UI to react to a forced reset.
- `GameDataInstaller` now exposes `Current Version` and `Last Supported Version` fields in the Inspector. When the stored data version is below `LastSupportedVersion`, all game data is automatically reset to defaults instead of migrating.

### Fixed
- Game data was silently overwriting the first save entry for every `DataSaveLoader` in the collection due to a missing index increment in the save loop. All entries are now saved correctly.
- `JsonSerializer.SerializeToToken` now uses `JToken.FromObject` instead of `JObject.FromObject`, allowing values that are not JSON objects (e.g. arrays, primitives) to be serialized without error.
