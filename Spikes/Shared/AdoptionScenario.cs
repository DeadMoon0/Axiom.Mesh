using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Axiom.LWSE.Admission;

namespace Axiom.Mesh.Spikes;

/// <summary>
/// Open question 1 of the design: can a connection this node dialled be served by LWSE Core with admission, TLS,
/// observers and drain, like an accepted one? Three nodes on loopback, each with pinned mutual TLS, prove or refute it.
/// </summary>
public static class AdoptionScenario
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    public static async Task<bool> RunAsync(int basePort, SpikeLog log, CancellationToken cancellationToken)
    {
        using X509Certificate2 certA = Certificates.CreateDeviceCertificate("A");
        using X509Certificate2 certB = Certificates.CreateDeviceCertificate("B");
        using X509Certificate2 certEve = Certificates.CreateDeviceCertificate("Eve");

        // A and B pin each other. Eve pins both, but nobody pins Eve.
        await using SpikeNode a = await SpikeNode.StartAsync("A", basePort, certA, Certificates.Pins(certB), new AdmissionOptions { PerClient = 1 }, log, cancellationToken);
        await using SpikeNode b = await SpikeNode.StartAsync("B", basePort + 1, certB, Certificates.Pins(certA), AdmissionOptions.Default, log, cancellationToken);
        await using SpikeNode eve = await SpikeNode.StartAsync("Eve", basePort + 2, certEve, Certificates.Pins(certA, certB), AdmissionOptions.Default, log, cancellationToken);

        var checks = new List<(string What, bool Passed)>();

        log.Add("--- 1. A dials B: both sides serve the connection through Core, TLS as client on A and as server on B");
        await a.DialAsync(b.Inbound, cancellationToken);
        checks.Add(("A's adopted connection was accepted, opened a session and got B's hello over TLS 1.3",
            await log.WaitForAsync(Wait, "[A/out] accepted #", "[A/out] session opened", "[A/out] hello from B", "Tls13")));
        checks.Add(("B accepted A's connection and got A's hello",
            await log.WaitForAsync(Wait, "[B/in] accepted #", "[B/in] hello from A")));

        log.Add("--- 2. A dials B again with PerClient = 1: admission must refuse the adopted connection");
        await a.DialAsync(b.Inbound, cancellationToken);
        checks.Add(("admission refused the second adopted connection",
            await log.WaitForAsync(Wait, "[A/out] rejected", "ClientLimit")));

        // TLS 1.3 lets the client finish its handshake before the server has judged the client certificate, so the
        // dialling side learns of a refusal only as a closed connection after its session opened. The design must
        // treat "session open" as "peer accepted me" only once the peer's own Hello arrived.
        log.Add("--- 3. Eve dials B: B does not pin Eve, so B must refuse in its TLS layer; Eve sees a close and never a hello from B");
        await eve.DialAsync(b.Inbound, cancellationToken);
        checks.Add(("B refused Eve in the TLS layer", await log.WaitForAsync(Wait, "[B/in] transport pinned-tls failed")));
        checks.Add(("Eve's adopted side was closed without a hello from B",
            await log.WaitForAsync(Wait, "[Eve/out] closed #") && !log.Contains("[Eve/out] hello from")));
        checks.Add(("B never opened a session for Eve", !log.Contains("hello from Eve")));

        log.Add("--- 4. A stops: drain must retire the adopted connection, and B must see it close");
        await a.StopAsync(cancellationToken);
        checks.Add(("drain retired and closed A's adopted connection",
            await log.WaitForAsync(Wait, "[A/out] retired #", "Draining", "[A/out] closed #")));
        checks.Add(("B saw the connection close", await log.WaitForAsync(Wait, "[B/in] closed #")));

        log.Add("--- result");
        bool all = true;
        foreach ((string what, bool passed) in checks)
        {
            all &= passed;
            log.Add($"{(passed ? "PASS" : "FAIL")}  {what}");
        }

        return all;
    }
}
