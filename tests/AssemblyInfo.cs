using Xunit;

// SettingsStore, SoundHelper and LocalizationManager hold static state,
// so test classes must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]