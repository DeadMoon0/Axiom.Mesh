# Axiom Mesh Entry payloads

**Status:** Draft 1, 2026-10-31. The layouts of the Entry kinds registered in [SPEC.md](SPEC.md) §14.7, moved out of
SPEC.md §8.5 when AMP/1 draft 5 made every payload opaque to a node. Normative for the services that own these kinds
(Sync for `Version`, Chat for `Message` and `Redaction`) and for every program that writes or reads them, such as the
command line. A node never parses a payload; nothing here changes what a node stores, replicates, orders or serves.

## Table of contents

1. Introduction
2. `Version` (kind 1)
3. `Message` (kind 2)
4. `Redaction` (kind 3)

## 1. Introduction

### 1.1 What a node reads, and what a service reads

An Entry (SPEC.md §8.6) carries a `kind`, a `payload` blob and two envelope fields a node acts on: `references`, the
objects the Entry needs, and `redacts`, the Entry it withdraws. A node routes, pins, wants, serves and redacts by the
envelope alone; the payload is opaque to it (SPEC.md §8.5). A service parses the payloads of its own kinds and shows or
hides what they say in its own namespace; it cannot keep a node from storing or forwarding an Entry whose payload it
finds wrong.

### 1.2 Filling the envelope

A writer of a kind of this document MUST put into the Entry's envelope what its payload names, so that every node keeps
and serves what a reader of the payload will need:

- `references`: every hash the payload names as an object to fetch (a `Version`'s `tree`, every `Message` attachment's
  `hash`), each once, ascending as byte strings, at most 64 (SPEC.md §8.6);
- `redacts`: for a `Redaction`, its `target`; all zero for the other kinds.

A reader SHOULD treat an Entry whose envelope does not name what its payload names as the writer's fault and show it as
broken; the node has already stored and forwarded it, which is correct.

### 1.3 Encoding and parsing

The types are those of SPEC.md §5.2, in the canonical form of SPEC.md §5.3. A payload of a kind with a layout here
MUST parse exactly to its end, every field within its bounds; a service treats one that does not as its own fault and
does not show it. The bounds are named constants of `Axiom.Mesh.Limits` (LIMITS.md, "Payloads of the registered Entry
kinds"); `Axiom.Mesh.Payloads` is the reference encoder and parser.

## 2. `Version` (kind 1)

A version of an Archive's files (SPEC.md §9.5).

```
Version (kind 1):
  tree:         hash
  label:        string(64)       the branch name, "main" by default; MUST NOT be empty
  prerelease:   string(64)       empty, or a pre-release tag such as "beta.2"
  notes:        string(4096)
```

`references` holds `tree`. The service that parses Versions keeps the branches, leaves and merges of SPEC.md §9.5.

## 3. `Message` (kind 2)

A message in a Channel (SPEC.md §9.4).

```
Message (kind 2):
  text:         string(16384)
  attachments:  list<Attachment>(64)
  replyTo:      hash             all zero when none
  Attachment:
    name:       string(255)
    hash:       hash             a Tree or ChunkList
    size:       u64              a copy, as in SPEC.md §8.4
```

`references` holds every attachment's `hash`. `replyTo` names an Entry, not an object to fetch, and is not referenced.

## 4. `Redaction` (kind 3)

The withdrawal of an earlier Entry of the same Channel (SPEC.md §9.4).

```
Redaction (kind 3):
  target:       hash             the entry withdrawn
```

`redacts` holds `target`, and the node acts on `redacts`, never on `target`: an Entry of any kind whose `redacts` is not
zero is a redaction (SPEC.md §8.6, §9.4). `target` stays in the payload for services that want it there; a reader that
finds `target` and `redacts` differ treats the Entry as broken. `references` is empty.
