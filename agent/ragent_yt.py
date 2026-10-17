#!/usr/bin/env python3
import hashlib
import hmac
import json
import os
import re
import subprocess
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse

VERSION = next((line.split('"')[1] for line in Path(__file__).with_name("ragent.py").read_text(encoding="utf-8").splitlines()
                if line.startswith("VERSION = ")), "?")
TOKEN = os.environ.get("RAGENT_TOKEN", "")
PORT = int(os.environ.get("RAGENT_YT_PORT", "8688"))
CACHE = Path(os.environ.get("CACHE_DIRECTORY", "/var/cache/ragent-yt")) / "audio"
CACHE_LIMIT = int(os.environ.get("RAGENT_YT_CACHE_MB", "1024")) * 1024 * 1024
YTDLP = os.environ.get("RAGENT_YTDLP", "/opt/ragent/bin/yt-dlp")
CREDENTIALS = os.environ.get("CREDENTIALS_DIRECTORY")
COOKIES_SOURCE = Path(CREDENTIALS) / "cookies" if CREDENTIALS else None
COOKIES = CACHE.parent / "cookies.txt"
SKEW = 60
DOWNLOAD_TIMEOUT = 120
CHUNK = 64 * 1024
VIDEO_ID = re.compile(r"^[A-Za-z0-9_-]{11}$")
TYPES = {".webm": "audio/webm", ".m4a": "audio/mp4", ".opus": "audio/ogg", ".mp3": "audio/mpeg"}
AUDIO_FORMAT = "bestaudio[ext=webm]/bestaudio[ext=m4a]/bestaudio"
YTDLP_VERSION = None
WORKERS = threading.BoundedSemaphore(2)
DOWNLOADS = {}
DOWNLOADS_LOCK = threading.Lock()


def digest(path, stamp):
    return hmac.new(TOKEN.encode(), f"GET\n{path}\n{stamp}".encode(), hashlib.sha256).hexdigest()


def signed_header(path, ts, sig):
    if not TOKEN or not ts or not sig or not ts.isdigit() or abs(time.time() - int(ts)) > SKEW:
        return False
    return hmac.compare_digest(digest(path, ts), sig.lower())


def signed_link(path, exp, sig):
    if not TOKEN or not exp or not sig or not exp.isdigit() or time.time() > int(exp):
        return False
    return hmac.compare_digest(digest(path, exp), sig.lower())


def prepare_cookies():
    if COOKIES_SOURCE is None or not COOKIES_SOURCE.is_file():
        COOKIES.unlink(missing_ok=True)
        return
    COOKIES.parent.mkdir(parents=True, exist_ok=True)
    COOKIES.write_bytes(COOKIES_SOURCE.read_bytes())
    COOKIES.chmod(0o600)


def ytdlp(args, timeout):
    cookies = ["--cookies", str(COOKIES)] if COOKIES.is_file() else []
    with WORKERS:
        return subprocess.run([YTDLP, "--no-playlist", "--no-cache-dir", *cookies, *args],
                              capture_output=True, text=True, timeout=timeout)


def cached(video):
    return next((p for p in CACHE.glob(f"{video}.*") if p.suffix in TYPES), None)


def trim_cache():
    files = sorted((p for p in CACHE.glob("*") if p.is_file()), key=lambda p: p.stat().st_mtime)
    total = sum(p.stat().st_size for p in files)
    for path in files:
        if total <= CACHE_LIMIT:
            break
        total -= path.stat().st_size
        path.unlink(missing_ok=True)


def download(video):
    with DOWNLOADS_LOCK:
        lock = DOWNLOADS.setdefault(video, threading.Lock())
    with lock:
        if (found := cached(video)) is not None:
            found.touch()
            return found
        try:
            fetch(f"https://www.youtube.com/watch?v={video}", "yt-dlp не скачал трек")
        finally:
            drop_parts(video)
        if (found := cached(video)) is None:
            raise RuntimeError("yt-dlp не оставил файл")
        return found


def drop_parts(video):
    for part in CACHE.glob(f"{video}.*.part"):
        part.unlink(missing_ok=True)


def fetch(target, failure):
    CACHE.mkdir(parents=True, exist_ok=True)
    result = ytdlp(["--dump-json", "--no-simulate", "-f", AUDIO_FORMAT, "-o", str(CACHE / "%(id)s.%(ext)s"), target],
                   DOWNLOAD_TIMEOUT)
    if result.returncode != 0:
        raise RuntimeError(last_line(result.stderr) or failure)
    trim_cache()
    lines = [line for line in result.stdout.splitlines() if line.startswith("{")]
    return (json.loads(lines[-1]) if lines else {}), result.stderr


