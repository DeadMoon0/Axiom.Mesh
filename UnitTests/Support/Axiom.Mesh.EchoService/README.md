# Axiom.Mesh.EchoService

A test program, not shipped: the echo service of `Axiom.Mesh.Testing` (`TestEchoService`) as an executable of its
own, over the service runtime of `Axiom.Mesh.Service` (`ServiceProgram.Run`). The Host's supervision tests
(`UnitTests/Axiom.Mesh.Host.Tests`, `ServiceSpawnTests`) list it as a spawned service (`spawn`: the `dotnet` host,
`args`: this assembly) and watch the daemon start it with a fresh token, call it, and restart it after it exited.

It defines Package kind 100 with Entry kind 100, remembers the Entries of that kind it saw, emits `seen` for each, and
answers `echo`, `count` and `write`.
