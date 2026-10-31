#!/usr/bin/env python3
"""Judges a TLS scan of an Axiom.Mesh node against SPEC.md section 4.2.

The node must offer TLS 1.3 and nothing older, only AEAD cipher suites with forward secrecy, no session tickets, no
early data (0-RTT), and it must negotiate the ALPN "axiom-mesh/1". The scan is testssl.sh's JSON output (--jsonfile,
a flat list, or --jsonfile-pretty) plus the transcript of one `openssl s_client -alpn axiom-mesh/1` handshake, because
testssl.sh offers only its own list of ALPN names, which the node refuses.

Usage:
    python3 -I check.py --testssl testssl.json --alpn alpn.txt
    python3 -I check.py --self-test

Exit status: 0 when every rule holds, 1 when one does not (each failure printed on its own line), 2 for a usage error
or an unreadable scan. Standard library only; run with -I so nothing from the working directory is imported.
"""

import argparse
import json
import re
import sys

ALPN = "axiom-mesh/1"

# SPEC.md section 4.2: forward-secret AEAD suites. Every TLS 1.3 suite is forward secret; CCM_8 has a truncated tag and is
# refused like anything that is not listed here.
ALLOWED_SUITES = {
    "TLS_AES_128_GCM_SHA256",
    "TLS_AES_256_GCM_SHA384",
    "TLS_CHACHA20_POLY1305_SHA256",
    "TLS_AES_128_CCM_SHA256",
}

OLDER_PROTOCOLS = ("SSLv2", "SSLv3", "TLS1", "TLS1_1", "TLS1_2")

# testssl.sh cipher categories that must all be absent.
FORBIDDEN_CATEGORIES = (
    "cipherlist_NULL",
    "cipherlist_aNULL",
    "cipherlist_EXPORT",
    "cipherlist_LOW",
    "cipherlist_3DES_IDEA",
    "cipherlist_OBSOLETED",
    "cipherlist_STRONG_NOFS",
)

SUITE_NAME = re.compile(r"\bTLS_[A-Z0-9_]+\b")
LIFETIME = re.compile(r"valid for|\b\d+\s*seconds?\b", re.IGNORECASE)


def findings_of(document):
    """The scan's findings as a list of dicts with at least 'id' and 'finding', from either JSON layout."""
    if isinstance(document, list):
        return [f for f in document if isinstance(f, dict) and "id" in f]
    if isinstance(document, dict):
        found = []
        for result in document.get("scanResult", []):
            for value in result.values():
                if isinstance(value, list):
                    found.extend(f for f in value if isinstance(f, dict) and "id" in f)
        return found
    return []


def text(finding):
    return str(finding.get("finding", "")).strip()


def denies(value):
    """Whether a finding says something is absent: 'not offered', 'not supported', 'no ...', 'none'."""
    lowered = value.lower()
    return (
        "not offered" in lowered
        or "not supported" in lowered
        or lowered.startswith("no ")
        or lowered.startswith("no--")
        or lowered.startswith("no -")
        or lowered in ("no", "none", "--", "-", "(none)")
    )


def affirms(value):
    """Whether a finding says something is present."""
    lowered = value.lower()
    return not denies(value) and ("offered" in lowered or "supported" in lowered or lowered.startswith("yes"))


