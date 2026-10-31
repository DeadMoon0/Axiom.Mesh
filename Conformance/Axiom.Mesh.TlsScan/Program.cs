using System;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Axiom.Mesh.TlsScan;

/// <summary>
/// <c>Axiom.Mesh.TlsScan [--port 41810] [--address 127.0.0.1] [--client-pem path]</c>: starts the scan target and waits
/// for Ctrl-C or SIGTERM. With <c>--client-pem</c> it writes the certificate and key a scanner presents (Bob's desktop).
/// </summary>
public static class Program
{
    /// <summary>The entry point.</summary>
    /// <param name="args">The options.</param>
    /// <returns>0 once stopped, 1 for a usage error.</returns>
    public static async Task<int> Main(string[] args)
    {
        int port = 41810;
        IPAddress address = IPAddress.Loopback;
        string? clientPem = null;
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--port" when int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value is > 0 and < 65536:
                    port = value;
                    break;
                case "--address" when IPAddress.TryParse(args[i + 1], out IPAddress? value):
                    address = value;
                    break;
                case "--client-pem":
                    clientPem = args[i + 1];
                    break;
                default:
                    Console.Error.WriteLine($"Unknown or malformed option {args[i]} {args[i + 1]}. Usage: Axiom.Mesh.TlsScan [--port N] [--address A] [--client-pem path]");
                    return 1;
            }
        }

        if (args.Length % 2 != 0)
        {
            Console.Error.WriteLine("Every option takes a value. Usage: Axiom.Mesh.TlsScan [--port N] [--address A] [--client-pem path]");
            return 1;
        }

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Cancel();

        await using ScanTarget target = await ScanTarget.StartAsync(new IPEndPoint(address, port), CancellationToken.None).ConfigureAwait(false);
        if (clientPem is not null)
            await target.WriteClientPemAsync(clientPem, CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"listening on {target.Endpoint} as {target.Device}");
        try
        {
            await Task.Delay(Timeout.Infinite, stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("stopping");
        }

        return 0;
    }
}
