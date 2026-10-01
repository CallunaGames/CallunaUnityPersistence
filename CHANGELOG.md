## [1.9.0-pre.1] - 2026-09-30

### Added
- **Backups.** Every successful `GameDataPersistence.Load()` backs up the loaded data as a JSON file (background thread, atomic write). A load that migrated also backs up the data before the migration under its old save version. Retention per save version: the newest N (default 3) plus the newest backup of the previous game build; the newest M save versions (default 3) up to the current one; backups of newer save versions are never deleted. A failed load creates and deletes no backups. Configured in `GameDataInstaller`: Create Backups, Backups Per Version, Backup Versions Kept, Backup Folder.
- `GameDataPersistence.GetBackups()` and `RestoreBackup(GameDataBackup)`: restore a backup after a failed load. The replaced save is kept aside (`<file>.before-restore-<time>`) by `SqliteSaveLoader` and `PersistentDataPathSaveLoader`; the backup is not modified. Saving is blocked until the next `Load()`.
- `GameDataPersistence.LastLoadResult` (`GameDataLoadResult`): why loading failed - `Configuration`, `Storage`, `Migration` or `DataSaveLoader` - with the ids of the failed loaders, the stored version and the exceptions.

### Changed
- **A throwing `DataSaveLoader` now fails the load** (`LoadingFailed = true`). Before, the loader silently got its default data, `LoadingFailed` stayed `false`, and the next save wrote the default over the real data. All loaders are still tried, so every failure is reported.
- **A failed load writes nothing.** Migrated data is only stored once every `DataSaveLoader` has loaded it - before, it was written before the loaders ran, so a failing loader left the save migrated to a version the previous game version can't load.

---

## [1.8.0] - 2026-09-30

Requires `com.calluna.core` 1.7.0 and `com.calluna.di` 1.5.2. Uses no API that core 2.0.0 removes.

### Fixed
- `GameDataPersistence.Save()` and `OverrideSave()` now wait for a background write started by `SaveAsync()`. Before, a synchronous save during a running async write could be followed by the async write's queued, older snapshot - overwriting newer data with older.
- After stored data below `MinSupportedVersion` was reset, the next `Save()` writes every loader, including ones with dirty tracking that report `IsDirty == false`. Before, their stale data stayed in storage and the new version key declared it current, so it was loaded on the next start.
- A slice that a `GameDataMigrator` removes from the dictionary is now deleted from storage. The migrated data is written in one batch (one SQLite transaction instead of one per key).
- `SqliteSaveLoader.Has()` and `PersistentDataPathSaveLoader.Has()` returned `false` for existing data until another call had opened the database or read the file. They now open/read it like `Load()` - without creating a missing database or file.
- `SqliteSaveLoader`: operations after `Clean()` reopened a connection that was never closed. They now run on a short-lived connection that is closed right away (keeping the intent that nothing stays open after cleanup, without losing a final save that runs after the loader's cleanup). `PersistentDataPathSaveLoader` likewise no longer reopens its file streams after `Clean()`.
- `SqliteSaveLoader.Clean()` could close the connection between `BeginBatch` and `CommitBatch`, since the lock was only held per operation. A batch now holds it until it is committed or rolled back.
- `package.json`: dependencies are version numbers instead of Git URLs (which UPM doesn't support in package dependencies) - `com.calluna.core` 1.7.0, `com.calluna.di` 1.5.2; `unity` / `unityRelease` corrected to `6000.0` / `33f1`.

### Changed
- `GameDataPersistence` no longer uses the implicit `T -> Observable<T>` conversion, which core 2.0.0 removes.
- `JsonSerializer` has one configuration: a bound `JsonSerializerSettings` (e.g. with converters) now applies to `JToken`s as well - i.e. to every `DataSaveLoader` - not only to strings. Without bound settings the former token defaults apply to both: invariant culture, null values omitted, no indentation. Strings produced by `Serialize()` therefore no longer contain `null` properties; reading them is unaffected.
- `PersistentDataPathSaveLoader` and `PlayerPrefsSaveLoader` support batches: a `GameDataPersistence` save rewrites the file / calls `PlayerPrefs.Save()` once instead of once per key.
- Test assembly: root namespace `Calluna.Persistence.Tests` (was `Calluna.Template.Tests`).

### Deprecated
- Editor window **Calluna > Game Data Viewer**: shows the single-blob PlayerPrefs format replaced in 1.6.0. Removed in 2.0.0.

## [1.7.1] - 2026-07-22

### Fixed
- `SqliteSaveLoader.Clean()` could close the underlying connection while a background write dispatched by `GameDataWriter.SaveAsync()` was still in progress, since DI cleanup order is not guaranteed. This produced `SQLiteException: bad parameter or other API misuse` on quit. `Clean()` now waits for any in-flight operation to finish before closing the connection (bounded by a 5s timeout, after which it logs a warning and closes anyway rather than hanging indefinitely).

## [1.7.0] - 2026-04-09

### Added
- `GameDataPersistence.SaveAsync()`: serializes all dirty loaders on the calling (main) thread, then writes the resulting data to storage on a background thread. Call this instead of `Save()` to avoid stalling the game loop during I/O.
- `GameDataPersistence.FlushPendingWrite()`: blocks the calling thread until any in-flight background write started by `SaveAsync()` has completed. Use this before quitting or loading a new scene to ensure data is fully persisted.
- `SqliteSaveLoader` now supports a `SynchronousOff` connection option (`PRAGMA synchronous=OFF`), reducing fsync overhead for faster writes when full durability guarantees are not required.
- SQLite sample (`Samples~/SqliteSample`) demonstrating save, load, and clear via `SqliteSaveLoader`.

### Fixed
- `PersistentDataPathSample/SaveLoadTester` was not unregistering its `Clear` button listener in `Clean()`, causing a stale callback to remain after the component was torn down.

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
