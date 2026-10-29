#!/usr/bin/env python3
import hashlib
import hmac
import json
import os
import shutil
import socket
import ssl
import statistics
import struct
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse

NAME = "R-WATCHER agent for sbln-bot"
VERSION = "1.8"
MUSIC_UNIT = Path("/etc/systemd/system/ragent-yt.service")
TOKEN = os.environ.get("RAGENT_TOKEN", "")
PORT = int(os.environ.get("RAGENT_PORT", "8687"))
TIMEOUT = float(os.environ.get("RAGENT_TIMEOUT", "4"))
WORKERS = int(os.environ.get("RAGENT_WORKERS", "32"))
DOMAINS_FILE = Path(os.environ.get("RAGENT_DOMAINS", Path(__file__).with_name("domains.txt")))
STATE = Path(os.environ.get("STATE_DIRECTORY", "/var/lib/ragent"))
SAMPLES = 3
FINALISTS = 40
CLEAN_START = 15
CLEAN_WAIT = 300
CLEAN_LOCK = threading.Lock()
SCAN_LOCK = threading.Lock()
SKEW = 60

CONTEXT = ssl.create_default_context()
CONTEXT.minimum_version = ssl.TLSVersion.TLSv1_2
CONTEXT.set_alpn_protocols(["h2", "http/1.1"])

HTTP = ssl.create_default_context()
HTTP.minimum_version = ssl.TLSVersion.TLSv1_2
HTTP.set_alpn_protocols(["http/1.1"])

REALITY = ssl.create_default_context()
REALITY.minimum_version = ssl.TLSVersion.TLSv1_3
REALITY.set_alpn_protocols(["h2", "http/1.1"])

MAX_IPS = 3
X25519 = 0x001D
H2_PREFACE = bytes.fromhex("505249202a20485454502f322e300d0a0d0a534d0d0a0d0a") + bytes.fromhex("000000040000000000")


def load_domains():
    lines = DOMAINS_FILE.read_text(encoding="utf-8").splitlines()
    return list(dict.fromkeys(x.strip().lower() for x in lines if x.strip() and not x.lstrip().startswith("#")))


def field(entries, name):
    for item in entries or ():
        for key, value in item:
            if key == name:
                return value
    return None


def block(payload, size):
    return len(payload).to_bytes(size, "big") + payload


def extension(kind, payload):
    return struct.pack(">HH", kind, len(payload)) + payload


def client_hello(domain, group):
    host = domain.encode("idna")
    extensions = b"".join([
        extension(0x0000, block(b"\x00" + block(host, 2), 2)),
        extension(0x000A, block(struct.pack(">H", group), 2)),
        extension(0x000D, block(b"\x04\x03\x08\x04\x04\x01\x08\x07\x05\x03\x08\x05\x05\x01\x06\x03\x08\x06\x06\x01", 2)),
        extension(0x0010, block(block(b"h2", 1), 2)),
        extension(0x002B, block(b"\x03\x04", 1)),
        extension(0x0033, block(struct.pack(">H", group) + block(os.urandom(32), 2), 2)),
    ])

    body = b"".join([
        b"\x03\x03",
        os.urandom(32),
        block(os.urandom(32), 1),
        block(struct.pack(">HHH", 0x1301, 0x1302, 0x1303), 2),
        block(b"\x00", 1),
        block(extensions, 2),
    ])

    handshake = b"\x01" + block(body, 3)
    return b"\x16\x03\x01" + block(handshake, 2)


def x25519_ok(domain, ip, group=X25519):
    try:
        sock = socket.create_connection((ip, 443), timeout=TIMEOUT)
    except OSError:
        return False
    try:
        sock.sendall(client_hello(domain, group))
        head = sock.recv(5)
        if len(head) != 5 or head[0] != 0x16:
            return False
        length = int.from_bytes(head[3:5], "big")
        body = b""
        while len(body) < min(length, 6):
            chunk = sock.recv(min(length, 6) - len(body))
            if not chunk:
                return False
            body += chunk
        return body[0] == 0x02
    except OSError:
        return False
    finally:
        sock.close()


def names(cert):
    values = [value for kind, value in cert.get("subjectAltName", ()) if kind == "DNS"]
    return values


