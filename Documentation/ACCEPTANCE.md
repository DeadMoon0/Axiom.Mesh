# Acceptance: two PCs

The run that decides whether phase 1a is done (DESIGN.md §17; fundamentals plan §8 item 5): two PCs on one LAN pair,
one creates a Package and writes an Entry through the command line (a text message and a published folder), the other
follows it and holds the same Entry hashes, and this survives one PC being offline. A third identity is never let in.

Fill in the run log at the end as you go, then commit it (`Documentation/acceptance-runs/<date>.md`, or append it here).

## Before you start

- Two desktop PCs on the same LAN, Windows or Linux, in any mix: **A** (Alice) and **B** (Bob). Write down each one's
  LAN address (`ipconfig` / `ip -4 addr`).
- The firewall lets TCP 41810 in (the mesh port), and UDP 5353 (mDNS) and UDP 41811 (broadcast) if you want discovery.
  On Windows the first start of `axiom-meshd` may show the firewall prompt: allow private networks.
- A folder of a few files on A to publish, for example `photos` with two pictures and a text file in a subfolder.
- For step 10, a second home on B for Eve (no third PC needed).

Commands are the same on both platforms; where a path differs, both are given.

## Step 1: install on both PCs

From a release:

```text
Windows:  irm https://raw.githubusercontent.com/DeadMoon0/Axiom.Mesh/main/install.ps1 | iex
Linux:    curl -fsSL https://raw.githubusercontent.com/DeadMoon0/Axiom.Mesh/main/install.sh | sh
```

Before a release exists, from a zip you built (`Scripts/package.ps1 -Output dist` on Windows,
`sh Scripts/package.sh linux-x64 dist` on Linux, which needs clang, zlib1g-dev and zip) or downloaded from the
artifacts `axiom-mesh-win-x64` / `axiom-mesh-linux-x64` of a green run of the Install workflow, unpacked so the zip and
its `SHA256SUMS` sit side by side:

```text
Windows:  .\install.ps1 -Source C:\path\to\axiom-mesh-0.1.0-win-x64.zip
Linux:    sh install.sh --source /path/to/axiom-mesh-0.1.0-linux-x64.zip
```

**Expected:** `verified axiom-mesh-…zip against SHA256SUMS`, `installed axiom-meshd and axiom-mesh to …`,
`axiom-meshd starts at logon: scheduled task \Axiom.Mesh\axiom-meshd` (Windows) or `systemd user unit …` (Linux), and a
status block with `Identity  none`. Open a new terminal so the PATH change applies.

**Pass:** `axiom-mesh status` answers on both PCs (exit 0).

