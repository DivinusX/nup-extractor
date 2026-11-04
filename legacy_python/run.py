"""Desktop Launcher for NUP Extractor.

Starts the local API backend and opens the interface in a borderless native app window.
"""

import multiprocessing
import os
import socket
import subprocess
import sys
import threading
import time
import webbrowser

# Ensure stdout/stderr exist in windowed/no-console mode
if sys.stdout is None:
    sys.stdout = open(os.devnull, "w")
if sys.stderr is None:
    sys.stderr = open(os.devnull, "w")

HOST = "127.0.0.1"
DEFAULT_PORT = 8765


def find_free_port(start_port: int = DEFAULT_PORT) -> int:
    """Find an available TCP port starting from start_port."""
    port = start_port
    while port < start_port + 100:
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
            if s.connect_ex((HOST, port)) != 0:
                return port
        port += 1
    return start_port


def launch_app_window(url: str):
    """Launch the web interface in an app window via Edge/Chrome or default browser."""
    edge_paths = [
        os.path.expandvars(r"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"),
        os.path.expandvars(r"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"),
    ]
    chrome_paths = [
        os.path.expandvars(r"%ProgramFiles%\Google\Chrome\Application\chrome.exe"),
        os.path.expandvars(r"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"),
        os.path.expandvars(r"%LocalAppData%\Google\Chrome\Application\chrome.exe"),
    ]

    for p in edge_paths + chrome_paths:
        if os.path.isfile(p):
            try:
                subprocess.Popen([p, f"--app={url}", "--window-size=1280,860"])
                print(f"[NUP Extractor] Launched desktop window using: {os.path.basename(p)}")
                return
            except Exception as e:
                print(f"[NUP Extractor] Failed to launch {p}: {e}")

    # Fallback to default browser
    print("[NUP Extractor] Opening in default web browser...")
    webbrowser.open(url)


def main():
    multiprocessing.freeze_support()
    import uvicorn
    import server

    port = find_free_port()
    url = f"http://{HOST}:{port}"

    print("=" * 60)
    print("           NUP EXTRACTOR - CLEAN-ROOM NX TOOL           ")
    print("=" * 60)
    print(f"[*] Starting local server at: {url}")

    # Start browser opener timer (wait 0.8s for server to start)
    threading.Timer(0.8, launch_app_window, args=[url]).start()

    try:
        uvicorn.run(server.app, host=HOST, port=port, log_level="warning")
    except KeyboardInterrupt:
        print("\n[NUP Extractor] Server stopped.")


if __name__ == "__main__":
    main()

