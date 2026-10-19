using Xunit;

// The harness writes progress to Console.Out, and these tests redirect it. Keep them sequential so
// one test cannot capture another's output.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
