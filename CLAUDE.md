# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Package Overview

`com.calluna.persistence` (v1.3.0) — A Unity UPM package for game data persistence. Provides serialization, versioning, and migration of save data with two storage backends: PlayerPrefs and persistent data path (file-based). Depends on `com.calluna.core` and `com.calluna.di` for dependency injection, and Newtonsoft.Json for serialization.

## Build & Test

There are no standalone build scripts. All compilation is handled by Unity. Tests run through Unity's Test Runner (Window > Testing > Test Runner). The test assembly is `Calluna.Template.Tests` located in `Tests~/`.

To run tests via CLI: `Unity -runTests -testPlatform EditMode -projectPath <path>`

## Architecture

There are two distinct persistence systems:

### 1. Low-Level: `SaveLoader` (single key-value pairs)

`SaveLoader` (interface in `Runtime/SaveLoader/`) defines `Has`, `Load<T>`, `Save<T>`, `Delete`, `Clear`. Two implementations:
- `PlayerPrefsSaveLoader` — primitives go directly to PlayerPrefs; complex types serialize to JSON
- `PersistentDataPathSaveLoader` — stores all data as `Dictionary<string, JToken>` in a single JSON file at `Application.persistentDataPath`. Keeps file streams open for performance. The `FileName` argument is configured during DI binding.

For single-type versioned data, `VersionedDataSaveLoader` wraps a `SaveLoader` and applies `DataMigrator<T>` instances automatically on load.

### 2. High-Level: `GameData` (composite save with coordinated migration)

`GameDataPersistence` (`Runtime/GameData/`) is the main orchestrator. It aggregates multiple `DataSaveLoader` instances (one per data type) into a single `GameData` blob (a `GameDataEntry[]` of `{Id, JToken}` pairs), serialized as one key in the underlying `SaveLoader`.

**Save flow:** Each `DataSaveLoader.GetSerializedData()` → `JToken` → collected into `Dictionary<string, JToken>` → wrapped in `GameData` (with version) → saved via `SaveLoader`.

**Load flow:** `SaveLoader.Load<GameData>` → version check → apply ordered `GameDataMigrator` steps in-place on the `Dictionary<string, JToken>` → dispatch each entry to its `DataSaveLoader.Load(JToken)` (or `LoadDefault()` if missing).

**Known performance issue:** Every `Save()` call serializes all data for all `DataSaveLoader` instances simultaneously, which can be costly.

### Migration Systems

- **GameData migrations** (`GameDataMigrator` subclass, MonoBehaviour): Receives the raw `Dictionary<string, JToken>` and mutates it in place. Ordered by `Version` property. Applied during `GameDataPersistence.Load()` if the stored version < current version. Data is reset entirely if stored version < `LastSupportedVersion`.
- **Single-type migrations** (`DataMigrator<T>` + `DataMigrationStep<TFrom, TTo>`): Steps registered by target version, applied sequentially. Each step deserializes from the old type, transforms, and re-serializes to the new type.

### DI Integration

Uses `com.calluna.di` with interfaces: `Injectable`, `Initializable`, `Cleanable`, `MonoInstaller`. `GameDataInstaller` is the scene-level MonoInstaller that wires everything. It optionally instantiates `LoadGameDataOnInit` (calls `Load()` on DI init) and `SaveGameDataOnClean` (calls `Save()` on DI cleanup) via the `_loadDataOnInit` / `_saveDataOnClean` toggles.

### State & Error Handling

`GameDataPersistence` exposes two `ReadonlyObservable<bool>` properties: `LoadingFailed` and `DataWasReset`. `Load()` and `Save()` catch all exceptions and log them; `Save()` is a no-op if `LoadingFailed` is true. Exception messages are acknowledged as insufficiently descriptive — prefer improving specificity when touching error paths.

### Editor Tooling

`Runtime/Editor/GameDataViewerWindow.cs` — Editor window at **Calluna > Game Data Viewer** for inspecting/editing raw GameData JSON stored in PlayerPrefs. Supports load, edit, copy to clipboard, and write-back.

## Key Conventions

- `DataSaveLoader<TData>` handles boilerplate serialization; concrete subclasses implement `DataId`, `HandleLoadedData`, `GetDefaultData`, and `GetData`.
- `DataSaveLoader` and `GameDataMigrator` are MonoBehaviours **intentionally** — this allows users to configure serialized fields in the Inspector. They are not scene objects with lifecycle concerns; they function as injectable services whose configuration is authored in the Inspector.
- `GameDataMigrator` subclasses are MonoBehaviours registered in `GameDataInstaller._migrators`.
- Serialization always goes through `JsonSerializer` (injectable Newtonsoft wrapper); avoid calling Newtonsoft directly.
- `TextFileReadWriter` is `internal` — it is an implementation detail of `PersistentDataPathSaveLoader`. Users bind the save loader via `PersistentDataPathSaveLoaderInstaller`, which handles the `TextFileReadWriter` binding internally. Tests access it via `InternalsVisibleTo("Calluna.Persistence.Tests")` declared in `Runtime/AssemblyInfo.cs`.
- `PlayerPrefsSaveLoaderInstaller` and `PersistentDataPathSaveLoaderInstaller` are the intended entry points for wiring a `SaveLoader`. Users add one of these MonoInstallers to their scene's MonoContext instead of assembling the bindings manually.
- `PersistentDataPathSaveLoader` uses `JToken` internally (not `string`) to avoid double-escaping; `JsonSerializer.SerializeToToken` / `Deserialize<T>(JToken)` are the appropriate overloads for this path.