def resolve(query):
    target = query if re.match(r"^https?://", query) else f"ytsearch1:{query}"
    info, stderr = fetch(target, "yt-dlp не нашёл трек")
    video = info.get("id") or ""
    if not VIDEO_ID.match(video):
        raise RuntimeError(last_line(stderr) or "yt-dlp ничего не нашёл по запросу")
    drop_parts(video)
    if cached(video) is None:
        raise RuntimeError("yt-dlp не оставил файл")
    return {"id": video, "title": info.get("title"), "uploader": info.get("uploader") or info.get("channel"),
            "duration": info.get("duration"), "thumbnail": info.get("thumbnail"),
            "url": info.get("webpage_url") or f"https://www.youtube.com/watch?v={video}"}


def last_line(text):
    lines = [line.strip() for line in (text or "").splitlines() if line.strip()]
    return lines[-1][:300] if lines else ""


def detect_version():
    global YTDLP_VERSION
    try:
        YTDLP_VERSION = subprocess.run([YTDLP, "--version"], capture_output=True, text=True, timeout=30).stdout.strip() or None
    except (OSError, subprocess.TimeoutExpired):
        YTDLP_VERSION = None


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        url = urlparse(self.path)
        query = parse_qs(url.query)
        if url.path.startswith("/audio/"):
            if not signed_link(url.path, first(query, "exp"), first(query, "sig")):
                self.close_connection = True
                return
            return self.audio(url.path[len("/audio/"):])
        if not signed_header(self.path, self.headers.get("X-Ragent-Ts"), self.headers.get("X-Ragent-Sig")):
            self.close_connection = True
            return
        if url.path == "/health":
            size = sum(p.stat().st_size for p in CACHE.glob("*") if p.is_file()) if CACHE.exists() else 0
            return self.reply(200, {"ok": True, "version": VERSION, "ytdlp": YTDLP_VERSION, "cache": size, "limit": CACHE_LIMIT,
                                    "cookies": COOKIES.is_file()})
        if url.path == "/resolve":
            text = first(query, "q").strip()
            if not text:
                return self.reply(400, {"error": "пустой запрос"})
            try:
                return self.reply(200, resolve(text))
            except subprocess.TimeoutExpired:
                return self.reply(504, {"error": "yt-dlp не уложился по времени"})
            except (RuntimeError, ValueError) as error:
                return self.reply(502, {"error": str(error)})
        return self.reply(404, {"error": "not found"})

    def audio(self, video):
        if not VIDEO_ID.match(video):
            return self.reply(404, {"error": "not found"})
        try:
            path = download(video)
        except subprocess.TimeoutExpired:
            return self.reply(504, {"error": "yt-dlp не уложился по времени"})
        except RuntimeError as error:
            return self.reply(502, {"error": str(error)})

        size = path.stat().st_size
        start, end = 0, size - 1
        ranged = re.match(r"^bytes=(\d*)-(\d*)$", self.headers.get("Range", ""))
        if ranged and (ranged.group(1) or ranged.group(2)):
            if ranged.group(1):
                start = int(ranged.group(1))
                end = min(int(ranged.group(2)), size - 1) if ranged.group(2) else size - 1
            else:
                start = max(0, size - int(ranged.group(2)))
            if start > end or start >= size:
                self.send_response_only(416)
                self.send_header("Content-Range", f"bytes */{size}")
                self.send_header("Content-Length", "0")
                self.end_headers()
                return

        self.send_response_only(206 if ranged else 200)
        self.send_header("Content-Type", TYPES.get(path.suffix, "application/octet-stream"))
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Content-Length", str(end - start + 1))
        if ranged:
            self.send_header("Content-Range", f"bytes {start}-{end}/{size}")
        self.end_headers()
        with path.open("rb") as stream:
            stream.seek(start)
            left = end - start + 1
            while left > 0 and (chunk := stream.read(min(CHUNK, left))):
                try:
                    self.wfile.write(chunk)
                except (BrokenPipeError, ConnectionResetError):
                    return
                left -= len(chunk)

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


def first(query, key):
    return (query.get(key) or [""])[0]


if __name__ == "__main__":
    if not TOKEN:
        raise SystemExit("RAGENT_TOKEN не задан")
    CACHE.mkdir(parents=True, exist_ok=True)
    prepare_cookies()
    threading.Thread(target=detect_version, daemon=True).start()
    ThreadingHTTPServer(("0.0.0.0", PORT), Handler).serve_forever()
