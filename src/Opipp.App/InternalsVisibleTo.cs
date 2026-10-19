using System.Runtime.CompilerServices;

// The STA tests drive the production Form1 members (start_processing, cancel_processing, the
// status labels) rather than a parallel implementation, so they need the internals.
[assembly: InternalsVisibleTo("Opipp.App.Tests")]
