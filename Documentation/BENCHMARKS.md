# Benchmarks

The numbers behind the performance rows of the fundamentals plan §5, each with the command that measured it, the
machine and the date (ARCHITECTURE.md rule 9). A number here promises nothing: the promises are the allocation budgets
and release floors of [GUARANTEES.md](GUARANTEES.md#performance), which tests hold. A target this machine misses is stated
as a miss with the STRUCTURAL-DEBT.md entry that carries it.

The suite is `Benchmarks/Axiom.Mesh.Benchmarks` ([README](../Benchmarks/Axiom.Mesh.Benchmarks/README.md)): BenchmarkDotNet
classes for what fits in a loop, and scenarios on real nodes (loopback sockets, real TLS 1.3, a directory store and an
LDB index per node, the real pipe) for what moves a gigabyte, walks ten thousand Entries or needs percentiles; each
scenario ran once for the number below.

## Machine and date

| | |
|---|---|
| Machine | 13th Gen Intel Core i9-13950HX 2.20 GHz, 24 physical / 32 logical cores (a laptop) |
| Operating system | Windows 11 Enterprise 10.0.26200 (25H2) |
| .NET | SDK 10.0.302, runtime 10.0.13, X64 RyuJIT x86-64-v3, Release |
| BenchmarkDotNet | 0.15.8, default job (MemoryDiagnoser on) |
| Date | 2026-10-31 |
| Store | the system drive's temporary folder (`%TEMP%`) |

Repeated runs of the scenarios on this laptop varied by up to about a quarter (thermal state, background scanning); the
figures are one run each, the last one made with the code as committed.

## Results against the targets

Micro benchmarks, from `Benchmarks/Axiom.Mesh.Benchmarks`:
`dotnet run -c Release -- --filter '*HashBenchmarks*' '*ChunkerBenchmarks*' '*EntrySignatureBenchmarks*' '*CodecBenchmarks*' '*StoreBenchmarks*' '*IndexBenchmarks*' --exporters json`.
Scenarios, from the same folder: `dotnet run -c Release -- scenario <name>`.

| Plan §5 row | Target | Measured | Verdict | Measured by |
|---|---|---|---|---|
| SHA-256 of a 1 MiB chunk | ≥ 1 GiB/s per core | 393.4 µs = 2.48 GiB/s, 0 B | met | `HashBenchmarks.Sha256Of1MiB` |
| Chunker cut + hash 64 MiB | ≥ 800 MiB/s, no allocation per Chunk in steady state | streaming cut (`Chunker.Seal` in a reused buffer): 29.54 ms = 2 167 MiB/s, 0 B; in-memory `Chunker.Cut`: 38.16 ms = 1 677 MiB/s, 64 MiB (one copy per Chunk, the Chunk objects) | met (streaming path) | `ChunkerBenchmarks.CutAndSeal64MiB`, `CutInMemory64MiB` |
| Sign an Entry | ≥ 10 000 / s per core | 158.35 µs = 6 315 / s (encode, sign, parse back, verify the own signature), 2 016 B | **miss**, debt 61 | `EntrySignatureBenchmarks.SignEntry` |
| Verify an Entry | ≥ 5 000 / s per core | 77.67 µs = 12 875 / s, 272 B | met | `EntrySignatureBenchmarks.VerifyEntry` |
| Codec, small messages | < 1 µs, only the message allocated | `Hello` encode 66.5 ns / 32 B, decode 66.7 ns / 192 B; an Entry as `Object` encode 28.8 ns / 0 B, decode 274.4 ns / 96 B (the decoder hashes the object) | met | `CodecBenchmarks` (`Hello`, `EntryObject`) |
| Codec, `PackageChanged` with 256 Heads | (not a small message) | encode 4.29 µs / 0 B, decode 1.93 µs / 18 608 B (the message and its 256 Heads) | recorded | `CodecBenchmarks` (`PackageState256Heads`) |
| Codec, 1 MiB `Object` | ≥ 2 GiB/s, one copy per direction | encode 46.1 µs = 21.2 GiB/s, 0 B (one copy into the frame buffer); decode 393.9 µs = 2.48 GiB/s, 96 B (no copy; the time is the SHA-256 the decoder verifies) | met | `CodecBenchmarks` (`ChunkObject1MiB`) |
| ObjectStore put 1 MiB | ≥ 100 MiB/s with flush | 3.22 ms = 311 MiB/s per put, each its own batch (file, flush, rename, index commit), 118.7 KB | met | `StoreBenchmarks.Put1MiB` |
| ObjectStore get 1 MiB | ≥ 1 GiB/s cached | 301.3 µs = 3.24 GiB/s, 24.6 KB | met | `StoreBenchmarks.Get1MiB` |
| ObjectStore small put | ≤ 5 ms | 2.67 ms (100 bytes, its own batch) | met | `StoreBenchmarks.PutSmall` |
| LDB entry commit | ≥ 1 000 commits/s at full durability | 909.4 µs = 1 100 commits/s (an Entry row and its object row, `Durability.Full`), 107.6 KB | met | `IndexBenchmarks.EntryRowCommit` |
| LDB 1 000 entries in one transaction | ≤ 50 ms | 88.4 ms ± 25.4 ms, 49.9 MB allocated by LDB | **miss**, debt 62 | `IndexBenchmarks.ThousandEntryRowsInOneTransaction` |
| (one Entry through the store writer) | (no target) | 3.53 ms = 283 / s: file, flush, Entry, object, Head and pin rows, counters, one batch each | recorded | `IndexBenchmarks.EntryThroughTheWriter` |
| 1 GiB of chunks over loopback (two nodes, real TLS) | ≥ 400 MiB/s | 5.46 s = 188 MiB/s (M11: 5.3 s, 193 MiB/s; an earlier M12 run the same day: 4.11 s, 249 MiB/s) | **miss**, debt 60 | `scenario loopback` |
| 10 000-entry history walk | ≤ 10 s | 9.25 s = 1 081 Entries/s (M11: 42.9 s); the release floor run the same evening measured 11.7 s (`AXIOM_MESH_FLOORS=1 dotnet test IntegrationTests/Axiom.Mesh.Integration.Tests -c Release -f net10.0 --filter Category=Floor`) | met in the benchmark run, at the edge: within this machine's run-to-run spread | `scenario walk` |
| `GetIdentity` over the real pipe, latency | p50 ≤ 200 µs, p99 < 1 ms | p50 66 µs, p99 115 µs, max 4.4 ms over 20 000 round trips | met | `scenario identity` |
| `GetIdentity`, allocations in the Host | ≤ 2 allocations in the Host | 3 568 bytes per round trip in the whole process (client, pipe and Host); the client's share is ≤ 320 bytes by `Local.Tests: AllocationTests` | **miss**, debt 63 | `scenario identity` |

## Added in M12

| Measurement | Measured | Measured by |
|---|---|---|
| `PutObject` of 256 new 1 MiB Chunks over the real pipe from one service connection | 1 in flight 83 MiB/s; 8 in flight 140 MiB/s; 16 in flight 158 MiB/s; 32 in flight 169 MiB/s | `scenario putobject` |
| `StoreFiles.PutFileAsync` of a 256 MiB file | 1 Chunk in flight 84 MiB/s; 8 (the default) 136 MiB/s. The same file, one in flight, against the Host as it was before M12 (every `PutObject` ordered behind the last, object and pin in two batches): 80 MiB/s, measured once in the same session with those two Host files reverted | `scenario putfile` |
| Forwarding a 1 MiB `Object`, what the frame layer allocates | over an in-memory duplex pipe (ProtoStream.Testing's transport): 341 KiB per object; over loopback TCP with TLS 1.3 (`SslStream`, the stream adapters): 22 KiB per object | `scenario forward` |
| Forwarding 1 GiB between two real nodes, what the process allocates | 663 MiB, 0.65 MiB per 1 MiB Chunk; sampled by type (`AXIOM_MESH_ALLOCATIONS=1`): byte arrays, LDB's `SlotRow[]` and `TreeEntry[]` (query and index arrays), `Segment`; Core's own object buffers are pooled | `scenario loopback` |

## What M12 changed, measured

- **History walk, 42.9 s to 9.25 s** (debt 52): parents are wanted as soon as intake admits an Entry, and the store writer
  writes a batch's object files several at a time before its transaction. A probe during the work showed the cause: the
  writer, one flushed file per Entry, ran at about one Entry per millisecond, its queue filled to 4 096 and intake waited
  up to 0.3 s for room.
- **File puts** (debt 57): `PutFileAsync` keeps up to 8 Chunks in flight in reused buffers sealed in place; the Host runs
  `PutObject` outside the ordered lane and queues each object with its pin, so concurrent puts share a batch.
- **Allocation per forwarded object** (debt 27): the visibility walk remembers the whole path from a visible Entry (the
  Chunks of one ChunkList cost one walk), and the want scheduler's settle checks allocate no delegate per want; the
  sampled `Func<DeviceId, bool>` and closure allocations of the first loopback measurement (68 MiB per GiB) are gone.
- **`ObjectBuilders.Chunk`** builds one array of the exact size instead of growing a buffer to 2 MiB and copying it.

## Misses

| Target | Debt | Why, in one line |
|---|---|---|
| Sign ≥ 10 000 / s | 61 | `EntryDraft.Sign` verifies its own signature by design (M1); signing alone is about 80 µs. |
| 1 000 entries in one LDB transaction ≤ 50 ms | 62 | LDB 0.1.0 maintains five indexes per Entry row and allocates about 50 KB per row inside the transaction. |
| 1 GiB over loopback ≥ 400 MiB/s | 60 | Every object is a file flushed on its own, and every stored, served and checked object costs index reads and writes; the receiver's writer bounds the stream. |
| ≤ 2 allocations in the Host for `GetIdentity` | 63 | The local port's request path (state machines, the answer, the outbox entry, the record) allocates per request. |

## CI guard

- The allocation budgets are `[ReleaseFact]`s in the unit suites (GUARANTEES.md, Performance).
- The release floors (`Integration.Tests: FloorTests`, `AXIOM_MESH_FLOORS=1`, Release) bound 1 GiB over loopback at 60 s
  and the 10 000-entry walk at 120 s. Their run on 2026-10-31 on the machine above
  (`AXIOM_MESH_FLOORS=1 dotnet test IntegrationTests/Axiom.Mesh.Integration.Tests -c Release -f net10.0 --filter Category=Floor`):
  1 GiB in 4.8 s (214 MiB/s), the walk in 11.7 s.
- `.github/workflows/benchmarks.yml` runs the hash, chunker, signature and codec benchmarks on demand and weekly, uploads
  the results and warns when a mean is more than twice its committed number (`Benchmarks/Axiom.Mesh.Benchmarks/baseline.json`,
  written from this run by `Scripts/benchmark-compare.ps1 -Write`); it never fails the build. A runner is not this machine,
  so the warning catches an order of magnitude, not a percentage.
