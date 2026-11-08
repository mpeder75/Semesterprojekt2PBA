// The gateway tests start whole hosts through WebApplicationFactory. Starting several hosts at the same time
// made CircuitBreakerConfigurationTests fail now and then on the CI runner (an ObjectDisposedException instead of
// the expected OptionsValidationException) while always passing locally. The suite runs in a couple of seconds,
// so it runs one test at a time to take the race out of the picture.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
