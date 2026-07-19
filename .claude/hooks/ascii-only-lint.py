#!/usr/bin/env python3
"""Block writes containing bytes outside printable ASCII.

PreToolUse hook for Write, Edit and MultiEdit. Reports the offending codepoint
and its line so it can be fixed before the write lands.

Test fixtures are exempt: they hold real upstream API payloads whose recipe names
legitimately carry accented characters.
"""
import json
import re
import sys

ALLOWED = re.compile(r"^[\x20-\x7E\x09\x0A\x0D]*$")
EXEMPT = re.compile(r"/(Fixtures|fixtures)/")


def offenders(text):
    for lineno, line in enumerate(text.splitlines(), start=1):
        for col, ch in enumerate(line, start=1):
            if not ALLOWED.match(ch):
                yield lineno, col, ch


def main():
    payload = json.load(sys.stdin)
    tool_input = payload.get("tool_input", {})
    path = tool_input.get("file_path", "")

    if EXEMPT.search(path):
        return 0

    chunks = [
        tool_input.get("content", ""),
        tool_input.get("new_string", ""),
    ]
    for edit in tool_input.get("edits", []):
        chunks.append(edit.get("new_string", ""))

    found = []
    for chunk in chunks:
        found.extend(offenders(chunk))

    if not found:
        return 0

    lines = [f"{path or 'content'} is not ASCII. This repo is ASCII only."]
    for lineno, col, ch in found[:20]:
        lines.append(f"  line {lineno} col {col}: U+{ord(ch):04X}")
    if len(found) > 20:
        lines.append(f"  ... and {len(found) - 20} more")
    lines.append("Use ' - ' for dashes, straight quotes, '->' for arrows, '...' for ellipsis.")

    print("\n".join(lines), file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())
