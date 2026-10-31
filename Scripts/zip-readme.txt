Axiom.Mesh - axiom-meshd (the Host) and axiom-mesh (the command line)
=======================================================================

Both are self-contained Native AOT executables; nothing else needs to be installed.

The install script does everything below for you, verifies this zip against SHA256SUMS and registers the Host to
start at logon:

  Windows:  irm https://raw.githubusercontent.com/DeadMoon0/Axiom.Mesh/main/install.ps1 | iex
  Linux:    curl -fsSL https://raw.githubusercontent.com/DeadMoon0/Axiom.Mesh/main/install.sh | sh

By hand:

  1. Put both executables in a directory on your PATH.
  2. Start the Host:                    axiom-meshd run
     or register it to start at logon: axiom-mesh host install
  3. Create your identity:              axiom-mesh identity create --user <your name> --device <this device>
  4. Check:                             axiom-mesh status

Pair two devices with 'axiom-mesh pair show' on one and 'axiom-mesh pair join <code>' on the other, then compare the
string both print and run 'axiom-mesh pair confirm' on both. 'axiom-mesh help' lists every command.

Documentation, the two-PC walkthrough (Documentation/ACCEPTANCE.md) and the source:
  https://github.com/DeadMoon0/Axiom.Mesh

MIT licensed; see LICENSE.
