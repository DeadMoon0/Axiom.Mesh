# Axiom.Mesh.Cli

`axiom-mesh`, the command line of [Axiom.Mesh](https://github.com/DeadMoon0/Axiom.Mesh): a user client of the local
protocol, installed as a .NET tool, that explains every failure in one line and never prints a stack trace for a known
fault.

Namespace: `Axiom.Mesh.Cli`. Depends on `Axiom.Mesh.Local` only, never on the Host or its engine.

## Status

Phase 1a (milestone M10): every command of the fundamentals plan §2.7 below. `host install` and `host uninstall` come with
the release (M13); the pairing code is printed as text and, on a terminal, in a box; a QR code follows later
(STRUCTURAL-DEBT.md 11).

## Quick Start

Start the Host once (`axiom-meshd run`), then on the first device:

```bash
axiom-mesh identity create --user alice --device laptop
axiom-mesh package create Chat                      # a Channel, for messages
axiom-mesh write Chat --text "hello mesh"
axiom-mesh history Chat
```

Pair with Bob (each runs one side; both screens show the same string, compare it before you confirm):

```bash
axiom-mesh pair show                 # Alice: prints a code, waits for Bob, prints the string to compare
axiom-mesh pair join amp1:user:...   # Bob: enters Alice's code, prints the same string
axiom-mesh pair confirm              # both, once the strings match
```

Share a folder with Bob and get it on his device:

```bash
axiom-mesh package create Photos --kind archive
axiom-mesh grant set Photos <bob's user id or prefix>
axiom-mesh package publish ./photos --package Photos
# on Bob's device:
axiom-mesh package follow <the Package id or prefix>
axiom-mesh package materialise Photos ./photos
axiom-mesh watch                     # every Entry stored from now on, until Ctrl-C
```

Enrol your second device into your user: on the new device `axiom-mesh pair show --device`, on a device that holds the
user key `axiom-mesh pair join <its code>`, then `pair confirm` on both.

## Commands

| Command | What it does |
|---|---|
| `status [--json]` | Whether the Host runs, who this device is, the store and the peers. |
| `identity create --user <name> --device <name>` | A new user with this device as its first. |
| `pair show [--device]` / `pair join <code>` / `pair confirm` / `pair cancel` / `pair status` | Pair two users, or enrol a device into your user (PAIR-SPEC.md). |
| `users`, `devices [<user>]`, `peers [--discovered]`, `sessions` | Known users, a user's devices, the peer table, the established sessions; each with `--json`. |
| `dial <device\|ip:port>`, `peer add <host:port>`, `peer remove <host:port>` | Dial a device or an address once; static peers. |
| `package create <name> [--kind archive\|channel] [--retention default\|everything\|latest]` | A Package you own; a Channel by default. |
| `package list [--followed]`, `package show <package>` | Packages listed to you or followed here; one Package and its Grants; each with `--json`. |
| `package follow <package>`, `package unfollow <package>` | Replicate a Package here, or stop. |
| `package publish <dir> --package <package> [--label <branch>]` | A folder as a Version of an Archive. |
| `package materialise <package> <dir> [--existing fail\|replace\|merge]` | The latest Version into a folder. |
| `grant set <package> <user> [--write]`, `grant revoke <package> <user>` | Let a known user read (or write) a Package you own; withdraw it. |
| `write <package> --text "…"` | A message to a Channel. |
| `history <package> [--limit <n>] [--reverse] [--json]` | A Package's Entries, oldest first; a withdrawn message shows as `(withdrawn)`. |
| `entry <hash> [--json]`, `object <hash> [--out <file>]` | One Entry in full; one object's tag and size, or its bytes. |
| `watch [<package>]` | Every Entry stored from now on, until Ctrl-C. |
| `store`, `pins [--json]`, `pin <hash>`, `unpin <hash>` | The store; pins; keep or release an object. |
| `version`, `help [<command>]` | This tool's version; every command, or one command's options. |

Ids are accepted in full (64 hex digits) or as a prefix that names exactly one (the 12 digits a table shows are enough);
Packages and users also by their exact name. A prefix that names two is refused with both named.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Done. |
| 1 | The command line did not parse, or a prefix named more than one id; the usage is printed. |
| 2 | The Host is not reachable: nothing answers at its pipe (the line names it and says `axiom-meshd run`), or it closed the connection. |
| 3 | The Host refused the request: one line per status of LOCAL-SPEC.md §11.1, such as "the user key is on another device of this user, not on laptop; run this there". |
| 4 | The Host did not answer within 30 seconds, or the command was interrupted. |

## Where it looks

- **The Host:** the pipe `AXIOM_MESH_PIPE` names, else `\\.\pipe\axiom-mesh` on Windows and
  `$XDG_RUNTIME_DIR/axiom-mesh.sock` on Linux (LOCAL-SPEC.md §2.1). A Host configured with another `pipe.name` is reached
  with `AXIOM_MESH_PIPE` set (STRUCTURAL-DEBT.md 48).
- **The write path:** `write` and `package publish` connect as the external service `cli` (decision D4) with the token
  `AXIOM_MESH_SERVICE_TOKEN` names, else `services.cli.token` of the Host's `config.json`, found by the Host's own rule:
  `AXIOM_MESH_HOME`, else `%LOCALAPPDATA%\Axiom.Mesh` on Windows, else `$XDG_CONFIG_HOME/axiom-mesh` (or
  `~/.config/axiom-mesh`). The Host writes the token when it first starts.
- **Paths:** `package publish` and `package materialise` resolve a relative folder against the current directory; the
  Host reads and writes it.

## Output

Tables are aligned with two spaces between columns, ids cut to 12 hex digits, times in UTC, sizes in binary units.
`--json` prints full ids, times in milliseconds since the Unix epoch and enumerations as lower-case words, written with
`Utf8JsonWriter` (no reflection; the tool runs as Native AOT).
