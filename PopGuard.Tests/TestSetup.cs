// Static state in PopGuard (Strings.Culture, Logger) makes parallel tests flaky,
// so run tests sequentially.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