def judge(findings, alpn_transcript):
    """Every rule broken, as one line each; an empty list means the node passed."""
    failures = []
    by_id = {}
    for finding in findings:
        by_id.setdefault(str(finding["id"]).lower(), []).append(finding)

    if not findings:
        failures.append("The scan reported no findings at all; it did not reach the node.")
        return failures

    for finding in findings:
        if str(finding.get("severity", "")).upper() == "FATAL" or str(finding["id"]).lower() == "scanproblem":
            failures.append("The scan reports a problem: %s: %s" % (finding["id"], text(finding)))

    # Protocols: TLS 1.3 and nothing older.
    for protocol in OLDER_PROTOCOLS:
        for finding in by_id.get(protocol.lower(), []):
            if not denies(text(finding)):
                failures.append("%s must not be offered (SPEC.md 4.2); the scan says: %s" % (protocol, text(finding)))
    tls13 = by_id.get("tls1_3", [])
    if not tls13:
        failures.append("The scan did not report on TLS 1.3.")
    for finding in tls13:
        if not affirms(text(finding)):
            failures.append("TLS 1.3 must be offered (SPEC.md 4.2); the scan says: %s" % text(finding))

    # Cipher suites: only forward-secret AEAD suites.
    offered = set()
    for finding in findings:
        identifier = str(finding["id"]).lower()
        if not (identifier.startswith("cipher-") or identifier.startswith("cipher_x") or identifier.startswith("cipher-tls")):
            continue
        value = text(finding)
        if "not offered" in value.lower():
            continue
        names = SUITE_NAME.findall(value)
        if not names:
            failures.append("A cipher the scan lists has no suite name to judge: %s: %s" % (finding["id"], value))
        offered.update(names)
    for suite in sorted(offered - ALLOWED_SUITES):
        failures.append("Cipher suite %s is not a forward-secret AEAD suite of TLS 1.3 (SPEC.md 4.2)." % suite)
    if not offered & ALLOWED_SUITES:
        failures.append("The scan lists no offered cipher suite; TLS_AES_128_GCM_SHA256 is required (SPEC.md 4.2).")
    for category in FORBIDDEN_CATEGORIES:
        for finding in by_id.get(category.lower(), []):
            if not denies(text(finding)):
                failures.append("%s must not be offered (SPEC.md 4.2); the scan says: %s" % (category, text(finding)))

    # Session tickets and resumption: every connection runs a full handshake.
    for finding in by_id.get("tls_session_ticket", []):
        if LIFETIME.search(text(finding)):
            failures.append("A session ticket was issued (SPEC.md 4.2 forbids them): %s" % text(finding))
    for identifier in ("sessionresumption_ticket", "sessionresumption_id"):
        for finding in by_id.get(identifier, []):
            if affirms(text(finding)):
                failures.append("Session resumption must not work (SPEC.md 4.2): %s: %s" % (finding["id"], text(finding)))

    # Early data: none. Without tickets there is no PSK to send it under; a finding that says otherwise fails.
    for finding in findings:
        identifier = str(finding["id"]).lower()
        if ("early_data" in identifier or "0rtt" in identifier or "0-rtt" in identifier) and affirms(text(finding)):
            failures.append("Early data (0-RTT) must not be accepted (SPEC.md 4.2): %s: %s" % (finding["id"], text(finding)))

    # ALPN: axiom-mesh/1 negotiated, nothing foreign served.
    for finding in by_id.get("alpn", []):
        value = text(finding)
        if value and not denies(value) and ALPN not in value:
            failures.append("The node serves an ALPN other than %s: %s" % (ALPN, value))
    if alpn_transcript is None:
        failures.append("No ALPN transcript was given; the ALPN %s is unproven." % ALPN)
    else:
        if not re.search(r"^ALPN protocol:\s*" + re.escape(ALPN) + r"\s*$", alpn_transcript, re.MULTILINE):
            failures.append("The handshake that offered %s did not negotiate it (no 'ALPN protocol: %s' line)." % (ALPN, ALPN))
        if not re.search(r"TLSv1\.3", alpn_transcript):
            failures.append("The handshake that offered %s did not run TLS 1.3." % ALPN)
    return failures


