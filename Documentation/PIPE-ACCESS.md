# Pipe access

Who may open the Host's pipe, and how the Host decides about a connection that reached it. This is the document
[LOCAL-SPEC.md](LOCAL-SPEC.md) §2.4 defers to: §2.1 and §2.2 require the effect (only the Host's operating-system user,
or users a system service lists, may talk to the Host; the Host learns who connected from the operating system, never
from the client); this document says the form the reference Host gives it, version 0.1.0, on each desktop platform.

There are two fences, independent of each other, and a connection must pass both:

1. **The operating system's access check on the pipe itself**: an access list on Windows, the socket file's mode on Linux.
   A process the check refuses never reaches the Host.
2. **The Host's decision on the connection's process identity** (`PipeAdmission.Decide`), made for every connection
   before its `Hello` is answered: the user the operating system reports for the connecting process must be the user the
   Host serves, or, for a Host that runs as a system service, a user on its allow list. A refused connection is closed
   without a frame and logged with its process id, executable and user.

## 1. The pipe's name

| Platform | Default | Override |
|---|---|---|
| Windows | `\\.\pipe\axiom-mesh` | `AXIOM_MESH_PIPE`, a full `\\.\pipe\…` path or a bare name that gets the prefix |
| Linux | `$XDG_RUNTIME_DIR/axiom-mesh.sock`, else `/run/user/<uid>/axiom-mesh.sock` | `AXIOM_MESH_PIPE`, a socket path |
| macOS | `$TMPDIR/axiom-mesh.sock` | `AXIOM_MESH_PIPE` |

The precedence is the same for the Host and every client: `AXIOM_MESH_PIPE` when it is set and not empty, else the name
the Host's `config.json` gives (`pipe.name`), else the default. The command line reads `pipe.name` from the same
`config.json` the Host reads, found by the same home rule (`AXIOM_MESH_HOME`, else `%LOCALAPPDATA%\Axiom.Mesh` on
Windows, the XDG configuration directory on Linux). The rule is one function, `PipeLocation.Resolve` in
`Axiom.Mesh.Local`. A Host started at logon (the install scripts, `axiom-mesh host install`) does not see variables of
the shell that registered it, so a pipe of its own belongs in `pipe.name`, not in `AXIOM_MESH_PIPE`.

## 2. Windows

**The pipe.** Every instance is created with `CreateNamedPipeW` (not .NET's `NamedPipeServerStream`, which cannot set the
flags below together with an access list):

- `FILE_FLAG_FIRST_PIPE_INSTANCE` on the first instance: a second Host, or any process that created the name first,
  makes the starting Host fail with `ERROR_ACCESS_DENIED`, which it reports as exit 3 ("the pipe is taken"), so no
  process can stand in front of the Host under its name once it runs, and a Host never listens on a name another owns.
- `PIPE_REJECT_REMOTE_CLIENTS` on every instance: a connection through `\\<machine>\pipe\…` from the network is refused.
- An access list, protected (nothing inherited), owned by the Host's account: the Host's account has full control; for a
  Host that runs as a system service, each user of `pipe.allowUsers` the machine knows gets read and write; nobody else
  has an entry, so everyone else is denied by the operating system.

**The process identity.** A named pipe can impersonate its client only after the client wrote, so the listener reads the
client's first bytes (at most 10 seconds, at most 16 connections waiting), then takes the client's process id from
`GetNamedPipeClientProcessId`, its executable from `QueryFullProcessImageNameW`, and its user from the impersonation
token (`RunAsClient`, `WindowsIdentity.Name`, `DOMAIN\user`), and replays the bytes to the Host. User names compare
without case.

**When the Host runs as a system service** (LocalSystem, LocalService or NetworkService account): the allow list applies
to both fences. Version 0.1.0 does not install the Host as a service (STRUCTURAL-DEBT.md entry 10); the install scripts
and `axiom-mesh host install` register a per-user logon task, which runs with the user's own interactive token, so the
pipe belongs to that user and the allow list is unused (the Host logs a configuration note when one is set).

## 3. Linux

**The socket.** The socket's directory is created private (0700) when the Host creates it (`$XDG_RUNTIME_DIR` is 0700
already); the socket file is made 0600 right after `bind`, before `listen`, so only its owner can connect. A socket file
that exists at start is taken over only when nothing answers there (a connect is refused with `ECONNREFUSED`); a file
another live process answers on makes the Host fail with exit 3 (`Integration.Tests: PipeTests.OnLinuxTheSocketIs0600_AStaleOneIsTakenOver_ALiveOneIsNot`), a path it cannot probe or bind with exit 4.

**The process identity.** For every connection the listener reads the peer's pid, uid and gid with `SO_PEERCRED`, set by
the kernel at `connect`, and its executable from `/proc/<pid>/exe`; the user is the uid as decimal digits.

**When the Host runs as root** (uid 0, the only case the reference Host counts as a system service on Linux): the allow
list applies to the second fence, but the socket file is still 0600, owned by root, so version 0.1.0 refuses every
allow-listed user at the first fence. Serving several users from one system Host needs a group-owned 0660 socket and is
not built (STRUCTURAL-DEBT.md entry 10).

## 4. What is proved, per platform

| Fence | Windows | Linux |
|---|---|---|
| Only the Host's user passes the access check | `Integration.Tests: PipeTests.OnWindowsThePipesAccessListAllowsTheHostsAccountAlone` (the access list read back from a live pipe) | `Integration.Tests: PipeTests.OnLinuxAnotherUserIsRefusedByTheSocketsModeAndByProcessIdentity` (as user `nobody`, by the 0600 mode) |
| A remote client is refused | `Integration.Tests: PipeTests.OnWindowsAConnectionThroughTheMachinesNameIsRefused` | not applicable (a Unix socket has no remote form) |
| The decision refuses another user | `Host.Tests: PipeAdmissionTests.Decide_AnotherOsUserIsRefused_TheServedUserAdmitted_AnAllowListedUserAdmittedForASystemService` (rule) | the same rule, and end to end as `nobody` with the mode opened up, by `SO_PEERCRED` |
| The Host learns the user from the operating system | `Integration.Tests: PipeTests.TheHostLearnsTheClientsPidAndUserFromThePlatform` | the same |
| Another account end to end | the install job's step "Another Windows account is refused by the pipe" (`install-tests.yml`, not run yet; STRUCTURAL-DEBT.md entry 37) | proved, as above |

GUARANTEES.md ("The pipe") states the promises these tests hold; CONFORMANCE.md maps LOCAL-SPEC.md §2.1 and §2.2 to them.

## 5. Configuration keys

| Key | Default | Effect |
|---|---|---|
| `pipe.name` | none (the platform's default) | the pipe's name for the Host and the command line, below `AXIOM_MESH_PIPE` |
| `pipe.allowUsers` | `[]` | users a Host running as a system service admits besides its own account; ignored, with a log note, by a Host run by a user |
| `userKeyRequests.confineTo` | `[]` | executables the requests that spend the user key are confined to (LOCAL-SPEC.md §2.2); empty means any admitted process |
| `services.<name>` | `cli` as an external service with a generated token | who may claim a service name, and how it proves it (LOCAL-SPEC.md §4.2); the token lives in this file, which only the user can read |
