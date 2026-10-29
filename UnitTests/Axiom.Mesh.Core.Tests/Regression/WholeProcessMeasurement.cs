namespace Axiom.Mesh.Core.Tests.Regression;

/// <summary>
/// The tests that measure the whole process (allocations across the threads of two nodes): they run alone, after every
/// other test, so no other test's work is counted.
/// </summary>
[CollectionDefinition(nameof(WholeProcessMeasurement), DisableParallelization = true)]
public sealed class WholeProcessMeasurement;
