# Axiom.Mesh.Benchmarks

The benchmark suite of the fundamentals plan §5. It ships nothing and is never run by `dotnet test`; its numbers, each
with the command that measured it, the machine and the date, live in
[Documentation/BENCHMARKS.md](../../Documentation/BENCHMARKS.md).

## Micro benchmarks (BenchmarkDotNet)

Run from this folder, Release only:

```
dotnet run -c Release -- --filter *HashBenchmarks*
dotnet run -c Release -- --filter *ChunkerBenchmarks* *EntrySignatureBenchmarks* *CodecBenchmarks*
dotnet run -c Release -- --filter *StoreBenchmarks* *IndexBenchmarks*
```

| Class | Measures |
|---|---|
| `HashBenchmarks` | SHA-256 of a 1 MiB Chunk |
| `ChunkerBenchmarks` | 64 MiB cut and hashed: the streaming cut into one reused buffer (`Chunker.Seal`) and the in-memory `Chunker.Cut` |
| `EntrySignatureBenchmarks` | signing an Entry (encode, sign, parse back, verify) and verifying one |
| `CodecBenchmarks` | AMP/1 encode and decode of `Hello`, a `PackageChanged` with 256 Heads, an Entry and a 1 MiB Chunk as `Object` |
| `StoreBenchmarks` | the object store on disk: 1 MiB put with its flush, small put, cached 1 MiB get |
| `IndexBenchmarks` | the LDB index at full durability: one Entry row committed, 1 000 in one transaction, one Entry through the store writer |

The store and index benchmarks use Core's internals (Core opens them to this assembly alone, see
`ConventionTests.InternalsAreOpenToTheOwnTestAssemblyOnly`) and write to the system's temporary folder.

## Scenarios (real nodes, once each)

What moves a gigabyte or ten thousand Entries, or needs percentiles, runs once on real nodes (loopback sockets, real
TLS 1.3, a directory store and an LDB index per node, the real pipe), built by the integration suite's
`RealMeshFixture`, compiled unchanged into this project:

```
dotnet run -c Release -- scenario loopback    # 1 GiB of Chunks between two nodes
dotnet run -c Release -- scenario walk        # a history of 10 000 Entries walked
dotnet run -c Release -- scenario identity    # GetIdentity over the real pipe: p50, p99, allocations
dotnet run -c Release -- scenario putobject   # PutObject of 1 MiB Chunks, 1, 8, 16 and 32 in flight
dotnet run -c Release -- scenario putfile     # StoreFiles.PutFileAsync of a 256 MiB file, 1 and 8 in flight
dotnet run -c Release -- scenario forward     # where forwarding a 1 MiB Object allocates, layer by layer
```

`AXIOM_MESH_ALLOCATIONS=1` makes the loopback scenario print the types its allocations were sampled as.
