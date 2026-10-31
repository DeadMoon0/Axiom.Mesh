# Axiom.Mesh.TlsScan

The target of the TLS scan that enforces SPEC.md §4.2 ([Documentation/SECURITY.md](../../Documentation/SECURITY.md)):
one real node with its own mutual TLS 1.3 transport, listening on the port given, under a fixed published identity
(Alice's laptop) that trusts Bob's desktop, so a scanner presenting Bob's desktop's certificate gets full handshakes.
It ships nothing and is never run by `dotnet test`.

```
dotnet run --project Conformance/Axiom.Mesh.TlsScan -c Release -- --port 41810 --client-pem client.pem
```

`--client-pem` writes Bob's desktop's certificate and private key (published test keys, see
`Axiom.Mesh.Testing.TestKeys`) for the scanner's `--mtls` option. The workflow
[tls-scan.yml](../../.github/workflows/tls-scan.yml) runs testssl.sh against it and one `openssl s_client -alpn
axiom-mesh/1` handshake, and [check.py](../tls/check.py) judges both: TLS 1.3 only, forward-secret AEAD suites only, no
session tickets, no early data, ALPN `axiom-mesh/1`. The same core is proved in-process by
`Integration.Tests: TlsConformanceTests`.
