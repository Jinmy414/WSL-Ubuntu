"""Exercise the proxy wrapper with a fake registry and a loopback server."""

from concurrent.futures import ThreadPoolExecutor
import json
import os
from pathlib import Path
import runpy
import socket
import subprocess
import sys
import tempfile
import unittest


WATCHER = Path(__file__).resolve().parents[1] / "setup" / "ds-proxy-watch"
PARSE_SETTINGS = runpy.run_path(str(WATCHER))["parse_settings"]
PROXY_VARS = ("http_proxy", "HTTP_PROXY", "https_proxy", "HTTPS_PROXY",
              "no_proxy", "NO_PROXY", "all_proxy", "ALL_PROXY")
CHILD_SCRIPT = """
import json
import os
import sys

keys = sys.argv[1].split(',')
print(json.dumps([sys.argv[2:], {key: os.environ[key] for key in keys if key in os.environ}]))
"""


def registry_settings(enabled, endpoints, bypass=""):
    return (f"    ProxyEnable    REG_DWORD    {int(enabled):#x}\n"
            f"    ProxyServer    REG_SZ    {endpoints}\n"
            f"    ProxyOverride    REG_SZ    {bypass}\n")


def run_wrapped_command(settings):
    with tempfile.TemporaryDirectory() as temp_dir:
        registry = Path(temp_dir) / "fake-registry"
        registry.write_text(f"#!/bin/sh\ncat <<'REGISTRY'\n{settings}REGISTRY\n", encoding="utf-8")
        registry.chmod(0o700)
        env = os.environ.copy()
        env.update(dict.fromkeys(PROXY_VARS, "http://stale.invalid:8080"))
        env["DS_REG_EXE"] = str(registry)
        env["XDG_RUNTIME_DIR"] = temp_dir
        result = subprocess.run(
            [sys.executable, str(WATCHER), "--exec", sys.executable, "-c", CHILD_SCRIPT,
             ",".join(PROXY_VARS), "argument with spaces", ""],
            env=env, capture_output=True, text=True, check=True, timeout=8,
        )
    return json.loads(result.stdout)


def serve_once(listener, response):
    connection, _ = listener.accept()
    with connection, connection.makefile("rb") as request:
        connection.settimeout(4)
        first_line = request.readline(256)
        while request.readline(256) not in (b"\r\n", b""):
            pass
        connection.sendall(response)
    return first_line


class RegistrySettingsTests(unittest.TestCase):
    def test_windows_per_protocol_settings_and_bypass(self):
        settings = registry_settings(True, "http=proxy.invalid:8080;https=[::1]:8443",
                                     "*.EXAMPLE.INVALID;localhost;*.example.invalid;<local>")
        endpoints, bypass = PARSE_SETTINGS(settings)
        self.assertEqual(endpoints, {"http": ("proxy.invalid", 8080), "https": ("::1", 8443)})
        self.assertEqual(bypass.count("*.example.invalid"), 1)
        self.assertEqual(bypass.count("localhost"), 1)
        self.assertNotIn("<local>", bypass)

    def test_malformed_or_unsupported_proxy_is_not_exported(self):
        for address in ("http://proxy.invalid:70000", "http://proxy.invalid/get", "socks5://proxy.invalid:1080"):
            with self.subTest(address=address):
                endpoints, _ = PARSE_SETTINGS(registry_settings(True, address))
                self.assertEqual(endpoints, {})


class ProxyCommandTests(unittest.TestCase):
    def test_disabled_system_proxy_clears_inherited_settings(self):
        args, env = run_wrapped_command(registry_settings(False, "127.0.0.1:8888"))
        self.assertEqual(args, ["argument with spaces", ""])
        self.assertEqual(env, {})

    def test_shared_proxy_requires_a_successful_connect_response(self):
        responses = (
            (b"HTTP/1.1 200 Connection Established\r\n\r\n", True),
            (b"HTTP/1.1 407 Proxy Authentication Required\r\n\r\n", False),
        )
        for response, accepted in responses:
            with self.subTest(accepted=accepted), socket.socket() as listener:
                listener.settimeout(4)
                listener.bind(("127.0.0.1", 0))
                listener.listen()
                port = listener.getsockname()[1]
                with ThreadPoolExecutor(max_workers=1) as executor:
                    request = executor.submit(serve_once, listener, response)
                    args, env = run_wrapped_command(registry_settings(True, f"127.0.0.1:{port}", "*.example.invalid"))
                    self.assertTrue(request.result(timeout=5).startswith(b"CONNECT "))

                self.assertEqual(args, ["argument with spaces", ""])
                if not accepted:
                    self.assertEqual(env, {})
                    continue
                for key in ("http_proxy", "HTTP_PROXY", "https_proxy", "HTTPS_PROXY"):
                    self.assertEqual(env[key], f"http://127.0.0.1:{port}")
                self.assertIn(".example.invalid", env["no_proxy"].split(","))
                self.assertEqual(env["NO_PROXY"], env["no_proxy"])
                self.assertNotIn("ALL_PROXY", env)
                self.assertNotIn("all_proxy", env)


if __name__ == "__main__":
    unittest.main()
