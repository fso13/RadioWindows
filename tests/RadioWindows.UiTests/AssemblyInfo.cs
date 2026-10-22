using Xunit;

// Avalonia's headless platform is process-wide: run UI tests sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