def self_test():
    """Runs the rules against made-up scans with known verdicts; returns the number of mismatches."""
    good_alpn = "CONNECTED(00000003)\nNew, TLSv1.3, Cipher is TLS_AES_256_GCM_SHA384\nProtocol: TLSv1.3\nALPN protocol: axiom-mesh/1\n"
    passing = [
        {"id": "SSLv2", "severity": "OK", "finding": "not offered"},
        {"id": "SSLv3", "severity": "OK", "finding": "not offered"},
        {"id": "TLS1", "severity": "INFO", "finding": "not offered"},
        {"id": "TLS1_1", "severity": "INFO", "finding": "not offered"},
        {"id": "TLS1_2", "severity": "INFO", "finding": "not offered"},
        {"id": "TLS1_3", "severity": "OK", "finding": "offered with final"},
        {"id": "cipherlist_NULL", "severity": "OK", "finding": "not offered"},
        {"id": "cipherlist_OBSOLETED", "severity": "OK", "finding": "not offered"},
        {"id": "cipherlist_STRONG_NOFS", "severity": "OK", "finding": "not offered"},
        {"id": "cipherlist_STRONG_FS", "severity": "OK", "finding": "offered"},
        {"id": "cipher-tls1_3_x1302", "severity": "OK", "finding": "TLS 1.3  x1302  TLS_AES_256_GCM_SHA384  ECDH 253  AESGCM  256"},
        {"id": "cipher-tls1_3_x1301", "severity": "OK", "finding": "TLS 1.3  x1301  TLS_AES_128_GCM_SHA256  ECDH 253  AESGCM  128"},
        {"id": "TLS_session_ticket", "severity": "INFO", "finding": "no -- no lifetime advertised"},
        {"id": "sessionresumption_ticket", "severity": "INFO", "finding": "not supported"},
        {"id": "sessionresumption_ID", "severity": "INFO", "finding": "not supported"},
        {"id": "ALPN", "severity": "INFO", "finding": "not offered"},
    ]

    def replaced(identifier, finding):
        return [dict(f, finding=finding) if f["id"] == identifier else f for f in passing]

    cases = [
        ("a conforming node", passing, good_alpn, True),
        ("TLS 1.2 offered", replaced("TLS1_2", "offered"), good_alpn, False),
        ("TLS 1.3 not offered", replaced("TLS1_3", "not offered"), good_alpn, False),
        ("a CBC suite", passing + [{"id": "cipher-tls1_2_xc013", "finding": "TLS 1.2  xc013  TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA  ECDH 256  AES  128"}], good_alpn, False),
        ("a truncated-tag suite", passing + [{"id": "cipher-tls1_3_x1305", "finding": "TLS 1.3  x1305  TLS_AES_128_CCM_8_SHA256  ECDH 253  AESCCM8  128"}], good_alpn, False),
        ("no suite at all", [f for f in passing if not f["id"].startswith("cipher-")], good_alpn, False),
        ("a session ticket", replaced("TLS_session_ticket", "valid for 7200 seconds only (<daily)"), good_alpn, False),
        ("ticket resumption", replaced("sessionresumption_ticket", "supported"), good_alpn, False),
        ("early data", passing + [{"id": "early_data", "finding": "supported"}], good_alpn, False),
        ("a foreign ALPN", replaced("ALPN", "h2, http/1.1"), good_alpn, False),
        ("no ALPN negotiated", passing, "New, TLSv1.3, Cipher is TLS_AES_256_GCM_SHA384\nNo ALPN negotiated\n", False),
        ("no ALPN transcript", passing, None, False),
        ("a broken scan", passing + [{"id": "scanProblem", "severity": "FATAL", "finding": "Can't connect"}], good_alpn, False),
        ("an empty scan", [], good_alpn, False),
    ]
    mismatches = 0
    for name, findings, transcript, expected in cases:
        failures = judge(findings, transcript)
        passed = not failures
        if passed != expected:
            mismatches += 1
            print("self-test: %s: expected %s, got %s %s" % (name, "pass" if expected else "fail", "pass" if passed else "fail", failures))
    pretty = {"scanResult": [{"protocols": passing[:6], "ciphers": passing[6:]}]}
    if len(findings_of(pretty)) != len(passing):
        mismatches += 1
        print("self-test: the pretty JSON layout was not read")
    print("self-test: %d cases, %d mismatches" % (len(cases) + 1, mismatches))
    return mismatches


def main(argv):
    parser = argparse.ArgumentParser(description="Judge a TLS scan of an Axiom.Mesh node against SPEC.md 4.2.")
    parser.add_argument("--testssl", help="testssl.sh JSON output (--jsonfile or --jsonfile-pretty)")
    parser.add_argument("--alpn", help="transcript of `openssl s_client -alpn axiom-mesh/1`")
    parser.add_argument("--self-test", action="store_true", help="check the rules against built-in scans and exit")
    args = parser.parse_args(argv)

    if args.self_test:
        return 0 if self_test() == 0 else 1
    if not args.testssl:
        parser.print_usage(sys.stderr)
        return 2
    try:
        with open(args.testssl, encoding="utf-8") as scan:
            findings = findings_of(json.load(scan))
        transcript = None
        if args.alpn:
            with open(args.alpn, encoding="utf-8", errors="replace") as handshake:
                transcript = handshake.read()
    except (OSError, ValueError) as error:
        print("Cannot read the scan: %s" % error, file=sys.stderr)
        return 2

    failures = judge(findings, transcript)
    for failure in failures:
        print("FAIL: " + failure)
    if failures:
        return 1
    print("PASS: TLS 1.3 only, AEAD suites (%d findings judged), no session tickets, no early data, ALPN %s." % (len(findings), ALPN))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
