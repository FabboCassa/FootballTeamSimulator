using NUnit.Framework;

// Every fixture builds its own host and in-memory SQLite database, so fixtures run side by side.
// Tests inside a fixture stay sequential: they share that fixture's host and data.
[assembly: Parallelizable(ParallelScope.Fixtures)]