def http_head(domain, ip):
    info = {}
    request = (
        "HEAD / HTTP/1.1\r\n"
        f"Host: {domain}\r\n"
        "User-Agent: Mozilla/5.0\r\n"
        "Accept: */*\r\n"
        "Connection: close\r\n\r\n"
    ).encode("ascii", "ignore")

    try:
        sock = socket.create_connection((ip, 443), timeout=TIMEOUT)
    except OSError:
        return info

    try:
        with HTTP.wrap_socket(sock, server_hostname=domain) as tls_sock:
            tls_sock.sendall(request)
            raw = b""
            while len(raw) < 4096:
                chunk = tls_sock.recv(4096 - len(raw))
                if not chunk:
                    break
                raw += chunk
                if b"\r\n\r\n" in raw:
                    break
    except (ssl.SSLError, OSError):
        return info

    head = raw.split(b"\r\n\r\n", 1)[0].decode("utf-8", "replace").splitlines()
    if not head:
        return info

    parts = head[0].split(" ")
    if len(parts) > 1 and parts[1].isdigit():
        info["http_status"] = int(parts[1])

    for line in head[1:]:
        name, _, value = line.partition(":")
        key = name.strip().lower()
        value = value.strip()
        if key == "server":
            info["http_server"] = value[:64]
        elif key == "alt-svc":
            info["h3"] = "h3" in value
        elif key == "location":
            info["redirect"] = value[:128]

    return info


def h2_alive(tls_sock):
    try:
        tls_sock.sendall(H2_PREFACE)
        head = tls_sock.recv(9)
    except OSError:
        return False
    return len(head) == 9 and head[3] == 4


def measure(domain, samples, deep=False):
    result = {"domain": domain, "ok": False}
    context = REALITY if deep else CONTEXT
    try:
        infos = socket.getaddrinfo(domain, 443, 0, socket.SOCK_STREAM)
    except OSError as e:
        result["error"] = f"DNS: {e.strerror or e}"
        return result

    ips = list(dict.fromkeys(info[4][0] for info in infos if info[0] == socket.AF_INET))[:MAX_IPS]
    if not ips:
        result["error"] = "DNS: нет A-записей"
        return result

    result["ip"] = ips[0]
    result["ips"] = len(ips)
    if deep:
        result["ipv6"] = any(info[0] == socket.AF_INET6 for info in infos)

    tcp, tls = [], []
    for attempt in range(samples):
        started = time.perf_counter()
        try:
            sock = socket.create_connection((ips[0], 443), timeout=TIMEOUT)
        except OSError as e:
            result["error"] = f"TCP: {e.strerror or e}"
            return result
        connected = time.perf_counter()
        try:
            with context.wrap_socket(sock, server_hostname=domain) as tls_sock:
                tls.append((time.perf_counter() - connected) * 1000)
                tcp.append((connected - started) * 1000)
                result["tls"] = tls_sock.version()
                result["alpn"] = tls_sock.selected_alpn_protocol()
                cert = tls_sock.getpeercert()
                expires = datetime.fromtimestamp(ssl.cert_time_to_seconds(cert["notAfter"]), timezone.utc)
                result["cert_days"] = (expires - datetime.now(timezone.utc)).days
                if deep and attempt == samples - 1:
                    result["cert_cn"] = field(cert.get("subject"), "commonName")
                    result["cert_issuer"] = field(cert.get("issuer"), "organizationName")
                    result["cipher"] = (tls_sock.cipher() or ("", "", 0))[0]
                    sans = names(cert)
                    result["cert_sans"] = len(sans)
                    result["cert_wildcard"] = any(name.startswith("*.") for name in sans)
                    result["cert_to"] = cert.get("notAfter")
                    talk = time.perf_counter()
                    result["h2_ok"] = result["alpn"] == "h2" and h2_alive(tls_sock)
                    result["data_ms"] = round((time.perf_counter() - talk) * 1000, 1)
        except ssl.SSLCertVerificationError as e:
            result["error"] = f"сертификат: {e.verify_message}"
            return result
        except (ssl.SSLError, OSError) as e:
            reason = getattr(e, "reason", None) or e
            result["error"] = f"нет TLS 1.3: {reason}" if deep else f"TLS: {reason}"
            return result

    if deep:
        result["x25519"] = x25519_ok(domain, ips[0])
        result.update(http_head(domain, ips[0]))

    if deep and len(ips) > 1:
        result["ips_ok"] = 1 + sum(reachable(ip, domain, context) for ip in ips[1:])

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
    if deep:
        if not result.get("h2_ok"):
            problems.append("h2 не отвечает")
        if not result.get("cert_cn") or not result.get("cert_issuer"):
            problems.append("сертификат без CN или издателя")
        if result.get("x25519") is False:
            problems.append("нет x25519")
        if result.get("http_status", 200) >= 500:
            problems.append(f"сайт отдаёт {result['http_status']}")
        if result.get("ips_ok", len(ips)) < len(ips):
            problems.append(f"живых адресов {result['ips_ok']} из {len(ips)}")
    result["ok"] = not problems
    if problems:
        result["error"] = ", ".join(problems)
    result["score"] = score(result)
    return result


