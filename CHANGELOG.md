## [1.5.0] - 2026-04-07

### Breaking Changes
- `DataMigrationStep` renamed to `VersionedDataMigrationStep` and `DataMigrationStep<TFrom, TTo>` renamed to `VersionedDataMigrationStep<TFrom, TTo>`. Update all references and subclasses.
- `DataMigrator` renamed to `VersionedDataMigrator` and `DataMigrator<T>` renamed to `VersionedDataMigrator<T>`. Update all references and subclasses.
- `VersionedDataMigrator.DataType` and `VersionedDataMigrator.Migrate()` are now `internal`; external code that overrode or called these members must be removed.
- `VersionedSaveData` is now `internal`; callers that referenced this type directly must remove those references.
- `GameDataEntry` is now `internal`; callers that referenced this struct directly must remove those references.
- `GameDataPersistence.Arguments` is now `internal`; callers that constructed or referenced this class directly must configure persistence through `GameDataInstaller` in the Inspector instead.

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
