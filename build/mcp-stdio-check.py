"""Drives an MCP stdio server end to end: initialize, tools/list, one offline tools/call.

usage: python build/mcp-stdio-check.py [--expect-tools a,b,c] -- <command...>
Exit code 0 on success, 1 on any failure. Any non-JSON line on stdout is a failure (stdout must carry only JSON-RPC).
"""
import json
import os
import queue
import subprocess
import sys
import threading
import time


def main() -> int:
    if "--" not in sys.argv:
        print(__doc__, file=sys.stderr)
        return 2
    split = sys.argv.index("--")
    options, command = sys.argv[1:split], sys.argv[split + 1:]
    expected = set(options[1].split(",")) if len(options) == 2 and options[0] == "--expect-tools" else set()

    if os.path.exists(command[0]):
        command[0] = os.path.abspath(command[0])  # Windows CreateProcess rejects relative paths with '/'
    proc = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    lines: "queue.Queue[bytes]" = queue.Queue()
    threading.Thread(target=lambda: [lines.put(line) for line in iter(proc.stdout.readline, b"")], daemon=True).start()
    threading.Thread(
        target=lambda: [sys.stderr.write("[server] " + line.decode("utf-8", "replace")) for line in iter(proc.stderr.readline, b"")],
        daemon=True,
    ).start()

    def send(message: dict) -> None:
        proc.stdin.write((json.dumps(message) + "\n").encode("utf-8"))
        proc.stdin.flush()

    def receive(request_id: int, timeout: float = 120.0):
        deadline = time.time() + timeout
        while time.time() < deadline:
            try:
                raw = lines.get(timeout=max(0.1, deadline - time.time()))
            except queue.Empty:
                break
            try:
                message = json.loads(raw)
            except ValueError:
                print("non-JSON line on stdout: " + raw.decode("utf-8", "replace").rstrip(), file=sys.stderr)
                return None
            if message.get("id") == request_id:
                return message
        print("timed out waiting for response %d" % request_id, file=sys.stderr)
        return None

    try:
        send({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {
            "protocolVersion": "2025-11-25", "capabilities": {},
            "clientInfo": {"name": "mcp-stdio-check", "version": "1.0"}}})
        init = receive(1)
        if not init or "result" not in init:
            print("initialize failed: %r" % (init,), file=sys.stderr)
            return 1
        print("server:", init["result"].get("serverInfo"))
        send({"jsonrpc": "2.0", "method": "notifications/initialized"})

        send({"jsonrpc": "2.0", "id": 2, "method": "tools/list"})
        listed = receive(2)
        names = {tool["name"] for tool in ((listed or {}).get("result") or {}).get("tools", [])}
        print("tools:", sorted(names))
        if not names or not expected.issubset(names):
            print("missing tools: %s" % sorted(expected - names), file=sys.stderr)
            return 1

        send({"jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": {
            "name": "validate_identifier", "arguments": {"value": "WL1A/00272852/9"}}})
        called = receive(3)
        result = (called or {}).get("result") or {}
        if not result or result.get("isError"):
            print("validate_identifier failed: %r" % (called,), file=sys.stderr)
            return 1
        print("validate_identifier:", result["content"][0]["text"][:200])
        return 0
    finally:
        proc.stdin.close()
        try:
            proc.wait(timeout=15)
        except subprocess.TimeoutExpired:
            proc.kill()


if __name__ == "__main__":
    sys.exit(main())