def reachable(ip, domain, context):
    try:
        sock = socket.create_connection((ip, 443), timeout=TIMEOUT)
    except OSError:
        return False
    try:
        with context.wrap_socket(sock, server_hostname=domain) as tls_sock:
            return tls_sock.version() == "TLSv1.3" and tls_sock.selected_alpn_protocol() == "h2"
    except (ssl.SSLError, OSError):
        return False


def score(r):
    if not r.get("ok"):
        return 0
    tcp = r["tcp_ms"]
    points = 100 - min(tcp, 250) * 0.3
    points -= min(r["tls_ms"] - tcp, 200) * 0.1 if r["tls_ms"] > tcp else 0
    points -= min(r["jitter_ms"], 50) * 0.2
    points -= min(r.get("data_ms", 0), 300) * 0.05
    if r.get("http_status", 200) >= 400:
        points -= 10
    return max(1, round(points))


def scan(current, top):
    if not SCAN_LOCK.acquire(blocking=False):
        return None
    try:
        return run_scan(current, top)
    finally:
        SCAN_LOCK.release()


def run_scan(current, top):
    domains = load_domains()
    if current:
        domains = [current] + [d for d in domains if d != current]
    started = time.perf_counter()
    with ThreadPoolExecutor(max_workers=WORKERS) as pool:
        first = list(pool.map(lambda d: measure(d, 1), domains))
        finalists = [r["domain"] for r in sorted((r for r in first if r["ok"]), key=lambda r: r["tcp_ms"])][:FINALISTS]
        if current and current not in finalists:
            finalists.append(current)
        final = {r["domain"]: r for r in pool.map(lambda d: measure(d, SAMPLES, deep=True), finalists)}
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


def disk():
    usage = shutil.disk_usage("/")
    return {"total": usage.total, "used": usage.used, "free": usage.free}


def last_clean():
    try:
        return json.loads((STATE / "clean.result").read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def clean():
    with CLEAN_LOCK:
        request, result = STATE / "clean.request", STATE / "clean.result"
        before = result.stat().st_mtime if result.exists() else 0
        request.write_text(str(time.time()), encoding="utf-8")
        started = time.monotonic()
        while time.monotonic() - started < CLEAN_WAIT:
            time.sleep(1)
            if result.exists() and result.stat().st_mtime > before:
                return last_clean()
            if request.exists() and time.monotonic() - started > CLEAN_START:
                request.unlink(missing_ok=True)
                return {"ok": False, "error": "служба очистки не запустилась, обнови агента: sudo ragent update"}
        return {"ok": False, "error": "очистка не уложилась в 5 минут, смотри: journalctl -u ragent-clean"}


def signed(method, path, ts, sig):
    if not TOKEN or not ts or not sig or not ts.isdigit() or abs(time.time() - int(ts)) > SKEW:
        return False
    expected = hmac.new(TOKEN.encode(), f"{method}\n{path}\n{ts}".encode(), hashlib.sha256).hexdigest()
    return hmac.compare_digest(expected, sig.lower())


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if not signed("GET", self.path, self.headers.get("X-Ragent-Ts"), self.headers.get("X-Ragent-Sig")):
            self.close_connection = True
            return
        url = urlparse(self.path)
        query = parse_qs(url.query)
        if url.path == "/health":
            return self.reply(200, {"ok": True, "name": NAME, "version": VERSION, "domains": len(load_domains()),
                                    "disk": disk(), "clean": last_clean(), "music": MUSIC_UNIT.exists()})
        if url.path == "/clean":
            return self.reply(200, clean())
        if url.path == "/scan":
            current = (query.get("current") or [""])[0].strip().lower() or None
            raw = (query.get("top") or ["10"])[0]
            top = max(1, min(int(raw) if raw.isdigit() else 10, 50))
            result = scan(current, top)
            return self.reply(200, result) if result else self.reply(409, {"error": "скан уже идёт"})
        return self.reply(404, {"error": "not found"})

    def reply(self, status, payload):
        body = json.dumps(payload, ensure_ascii=False).encode()
        self.send_response_only(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def send_error(self, code, message=None, explain=None):
        self.close_connection = True

    def log_message(self, fmt, *args):
        pass


if __name__ == "__main__":
    if not TOKEN:
        raise SystemExit("RAGENT_TOKEN не задан")
    load_domains()
    ThreadingHTTPServer(("0.0.0.0", PORT), Handler).serve_forever()
