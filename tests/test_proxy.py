"""Exercise the proxy wrapper with isolated config and loopback sockets."""

from concurrent.futures import ThreadPoolExecutor
import json
import os
from pathlib import Path
import runpy
import socket
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, call, patch


WATCHER = Path(__file__).resolve().parents[1] / "setup" / "ds-proxy-watch"
WATCHER_FUNCTIONS = runpy.run_path(str(WATCHER))
PARSE_SETTINGS = WATCHER_FUNCTIONS["parse_settings"]
WATCHER_GLOBALS = PARSE_SETTINGS.__globals__
PROXY_VARS = ("http_proxy", "HTTP_PROXY", "https_proxy", "HTTPS_PROXY",
              "no_proxy", "NO_PROXY", "all_proxy", "ALL_PROXY")
WRAPPER_SCRIPT = """
from pathlib import Path
import runpy
import sys

wrapper = runpy.run_path(sys.argv[1])["exec_with_proxy"]
wrapper.__globals__["PROXY_MODE_FILE"] = Path(sys.argv[2])
sys.exit(wrapper(sys.argv[3:]))
"""
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


def run_wrapped_command(settings, mode="windows"):
    with tempfile.TemporaryDirectory() as temp_dir:
        registry = Path(temp_dir) / "fake-registry"
        registry.write_text(f"#!/bin/sh\ncat <<'REGISTRY'\n{settings}REGISTRY\n", encoding="utf-8")
        registry.chmod(0o700)
        config = Path(temp_dir) / "proxy-mode"
        config.write_text(mode + "\n", encoding="utf-8")
        env = os.environ.copy()
        env.update(dict.fromkeys(PROXY_VARS, "http://stale.invalid:8080"))
        env["DS_REG_EXE"] = str(registry)
        env["XDG_RUNTIME_DIR"] = temp_dir
        result = subprocess.run(
            [sys.executable, "-c", WRAPPER_SCRIPT, str(WATCHER), str(config), sys.executable, "-c", CHILD_SCRIPT,
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


class ProxyModeTests(unittest.TestCase):
    def test_missing_config_selects_builtin_clash(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            config = Path(temp_dir) / "proxy-mode"
            with patch.dict(WATCHER_GLOBALS, {"PROXY_MODE_FILE": config}):
                self.assertEqual(WATCHER_FUNCTIONS["read_proxy_mode"](), ("clash", 7897))

    def test_explicit_windows_and_clash_modes(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            config = Path(temp_dir) / "proxy-mode"
            with patch.dict(WATCHER_GLOBALS, {"PROXY_MODE_FILE": config}):
                for mode, expected in (("windows", ("windows", None)), ("clash 8118", ("clash", 8118))):
                    with self.subTest(mode=mode):
                        config.write_text(mode + "\n", encoding="utf-8")
                        self.assertEqual(WATCHER_FUNCTIONS["read_proxy_mode"](), expected)

    def test_invalid_mode_or_clash_port_raises(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            config = Path(temp_dir) / "proxy-mode"
            with patch.dict(WATCHER_GLOBALS, {"PROXY_MODE_FILE": config}):
                for mode in ("unknown", "clash invalid", "clash 0", "clash 65536"):
                    with self.subTest(mode=mode):
                        config.write_text(mode + "\n", encoding="utf-8")
                        with self.assertRaises(ValueError):
                            WATCHER_FUNCTIONS["read_proxy_mode"]()

    def test_clash_checks_guest_listener_before_probing(self):
        port = 8118
        endpoint = ("127.0.0.1", port)
        for has_listener, connects in ((False, False), (True, False), (True, True)):
            guest_listener = Mock(return_value=has_listener)
            reachable = Mock(return_value=connects)
            windows_settings = Mock()
            with self.subTest(has_listener=has_listener, connects=connects), patch.dict(WATCHER_GLOBALS, {
                "read_proxy_mode": lambda: ("clash", port),
                "guest_listener": guest_listener,
                "reachable": reachable,
                "windows_settings": windows_settings,
            }):
                active, _, configured = WATCHER_FUNCTIONS["active_proxy"]()
                expected = {"http": endpoint, "https": endpoint} if connects else {}
                self.assertEqual(active, expected)
                self.assertTrue(configured)
                guest_listener.assert_called_once_with(port)
                if has_listener:
                    reachable.assert_called_once_with(endpoint)
                else:
                    reachable.assert_not_called()
                windows_settings.assert_not_called()

    def test_guest_listener_requires_an_active_tcp_listener(self):
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", 0))
            port = listener.getsockname()[1]
            guest_listener = WATCHER_FUNCTIONS["guest_listener"]
            self.assertFalse(guest_listener(port))
            listener.listen()
            self.assertTrue(guest_listener(port))


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

    def test_builtin_clash_passes_its_guest_proxy_to_the_command(self):
        with socket.socket() as listener:
            listener.settimeout(4)
            listener.bind(("127.0.0.1", 0))
            listener.listen()
            port = listener.getsockname()[1]
            with ThreadPoolExecutor(max_workers=1) as executor:
                request = executor.submit(serve_once, listener, b"HTTP/1.1 200 Connection Established\r\n\r\n")
                args, env = run_wrapped_command(registry_settings(False, "127.0.0.1:8888"), f"clash {port}")
                self.assertTrue(request.result(timeout=5).startswith(b"CONNECT "))

        self.assertEqual(args, ["argument with spaces", ""])
        self.assertEqual(env["HTTP_PROXY"], f"http://127.0.0.1:{port}")
        self.assertEqual(env["HTTPS_PROXY"], env["HTTP_PROXY"])


class GnomeProxyTests(unittest.TestCase):
    def test_watcher_repairs_external_mode_change_without_reapplying_environment(self):
        schema = "org.gnome.system.proxy"
        bypass = ("localhost", "127.0.0.0/8")
        endpoints = dict.fromkeys(("http", "https"), ("127.0.0.1", 7897))

        def snapshot(mode):
            return (f"{schema} mode '{mode}'\n"
                    f"{schema} ignore-hosts {list(bypass)!r}\n"
                    f"{schema}.http host '127.0.0.1'\n"
                    f"{schema}.http port 7897\n"
                    f"{schema}.https host '127.0.0.1'\n"
                    f"{schema}.https port 7897\n")

        read_settings = Mock(side_effect=(schema + "\n", snapshot("manual"),
                                          snapshot("none"), snapshot("manual")))
        write_settings = Mock()
        write_environment = Mock()
        sleep = Mock(side_effect=(None, None, StopIteration))
        with patch.dict(WATCHER_GLOBALS, {
            "active_proxy": lambda preference: (endpoints, bypass, True),
            "read_proxy_mode": lambda: ("clash", 7897),
            "run": write_settings,
            "time": SimpleNamespace(sleep=sleep),
            "write_environment": write_environment,
        }), patch.object(WATCHER_GLOBALS["shutil"], "which", return_value="/usr/bin/gsettings"), \
                patch.object(subprocess, "check_output", read_settings):
            with self.assertRaises(StopIteration):
                WATCHER_FUNCTIONS["main"]()

        self.assertEqual(read_settings.call_args_list, [
            call(["gsettings", "list-schemas"], text=True, timeout=3),
            *[call(["gsettings", "list-recursively", schema], text=True, timeout=3)] * 3,
        ])
        self.assertEqual([entry for entry in write_settings.call_args_list if entry.args[0] == "gsettings"],
                         [call("gsettings", "set", schema, "mode", '"manual"')])
        write_environment.assert_called_once()


if __name__ == "__main__":
    unittest.main()
