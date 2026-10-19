using System.Runtime.CompilerServices;

// The parity harness is an executable, not a library, but its runner and verdict helpers are
// exercised directly by fake-engine tests. Keep that surface internal to the tool.
[assembly: InternalsVisibleTo("Opipp.Parity.Tests")]
