# Architecture: the rules Axiom.Mesh is built by

> Read with [ARCHITECTURE-MAP.md](ARCHITECTURE-MAP.md), which names what carries each rule in the code today, and
> [STRUCTURAL-DEBT.md](../STRUCTURAL-DEBT.md), which names where the code does not follow them yet.
>
> **This file names no code.** It states rules and the reasoning behind them; it does not name a type, a method, a
> file or a test. A rule is durable, the name of the class enforcing it is not. It may name the concepts the rules are
> about (a node, a peer, a Package, an Entry, a roster, the pipe, a service) and things outside Mesh (TLS, ProtoStream,
> LWSE, LDB, the specifications).
>
> **This file changes only on the maintainer's explicit instruction.** It is never edited as a side effect of other
> work and never bent to match code that drifted: code moves toward this document, or the gap is recorded as debt.
>
> **Every look is an inspection.** Whoever reads or touches any part of Mesh checks it against these rules in
> passing. A breach spotted is fixed or recorded; it is never silently stepped over.

## 1. Honest layers under one dishonest runtime

What decides is honest: given the same bytes, keys and timestamps it gives the same answer, with no socket, disk,
clock or randomness of its own. The model of objects and Entries, the descriptions of both protocols, the discovery
codecs and every rule a session or the store applies are honest. One runtime at the top, the Host, holds everything
dishonest and hands it down as values. Dishonesty is pushed up, never sideways.

## 2. Dependencies point downwards; apps and services reference nothing of the engine or the daemon

Each layer references only the layers below it, along one stated graph. The engine sits on the model, both protocols
and discovery; the daemon sits on the engine. Apps and services are clients of the local protocol and the model, and
reference nothing of the engine or the daemon, directly or through another package. A dependency belongs to the
layer that needs it.

## 3. The Host is the only process that holds a key, a connection, the store or the database

Every other program on a device, the app and the command line included, is a client of the Host over the pipe. No
client holds a private key, opens a peer connection, writes the store or touches the database. A private key never
crosses the pipe; signing happens in the Host.

## 4. Nothing a peer says is believed without a hash, a signature or a roster

An object is named by its hash and checked against it before anything reads it; a signed object is checked against
the key its specification names; a peer is admitted only when its key is in a current roster. Nothing unverified is
stored, served, listed or forwarded. What a peer does wrong is a violation or faulty content, never an exception in
the caller's code.

## 5. Every limit is a named constant with the specification's value, enforced where the bytes enter

A bound is written once, with the value the specification states, and checked at the edge where the data arrives:
from the length field before the content, at the frame before the message, at the pipe before the request. A limit
that exists only in a comment, or is checked deep inside after the bytes were accepted, is not a limit.

## 6. Every wait has a deadline and every queue a bound; the Host never blocks on a client or a peer

No read, handshake, request or subscription waits without a deadline. Every queue toward a peer or a client is
bounded in count and in bytes, and what happens when it is full is stated: back-pressure, a dropped hint, a lapsed
subscription, a closed connection. A slow client loses its subscription, never the mesh.

## 7. A failure says who must act and whether a retry can succeed

Every failure a caller sees names who has to change something (the caller, the configuration, the environment),
whether the same call can succeed when retried unchanged, and what to do next. Something that might not be there is
a question with an answer, not a failure. The command line explains every known failure in one line and never shows
a stack trace for one.

## 8. Every rule that decides is a pure function tested from the specification; time, sockets, disk and the pipe are injected edges

A rule's tests take their expected values from the section of the specification it implements, not from running the
code. The edges that touch the world are thin and replaceable, so the same node runs on sockets and disk or entirely
in memory, under a clock the test controls.

## 9. A promise only with the test that proves it, a number only with the command that measured it

A guarantee, a conformance claim or a security property is written down next to the test that proves it, and is not
made before that test exists. A performance figure is written down with the command and the machine that measured it.
A test that is removed takes its promise with it.

## 10. The public surface grows without breaking a compiled consumer; every library runs trimmed and ahead of time

A minor version adds; it does not change the shape of what a compiled consumer calls. Every shipped library is
trimmable and safe for native ahead-of-time compilation, on every platform the design names, with no platform-only
API below the daemon.

## 11. Opt-outs are named options logged at start

Where a safe default is relaxed (a discovery method switched off, an allowed executable list left open, a budget
raised), the relaxation is a named option, never an absence, and the effective configuration is logged once when the
Host starts.
