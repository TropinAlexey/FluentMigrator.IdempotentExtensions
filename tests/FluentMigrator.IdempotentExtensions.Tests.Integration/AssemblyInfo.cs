using Xunit;

// Each provider test class spins up its own Docker container per test method (full isolation, no
// shared state between tests). Running the three provider classes in parallel means up to 3x that
// container churn hitting Docker at once, which caused connect/command timeouts under load — disable
// cross-class parallelization so provider suites run one after another instead.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
