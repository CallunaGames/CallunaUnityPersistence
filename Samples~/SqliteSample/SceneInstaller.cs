using Calluna.DI;

namespace Calluna.Persistence.Samples.Sqlite
{
    /// <summary>
    /// Optional scene-level installer for the SQLite sample.
    /// Add this alongside <see cref="SqliteSaveLoaderInstaller"/> on your MonoContext
    /// to override JSON serializer settings for the sample scene.
    /// </summary>
    public class SceneInstaller : MonoInstaller
    {
        public override void InstallBindings(Binder binder)
        {
            // No overrides needed for the default SQLite setup.
            // Add custom JsonSerializerSettings here if required.
        }
    }
}
