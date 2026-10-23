using NUnit.Framework;

// Fixtures share nothing (each builds its own world in OneTimeSetUp, Sim.Core has no mutable
// statics), so they run side by side. Tests inside a fixture stay sequential: some mutate the
// fixture's clubs and later tests read them in name order.
[assembly: Parallelizable(ParallelScope.Fixtures)]