If either PC ran a build from before 0.1.0, the Host refuses its old store at start (`axiom-mesh status` says the Host is
not reachable; the Host's log or `axiom-meshd run` in a terminal shows "written by a pre-release build"): stop the Host
(`axiom-mesh host uninstall`), delete the old `data` and `keys` directories it names, and run `axiom-mesh host install`.

## Step 2: an identity on each PC

```text
A:  axiom-mesh identity create --user alice --device pc-a
B:  axiom-mesh identity create --user bob --device pc-b
```

**Expected:** `Created  user alice, device pc-a`, a user id, a device id, `Roster   seq 1` (and the same for Bob).

**Pass:** `axiom-mesh status` shows `Identity` with the name on each PC.

## Step 3: pair the two users

```text
A:  axiom-mesh pair show
B:  axiom-mesh pair join <the code A prints>
```

**Expected:** A prints a `Code` (also boxed) and waits; once B joins, both print `Compare  XXXX-XXXX`, the same eight
characters on both screens.

```text
A:  axiom-mesh pair confirm
B:  axiom-mesh pair confirm
```

**Expected:** both end with `paired`. Within a few seconds:

```text
A:  axiom-mesh users          (lists alice and bob)
A:  axiom-mesh peers          (pc-b with STATE established)
B:  axiom-mesh peers          (pc-a with STATE established)
```

If `peers` stays without `established` after 30 seconds (multicast or broadcast blocked on the LAN), add the other PC by
address on one side: `axiom-mesh peer add <ip of the other PC>:41810`.

**Pass:** both screens showed the same comparison string; `peers` shows the other PC `established` on both. Note
whether discovery found it (mDNS or broadcast) or you needed `peer add`: that answers the manual mDNS check
(STRUCTURAL-DEBT.md entry 2).

## Step 4: a Channel and an Archive on A, granted to Bob

```text
A:  axiom-mesh package create Family
A:  axiom-mesh package create Photos --kind archive
A:  axiom-mesh grant set Family bob
A:  axiom-mesh grant set Photos bob
```

**Expected:** `Created  channel Family` and `Created  archive Photos` with their Package ids; `user … may read package …
(grant seq 1)` twice.

## Step 5: write a message and publish a folder on A

```text
A:  axiom-mesh write Family --text "hello from A"
A:  axiom-mesh package publish ./photos --package Photos
A:  axiom-mesh history Family
A:  axiom-mesh history Photos
```

**Expected:** `written: entry <hash> (clock 0)`; the publish reports the Version's Entry and the root Tree; each history
shows one Entry. Write down both Entry hashes (the first 12 hex digits are enough).

## Step 6: follow on B and compare

```text
B:  axiom-mesh package list
B:  axiom-mesh package follow Family
B:  axiom-mesh package follow Photos
B:  axiom-mesh history Family
B:  axiom-mesh history Photos
```

**Expected:** `package list` shows Family and Photos as `listed` before the follow; after it, each `history` shows the
same Entry hash as on A, the message's text `hello from A`.

```text
B:  axiom-mesh package materialise Photos <an empty folder, absolute or relative>
```

**Expected:** the folder holds the same files as A's `photos`. Compare them, for example with
`Get-FileHash -Recurse` on Windows or `sha256sum` on Linux on both sides.

**Pass:** the same Entry hashes on both PCs; the materialised files are byte-equal.

## Step 7: B offline, A writes, B catches up

```text
B:  axiom-mesh host uninstall          (stops B's Host; or shut B down)
A:  axiom-mesh write Family --text "written while B was away"
B:  axiom-mesh host install            (starts B's Host again; or boot B and log on)
B:  axiom-mesh watch Family            (Ctrl-C once the Entry shows)
```

**Expected:** within 60 seconds of B's Host starting, `watch` (or `history Family`) shows the Entry written while B was
away, with A's hash.

**Pass:** B holds the new Entry within 60 s, without anyone touching A. Repeat once with A offline and B writing after
step 8 if you want both directions.

## Step 8: the reverse direction, with a write grant

```text
A:  axiom-mesh grant set Family bob --write
B:  axiom-mesh write Family --text "hello from B"
A:  axiom-mesh history Family
```

**Expected:** `user … may read and write package …`; B prints its Entry hash; A's history shows it with that hash.

**Pass:** an Entry written on B reaches A with the same hash.

## Step 9: revoke

```text
A:  axiom-mesh grant revoke Family bob
B:  axiom-mesh package show Family
B:  axiom-mesh history Family
A:  axiom-mesh write Family --text "after the revocation"
B:  axiom-mesh history Family
```

**Expected:** `user … no longer has package …`; on B, `package show Family` reports rights none and no peer listing it
(A's listing omits it now; B keeps following what it holds), `history Family` still shows what B held before, and the
Entry A writes after the revocation never reaches B.

**Pass:** Bob is no longer served Family; what B held stays.

## Step 10: Eve is never let in

On B, start a second Host for Eve with a home, pipe and port of its own:

```text
Windows (PowerShell):
  $env:AXIOM_MESH_HOME = "$env:TEMP\eve"; mkdir $env:AXIOM_MESH_HOME
  Set-Content "$env:AXIOM_MESH_HOME\config.json" '{ "mesh": { "port": 41820 }, "pipe": { "name": "axiom-mesh-eve" } }'
  Start-Process axiom-meshd -ArgumentList run
Linux:
  export AXIOM_MESH_HOME=/tmp/eve; mkdir -p -m 700 $AXIOM_MESH_HOME
  echo '{ "mesh": { "port": 41820 }, "pipe": { "name": "/tmp/eve/pipe.sock" } }' > $AXIOM_MESH_HOME/config.json
  axiom-meshd run &

Then, in the same terminal:
  axiom-mesh identity create --user eve --device eve-on-b
  axiom-mesh peer add <ip of A>:41810
  axiom-mesh peers
```

**Expected:** Eve's `peers` shows A refused or backed off, never `established`; on A, `axiom-mesh peers` never lists
Eve's device as `established` (it may not list it at all: a key in no roster is closed during the handshake).

**Pass:** Eve never becomes Established on A. Stop Eve's Host afterwards (Ctrl-C, or `Stop-Process -Name axiom-meshd`
for the one started with `AXIOM_MESH_HOME` set, `kill %1` on Linux) and delete its home.

## Step 11: chat through the chat service

Both PCs run `axiom-mesh-chat` beside the Host (the zip ships it; the Host starts it). On A, with the Channel `Family`
of step 4 and Bob's write grant of step 8:

```text
axiom-mesh services                      # chat: spawned, connected
axiom-mesh chat send Family --text "holiday" --file <a photo or a folder>
```

On B:

```text
axiom-mesh chat show Family
axiom-mesh chat get <entry from the first column> <attachment name> --out <a new path>
axiom-mesh chat send Family --text "lovely" --reply <entry>
axiom-mesh chat show Family --json > b.json
```

On A: `axiom-mesh chat show Family --json > a.json`, then `axiom-mesh chat withdraw <B's reply>` (A owns the Channel).
On B: `axiom-mesh chat withdraw <A's message>` is refused.

**Expected:** both `chat show` list the same entries in the same order; the fetched attachment equals the original byte
for byte; `a.json` and `b.json` are equal before the withdrawal; after it both show B's reply as `(withdrawn)` and never
its text; B's attempt says only the device that wrote the message or a device of the owner may withdraw it.

**Then** end `axiom-mesh-chat` on A from the Task Manager (`Stop-Process -Name axiom-mesh-chat`, `pkill axiom-mesh-chat`):
within a few seconds `axiom-mesh services` shows it connected again and `axiom-mesh chat show Family` works.

**Pass:** same order on both, withdrawn on both, byte-equal attachment, the service restarted by the Host.

## Cleaning up

`axiom-mesh host uninstall` stops the Host and removes its logon start; `install.ps1 -Uninstall` / `install.sh
--uninstall` also removes the executables and the PATH entry. The Host's home (keys, store, `config.json`) stays until
you delete it.

## Run log

Copy this, fill it in, commit it.

```text
Date:
Axiom.Mesh version (axiom-mesh version / axiom-meshd version):
PC A: OS and version, LAN address, install source (release / local zip):
PC B: OS and version, LAN address, install source:

Step 1  install                      pass / fail   notes:
Step 2  identities                   pass / fail   notes:
Step 3  pairing, same string         pass / fail   string:            discovery found the peer: mDNS / broadcast / peer add
Step 4  packages and grants          pass / fail   notes:
Step 5  write and publish on A       pass / fail   message entry:            version entry:
Step 6  follow on B, same hashes     pass / fail   byte-equal files: yes / no
Step 7  offline catch-up             pass / fail   seconds until B held the Entry:
Step 8  write grant, B to A          pass / fail   entry:
Step 9  revoke                       pass / fail   notes:
Step 10 Eve never established        pass / fail   what Eve's peers showed:
Step 11 chat (0.2.0)                 pass / fail   same order: yes / no   attachment byte-equal: yes / no   restarted: yes / no

Anything unexpected (copy the exact line):
Verdict: phase 1a accepted / not accepted; chat (phase 2, step 11) accepted / not accepted
```
