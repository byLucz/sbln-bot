#!/usr/bin/env python3
import json
import os
import socket
import ssl
import statistics
import time
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse

NAME = "R-WATCHER agent for sbln-bot"
VERSION = "1.1"
TOKEN = os.environ.get("RAGENT_TOKEN", "")
PORT = int(os.environ.get("RAGENT_PORT", "8687"))
TIMEOUT = float(os.environ.get("RAGENT_TIMEOUT", "4"))
WORKERS = int(os.environ.get("RAGENT_WORKERS", "32"))
DOMAINS_FILE = Path(os.environ.get("RAGENT_DOMAINS", Path(__file__).with_name("domains.txt")))
SAMPLES = 3
FINALISTS = 40

CONTEXT = ssl.create_default_context()
CONTEXT.minimum_version = ssl.TLSVersion.TLSv1_2
CONTEXT.set_alpn_protocols(["h2", "http/1.1"])


def load_domains():
    lines = DOMAINS_FILE.read_text(encoding="utf-8").splitlines()
    return list(dict.fromkeys(x.strip().lower() for x in lines if x.strip() and not x.lstrip().startswith("#")))


def measure(domain, samples):
    result = {"domain": domain, "ok": False}
    try:
        infos = socket.getaddrinfo(domain, 443, socket.AF_INET, socket.SOCK_STREAM)
    except OSError as e:
        result["error"] = f"DNS: {e.strerror or e}"
        return result

    ip = infos[0][4][0]
    result["ip"] = ip

    tcp, tls = [], []
    for _ in range(samples):
        started = time.perf_counter()
        try:
            sock = socket.create_connection((ip, 443), timeout=TIMEOUT)
        except OSError as e:
            result["error"] = f"TCP: {e.strerror or e}"
            return result
        connected = time.perf_counter()
        try:
            with CONTEXT.wrap_socket(sock, server_hostname=domain) as tls_sock:
                tls.append((time.perf_counter() - connected) * 1000)
                tcp.append((connected - started) * 1000)
                result["tls"] = tls_sock.version()
                result["alpn"] = tls_sock.selected_alpn_protocol()
                cert = tls_sock.getpeercert()
                expires = datetime.fromtimestamp(ssl.cert_time_to_seconds(cert["notAfter"]), timezone.utc)
                result["cert_days"] = (expires - datetime.now(timezone.utc)).days
        except ssl.SSLCertVerificationError as e:
            result["error"] = f"сертификат: {e.verify_message}"
            return result
        except (ssl.SSLError, OSError) as e:
            result["error"] = f"TLS: {getattr(e, 'reason', None) or e}"
            return result

    result["tcp_ms"] = round(statistics.median(tcp), 1)
    result["tls_ms"] = round(statistics.median(tls), 1)
    result["jitter_ms"] = round(max(tcp) - min(tcp), 1)

    problems = []
    if result.get("tls") != "TLSv1.3":
        problems.append("нет TLS 1.3")
    if result.get("alpn") != "h2":
        problems.append("нет h2")
    if result.get("cert_days", 0) < 14:
        problems.append("сертификат истекает")
    result["ok"] = not problems
    if problems:
        result["error"] = ", ".join(problems)
    result["score"] = score(result)
    return result


def score(r):
    if not r.get("ok"):
        return 0
    tcp = r["tcp_ms"]
    points = 100 - min(tcp, 250) * 0.3
    points -= min(r["tls_ms"] - tcp, 200) * 0.1 if r["tls_ms"] > tcp else 0
    points -= min(r["jitter_ms"], 50) * 0.2
    return max(1, round(points))


def scan(current, top):
    domains = load_domains()
    if current:
        domains = [current] + [d for d in domains if d != current]
    started = time.perf_counter()
    with ThreadPoolExecutor(max_workers=WORKERS) as pool:
        first = list(pool.map(lambda d: measure(d, 1), domains))
        finalists = [r["domain"] for r in sorted((r for r in first if r["ok"]), key=lambda r: r["tcp_ms"])][:FINALISTS]
        if current and current not in finalists:
            finalists.append(current)
        final = {r["domain"]: r for r in pool.map(lambda d: measure(d, SAMPLES), finalists)}
    results = [final.get(r["domain"], r) for r in first]
    ranked = sorted((r for r in results if r["ok"] and r["domain"] in final), key=lambda r: (-r["score"], r["tcp_ms"]))
    return {
        "name": NAME,
        "version": VERSION,
        "host": socket.gethostname(),
        "elapsed_ms": round((time.perf_counter() - started) * 1000),
        "total": len(results),
        "passed": sum(1 for r in results if r["ok"]),
        "current": next((r for r in results if r["domain"] == current), None) if current else None,
        "results": ranked[:top],
    }


class Handler(BaseHTTPRequestHandler):
    server_version = "ragent/" + VERSION

    def do_GET(self):
        if not TOKEN or self.headers.get("Authorization") != f"Bearer {TOKEN}":
            return self.reply(401, {"error": "unauthorized"})
        url = urlparse(self.path)
        query = parse_qs(url.query)
        if url.path == "/health":
            return self.reply(200, {"ok": True, "name": NAME, "version": VERSION, "domains": len(load_domains())})
        if url.path == "/scan":
            current = (query.get("current") or [""])[0].strip().lower() or None
            raw = (query.get("top") or ["10"])[0]
            top = max(1, min(int(raw) if raw.isdigit() else 10, 50))
            return self.reply(200, scan(current, top))
        return self.reply(404, {"error": "not found"})

    def reply(self, status, payload):
        body = json.dumps(payload, ensure_ascii=False).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, fmt, *args):
        pass


if __name__ == "__main__":
    if not TOKEN:
        raise SystemExit("RAGENT_TOKEN не задан")
    load_domains()
    ThreadingHTTPServer(("0.0.0.0", PORT), Handler).serve_forever()
