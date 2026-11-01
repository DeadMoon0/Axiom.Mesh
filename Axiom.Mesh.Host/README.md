# Axiom.Mesh.Host

`axiom-meshd`, the [Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh) daemon: the one process on a device that holds the keys, the store, the database and every connection. It reads its configuration, keeps its keys, and opens the pipe its clients connect to (LOCAL-SPEC.md §2).

Namespace: `Axiom.Mesh.Host`. Depends on `Axiom.Mesh.Core`, Microsoft.Extensions.Logging (console) and, on Windows, `System.Security.Cryptography.ProtectedData`.

## Status

0.2.0 (unreleased; 0.1.0 before it). `axiom-meshd run` and `axiom-meshd version`; `config.json` with a default for every key; the services it spawns and supervises (since 0.2.0, below); the key store (DPAPI on Windows, 0600 on Linux); the pipe with process identity on every connection (who may open it: [PIPE-ACCESS.md](https://github.com/DeadMoon0/Axiom.Mesh/blob/main/Documentation/PIPE-ACCESS.md)); the store and database on disk; discovery; logging. The requests it serves are those of `Axiom.Mesh.Core`'s local port, pairing included; the `cli` external service of the default configuration writes Entries and puts objects with the token the daemon writes into `config.json`. **The daemon ships as a GitHub Release asset only** (Native AOT, one zip per platform with `axiom-mesh` and `SHA256SUMS`, fetched by `install.ps1` / `install.sh`), never as a NuGet package; `PipeHost` stays public for a custom host or an installer test that builds the daemon from source.

## Running it

```text
axiom-meshd run [--home <directory>]
axiom-meshd version
```

`run` needs nothing configured: on first start it writes `config.json` with every default and a new token for the `cli` service, and prints where everything is:

```text
axiom-meshd 0.1.0 runs.
  home      C:\Users\alice\AppData\Local\Axiom.Mesh
  config    C:\Users\alice\AppData\Local\Axiom.Mesh\config.json
  pipe      \\.\pipe\axiom-mesh
  mesh      [::]:41810
  logs      C:\Users\alice\AppData\Local\Axiom.Mesh\logs
  identity  none yet: create one with axiom-mesh identity create --user <name> --device <name>
Press Ctrl-C to stop.
```

Ctrl-C or SIGTERM sends `Drain` to every local client, gives them 5 seconds, says `Bye(Shutdown)` to the rest, closes the mesh sessions and releases the store. The daemon runs in the foreground; to run it at every logon, `axiom-mesh host install` registers it (a per-user scheduled task on Windows that starts it through `conhost.exe --headless`, so no window opens; a systemd user unit on Linux), as the install scripts do, and `axiom-mesh host uninstall` stops it and removes the registration. A system service is not automated (STRUCTURAL-DEBT.md entry 10).

To run a second daemon on one machine (tests, a second identity), give it its own home with a `config.json` that names its own pipe and port (`{ "mesh": { "port": 41820 }, "pipe": { "name": "my-test" } }`) and start it with `AXIOM_MESH_HOME` pointing there; `axiom-mesh` with the same `AXIOM_MESH_HOME` reads `pipe.name` from that file and finds it. `AXIOM_MESH_PIPE` still wins over `pipe.name` for both.

## Where everything lives

| What | `--home <dir>` or `AXIOM_MESH_HOME` | Windows default | Linux default |
|---|---|---|---|
| `config.json` | `<dir>/config.json` | `%LOCALAPPDATA%\Axiom.Mesh\config.json` | `$XDG_CONFIG_HOME/axiom-mesh/config.json` (`~/.config`) |
| keys | `<dir>/keys/` | `%LOCALAPPDATA%\Axiom.Mesh\keys\` | `$XDG_DATA_HOME/axiom-mesh/keys/` (`~/.local/share`) |
| store and database | `<dir>/data/` (`objects/`, `index/`) | `%LOCALAPPDATA%\Axiom.Mesh\data\` | `$XDG_DATA_HOME/axiom-mesh/data/` |
| logs | `<dir>/logs/` | `%LOCALAPPDATA%\Axiom.Mesh\logs\` | `$XDG_STATE_HOME/axiom-mesh/logs/` (`~/.local/state`) |
| a spawned service's state | `<dir>/services/<name>/` | `%LOCALAPPDATA%\Axiom.Mesh\services\<name>\` | `$XDG_DATA_HOME/axiom-mesh/services/<name>/` |
| pipe | `AXIOM_MESH_PIPE`, else `pipe.name` | `\\.\pipe\axiom-mesh` | `$XDG_RUNTIME_DIR/axiom-mesh.sock` |

The keys are the device key and, on the device that created the identity, the user key, each PKCS#8. On Windows each file is wrapped with DPAPI for the current user; on Linux the key directory is 0700 and each file 0600, and the daemon refuses to start when either is wider. `config.json` holds the `cli` token; on Linux it is 0600, on Windows `%LOCALAPPDATA%` admits the user alone. The log is one line per message in `axiom-meshd-<date>.log`, a new file each day and past 10 MiB, the newest 10 kept. At start it logs where it runs and how a rename in the store is made durable here (`store <data>: directory flush NotNeeded` on Windows, `Flushed` where the directory was fsynced, `Unsupported` where it could not be).

## The pipe

On Windows the daemon creates every pipe instance itself (`CreateNamedPipe`), because .NET's `NamedPipeServerStream` does not set `PIPE_REJECT_REMOTE_CLIENTS`: a connection through `\\<machine>\pipe\<name>` reaches it (measured on .NET 8 and 10). Every instance rejects remote clients and has an access list that admits the daemon's own account alone; the first is created with `FILE_FLAG_FIRST_PIPE_INSTANCE`, so a daemon whose pipe is taken does not start (exit code 3). For every connection it reads the client's process id (`GetNamedPipeClientProcessId`), its executable (`QueryFullProcessImageName`) and its user from the client's impersonation token, which Windows grants only after the first bytes were read, so the daemon reads them first (within 10 seconds, at most 16 connections at once) and hands them on.

On Linux the socket file is created 0600 in `$XDG_RUNTIME_DIR`; a file left by a crashed daemon is removed only after a connection to it was refused, and one that answers means another daemon runs (exit code 3). For every connection the peer's pid and uid come from `SO_PEERCRED`, its executable from `/proc/<pid>/exe`.

A connection whose user is not the daemon's own is closed before `Hello` is answered. A daemon that runs as a system service (Windows: SYSTEM or a service account; Linux: root) admits besides the users `pipe.allowUsers` lists. The access lists, modes and the allow list are specified in [PIPE-ACCESS.md](https://github.com/DeadMoon0/Axiom.Mesh/blob/main/Documentation/PIPE-ACCESS.md).

## config.json

Every key is optional; a key the daemon does not know, or a value out of range, stops the start with a message that names it and what to do.

```json
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
      "token": "<64 hex characters, generated on first start>",
      "kinds": [1, 2],
      "entryKinds": [1, 2, 3],
      "releasePinsOnDisconnect": false
    },
    "chat": {
      "inProcess": false,
      "spawn": "axiom-mesh-chat",
      "args": [],
      "external": false,
      "token": "",
      "kinds": [2],
      "entryKinds": [2, 3],
      "releasePinsOnDisconnect": false
    },
    "sync": {
      "inProcess": false,
      "spawn": "axiom-mesh-sync",
      "args": [],
      "external": false,
      "token": "",
      "kinds": [1],
      "entryKinds": [1],
      "releasePinsOnDisconnect": false
    }
  },
  "userKeyRequests": {
    "confineTo": []
  }
}
```

| Key | Default | Meaning |
|---|---|---|
| `mesh.port` | 41810 | The TCP port of the mesh listener; 0 lets the system choose. |
| `mesh.listen` | null | The address it binds; null for every address. |
| `mesh.mdns` | true | Announce and listen over mDNS/DNS-SD (SPEC.md §3.1), once the device has an identity. |
| `mesh.broadcast` | true | Announce and listen on UDP 41811 (SPEC.md §3.2), once the device has an identity. |
| `mesh.staticPeers` | [] | `host:port` addresses dialled without discovery (SPEC.md §3.3). `AddStaticPeer` adds more, which the node keeps in its index; these configured ones are removed here, not by `RemoveStaticPeer`. |
| `store.budgetBytes` | 0 | The store's budget; 0 for none. Eviction keeps the store under it; a local write that would pin past it is `StoreFull`. |
| `pipe.name` | null | The pipe's name (Windows) or socket path (Linux); `AXIOM_MESH_PIPE` wins over it. `axiom-mesh` reads it from the same file, so its clients find the daemon without the variable. |
| `pipe.allowUsers` | [] | Users a daemon that runs as a system service admits (Windows `DOMAIN\name`, Linux the uid). |
| `services.<name>` | `cli`, `chat`, `sync` | A service by name (1 to 32 of `A–Z a–z 0–9 . - _`), with exactly one of `inProcess`, `spawn` and `external`. |
| `services.<name>.spawn` | null | The executable the daemon starts and supervises: an absolute path, or a bare file name beside `axiom-meshd` (`.exe` added on Windows). An entry whose executable is not there is noted once at start and left alone. |
| `services.<name>.args` | [] | The arguments a spawned service is started with. |
| `services.<name>.token` | generated | An external service's token, 64 lowercase hex characters; an empty one is generated and written back. Whoever holds it may claim the name. A spawned service gets a fresh token for every start, in its environment only. |
| `services.<name>.kinds` / `entryKinds` | [] | The Package kinds and Entry kinds the service may register (LOCAL-SPEC.md §9.2). |
| `services.<name>.releasePinsOnDisconnect` | false | Release the service's `Service` pins when its connection ends (LOCAL-SPEC.md §9.6); by default they stay for its restart. |
| `userKeyRequests.confineTo` | [] | Absolute paths of the executables that may send the requests that spend the user key; empty for any. Each such request is logged with its pid and executable either way. |

No in-process service is compiled in; such an entry is accepted in the file and noted in the log.

## Spawned services

For every `spawn` entry whose executable is there, the daemon starts the program once its pipe serves (LOCAL-SPEC.md
§4.2, §9.1), in the service's state directory, with these variables added to its own environment:

| Variable | Value |
|---|---|
| `AXIOM_MESH_SERVICE_TOKEN` | a fresh token of 32 random bytes for this start, 64 hex characters; it admits this child's connections until it exits, and appears in no log line or argument |
| `AXIOM_MESH_PIPE` | the pipe the daemon listens on, in full |
| `AXIOM_MESH_HOME` | the daemon's home (where `config.json` is) |
| `AXIOM_MESH_SERVICE_STATE` | the service's state directory, created for the user alone |

What the child writes to standard output and error goes into the daemon's log (`service <name>: <line>`). When it
exits, its token is withdrawn first; then it is started again after 1 s, 2 s, 4 s … 30 s (the backoff counts crashes,
exits within 10 seconds of the start; a run of a minute forgets them). Five crashes within five minutes, or exit code 2
(configuration) or 3 (refused), give the service up until the daemon restarts, with an error in the log. On Ctrl-C or
SIGTERM the daemon drains every client, the services included (a service built on `Axiom.Mesh.Service` exits on the
drain), and kills a child still running 5 seconds later with every process it started. `axiom-mesh services` lists
every configured service and whether it is connected. Services are built on the `Axiom.Mesh.Service` package.

## Exit codes

| Code | Name | When |
|---|---|---|
| 0 | `Ok` | The command did what it was asked; `run` stopped when told. |
| 1 | `Usage` | The command line names no command the daemon knows. |
| 2 | `Configuration` | `config.json`, the home or the keys cannot be used as they stand (the message names what and how to fix it). |
| 3 | `PipeTaken` | Another daemon serves the pipe. |
| 4 | `Environment` | The store, the database, the mesh port or the pipe was refused by the system. |

No failure prints a stack trace: each is one message, who must act, and the steps out.
