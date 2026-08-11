using Xunit;

// FormRegistry is a process-wide static service locator, so test classes that install handlers
// through FormRegistryScope must not run concurrently. xUnit parallelises test classes by
// default; without this the stubs of one class leak into another.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
