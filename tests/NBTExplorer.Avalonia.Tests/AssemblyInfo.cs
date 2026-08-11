using Xunit;

// FormRegistry and NbtClipboardController are process-wide static service locators — that is the
// seam the model uses to reach the UI, and it is deliberate. It also means any two tests that
// install handlers cannot run at the same time: xUnit parallelises test CLASSES by default, so
// one class's stubs were being overwritten by another's mid-run, and the suite failed
// intermittently with a different count each time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
