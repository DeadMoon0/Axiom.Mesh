using System;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Axiom.Mesh.Host.Tests.Regression;

/// <summary>What the daemon writes today, pinned: the default <c>config.json</c> and the format of a log line.</summary>
public sealed class HostRegressionTests
{
    private const string DefaultConfig = """
        {
          "mesh": {
            "port": 41810,
            "listen": null,
            "mdns": true,
            "broadcast": true,
            "staticPeers": []
          },
          "store": {
            "budgetBytes": 0
          },
          "pipe": {
            "name": null,
            "allowUsers": []
          },
          "services": {
            "cli": {
              "inProcess": false,
              "spawn": null,
              "args": [],
              "external": true,
              "token": "<token>",
              "kinds": [
                1,
                2
              ],
              "entryKinds": [
                1,
                2,
                3
              ],
              "releasePinsOnDisconnect": false
            },
            "chat": {
              "inProcess": false,
              "spawn": "axiom-mesh-chat",
              "args": [],
              "external": false,
              "token": "",
              "kinds": [
                2
              ],
              "entryKinds": [
                2,
                3
              ],
              "releasePinsOnDisconnect": false
            },
            "sync": {
              "inProcess": false,
              "spawn": "axiom-mesh-sync",
              "args": [],
              "external": false,
              "token": "",
              "kinds": [
                1
              ],
              "entryKinds": [
                1
              ],
              "releasePinsOnDisconnect": false
            }
          },
          "userKeyRequests": {
            "confineTo": []
          }
        }
        """;

    // Damage: the file a user edits loses a key, renames one, or stops showing a default, so the README no longer matches it.
    [Fact]
    public void TheDefaultConfigurationIsWrittenWithEveryKeyAndItsDefault()
    {
        (HostOptions options, _) = HostConfiguration.WithTokens(new HostOptions(), new Axiom.Mesh.Core.DeterministicEntropy(1));
        string written = HostConfiguration.Write(options);

        string masked = Regex.Replace(written, "\"[0-9a-f]{64}\"", "\"<token>\"");
        Assert.Equal(DefaultConfig.ReplaceLineEndings(), masked.ReplaceLineEndings());
    }

    // Damage: a line of the daemon's log file loses its time, its level, its category or the exception's first line.
    [Fact]
    public void ALogLineIsUtcTimeLevelCategoryMessageAndTheExceptionsFirstLine()
    {
        var at = new DateTimeOffset(2026, 10, 29, 12, 34, 56, 789, TimeSpan.FromHours(2));

        string line = RollingFileLoggerProvider.Line(at, LogLevel.Warning, "Axiom.Mesh.Local", "user-key request\nSetGrant", new InvalidOperationException("broken\nthere"));

        Assert.Equal("2026-10-29T10:34:56.789Z warn Axiom.Mesh.Local: user-key request SetGrant | InvalidOperationException: broken there", line);
    }
}
