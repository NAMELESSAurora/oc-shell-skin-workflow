"""Offline voice format helpers: standard library, no network or TTS calls."""
from __future__ import annotations
import array
import hashlib
import html
import ipaddress
import json
import math
import re
import struct
import sys
from pathlib import Path
from urllib.parse import parse_qsl, urlsplit

SIDECAR_FIELDS = {"role", "id", "ja", "zh", "delivery", "model", "sha256"}
BASE_CONTEXTS = ("touch", "dollTouch", "squeeze", "drag", "release", "impact", "repeatTouch")

class WorkflowError(ValueError):
    pass

def role_id(value):
    if not isinstance(value, str) or not re.fullmatch(r"[a-z0-9][a-z0-9_]{0,39}", value):
        raise WorkflowError("Role must be 1-40 lowercase ASCII letters, digits or underscores.")
    return value

def read_json(path):
    if path.stat().st_size > 4 * 1024 * 1024:
        raise WorkflowError("JSON exceeds the 4 MiB input limit.")
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (UnicodeError, json.JSONDecodeError) as exc:
        raise WorkflowError("Expected valid UTF-8 JSON.") from exc

def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

def sha256(data):
    return hashlib.sha256(data).hexdigest()

def text(value, label, limit, empty=False):
    if not isinstance(value, str) or len(value) > limit or (not empty and not value.strip()):
        raise WorkflowError(f"{label} must be text of at most {limit} characters.")
    if any(ord(c) < 32 and c not in "\r\n\t" for c in value):
        raise WorkflowError(f"{label} contains unsupported control characters.")
    return value

def load_lines(path, role):
    data = read_json(path)
    if not isinstance(data, dict):
        raise WorkflowError("Lines JSON must be an object.")
    chars = data.get("Characters")
    raw = chars.get(role) if isinstance(chars, dict) else data.get("Lines") if data.get("Role") == role else None
    if not isinstance(raw, list) or not raw or len(raw) > 1000:
        raise WorkflowError("Requested role needs 1-1000 Characters lines or a matching Role/Lines draft.")
    result, seen = [], set()
    for item in raw:
        if not isinstance(item, dict):
            raise WorkflowError("Every line must be an object.")
        ident = item.get("Id")
        if not isinstance(ident, str) or not re.fullmatch(r"[A-Za-z0-9_]{1,100}", ident) or not ident.startswith(role + "_"):
            raise WorkflowError("Id must be a safe ASCII filename with the exact role_ prefix.")
        if ident in seen:
            raise WorkflowError("Duplicate interaction Id.")
        seen.add(ident)
        context = item.get("Context")
        if not isinstance(context, str) or not re.fullmatch(r"[A-Za-z][A-Za-z0-9]{0,30}", context):
            raise WorkflowError("Context must be a safe event name.")
        result.append({"Id": ident, "Context": context, "Japanese": text(item.get("Japanese"), "Japanese", 600),
                       "Chinese": text(item.get("Chinese"), "Chinese", 2000), "Delivery": text(item.get("Delivery"), "Delivery", 1500)})
    return result

def canonical_script(role, lines):
    return {"Version": 1, "Language": "ja", "Origin": "Reviewed original interaction lines; offline prepared library.", "Characters": {role: lines}}

def pcm_wave(path):
    # Preserve PCM exactly; rebuild fmt/data only, dropping possible embedded private metadata.
    if path.stat().st_size > 2 * 1024 * 1024:
        raise WorkflowError("WAV exceeds the 2 MiB input limit.")
    raw = path.read_bytes()
    if len(raw) < 44 or raw[:4] != b"RIFF" or raw[8:12] != b"WAVE":
        raise WorkflowError("Expected actual RIFF/WAVE, not renamed compressed audio.")
    if struct.unpack_from("<I", raw, 4)[0] != len(raw) - 8:
        raise WorkflowError("WAV is truncated, streaming-open or has trailing bytes.")
    pos, fmt, pcm, chunks = 12, None, None, []
    while pos < len(raw):
        if pos + 8 > len(raw):
            raise WorkflowError("Incomplete WAV chunk header.")
        tag, size = struct.unpack_from("<4sI", raw, pos)
        end = pos + 8 + size
        padded = end + size % 2
        if padded > len(raw):
            raise WorkflowError("Truncated WAV chunk.")
        body = raw[pos + 8:end]
        chunks.append(tag.decode("ascii", errors="replace"))
        if tag == b"fmt ":
            if fmt is not None or len(body) < 16:
                raise WorkflowError("Expected exactly one valid fmt chunk.")
            fmt = struct.unpack_from("<HHIIHH", body)
        if tag == b"data":
            if pcm is not None:
                raise WorkflowError("Expected exactly one data chunk.")
            pcm = body
        pos = padded
    if fmt != (1, 1, 24000, 48000, 2, 16) or not pcm or len(pcm) % 2:
        raise WorkflowError("Runtime WAV must be uncompressed 24,000 Hz mono 16-bit PCM.")
    frames = len(pcm) // 2
    seconds = frames / 24000
    if not .25 <= seconds <= 30:
        raise WorkflowError("Interaction WAV duration must be 0.25-30 seconds.")
    samples = array.array("h", pcm)
    if sys.byteorder != "little": samples.byteswap()
    peak = max(abs(x) for x in samples)
    if peak == 0:
        raise WorkflowError("All-silent PCM is not usable speech.")
    rms = math.sqrt(sum(x*x for x in samples) / len(samples))
    clipping = sum(abs(x) >= 32760 for x in samples) / len(samples)
    warnings = []
    if clipping > .001: warnings.append("possible-clipping: review by listening")
    if 20 * math.log10(peak / 32768) < -35: warnings.append("low-signal: review audibility")
    clean = (b"RIFF" + struct.pack("<I", 36 + len(pcm)) + b"WAVEfmt " + struct.pack("<I", 16)
             + struct.pack("<HHIIHH", 1, 1, 24000, 48000, 2, 16) + b"data" + struct.pack("<I", len(pcm)) + pcm)
    return clean, {"sourceSha256": sha256(raw), "sha256": sha256(clean), "pcmSha256": sha256(pcm), "frames": frames,
                   "seconds": seconds, "sampleRate": 24000, "channels": 1, "bits": 16,
                   "removedNonAudioChunks": [c for c in chunks if c not in ("fmt ", "data")],
                   "peakDbfs": 20 * math.log10(peak / 32768), "rmsDbfs": 20 * math.log10(rms / 32768),
                   "clippingFraction": clipping, "warnings": warnings}

def safe_relative(value, base, label="resource"):
    if not isinstance(value, str) or not value or "\\" in value or ":" in value or value.startswith("/"):
        raise WorkflowError(f"{label} must be a relative POSIX path.")
    candidate = (base / value).resolve()
    if not candidate.is_relative_to(base.resolve()):
        raise WorkflowError(f"{label} escapes its allowed folder.")
    return candidate

def public_url(value):
    value = text(value, "PublicUrl", 1800)
    parsed = urlsplit(value)
    if parsed.scheme != "https" or not parsed.hostname or parsed.username or parsed.password or parsed.fragment:
        raise WorkflowError("Source URL must be public HTTPS without credentials/fragments.")
    if parsed.hostname in ("localhost", "127.0.0.1", "::1") or parsed.hostname.endswith(".local"):
        raise WorkflowError("Source URL must refer to public material.")
    try:
        address = ipaddress.ip_address(parsed.hostname)
    except ValueError:
        address = None
    if address is not None and not address.is_global:
        raise WorkflowError("Source URL cannot expose a private/local address.")
    try:
        if parsed.port not in (None, 443): raise WorkflowError("Source URL cannot use a private service port.")
    except ValueError as exc:
        raise WorkflowError("Invalid source URL port.") from exc
    if parsed.query:
        q = parse_qsl(parsed.query, keep_blank_values=True)
        if not (parsed.hostname in ("youtube.com", "www.youtube.com", "m.youtube.com") and len(q) == 1
                and q[0][0] == "v" and re.fullmatch(r"[A-Za-z0-9_-]{6,32}", q[0][1])):
            raise WorkflowError("Source URL query may contain signatures; use the public canonical URL.")
    return value

def approved_sources(path, role):
    if path is None: return None
    data = read_json(path)
    if not isinstance(data, dict) or data.get("ApprovedForPreparation") is not True:
        raise WorkflowError("Source metadata must explicitly be manually approved for preparation.")
    raw = data.get("Sources")
    if not isinstance(raw, list) or not raw or len(raw) > 100:
        raise WorkflowError("Approved metadata needs 1-100 source entries.")
    sources = []
    for item in raw:
        if not isinstance(item, dict): raise WorkflowError("Source entry must be an object.")
        sources.append({"Title": text(item.get("Title"), "Source title", 300), "PublicUrl": public_url(item.get("PublicUrl")),
                        "Speaker": text(item.get("Speaker", ""), "Speaker", 300, True),
                        "Language": text(item.get("Language", "ja"), "Language", 20),
                        "PermissionNote": text(item.get("PermissionNote"), "PermissionNote", 1200)})
    return {"Role": role, "ApprovedForPreparation": True, "RightsReview": text(data.get("RightsReview"), "RightsReview", 1500),
            "Sources": sources, "AutomationLimit": "Uploader review recorded; no automatic rights, performer consent or voice-quality judgment."}

def model_name(value):
    if not isinstance(value, str) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,100}", value):
        raise WorkflowError("Model label must be a safe non-secret identifier.")
    return value

def sidecar(role, line, model, digest):
    return {"role": role, "id": line["Id"], "ja": line["Japanese"], "zh": line["Chinese"], "delivery": line["Delivery"], "model": model, "sha256": digest}

def manifest(role, lines, records, model):
    clips = []
    for line, record in zip(lines, records):
        clip = sidecar(role, line, model, record["sha256"])
        clip.update({"context": line["Context"], "status": "ready", "wav": f'{role}/{line["Id"]}.wav'})
        clips.append(clip)
    return {"Version": 1, "role": role, "language": "ja", "model": model, "audioFormat": {"sampleRate": 24000, "channels": 1, "bits": 16}, "cloudCredentialsIncluded": False, "clips": clips}

def listen_html(role, lines, portrait=False):
    esc = html.escape
    cards = []
    for line in lines:
        ident, context = line["Id"], line["Context"]
        cards.append(f'<article data-context="{esc(context, quote=True)}"><small>{esc(context)} · {esc(ident)}</small><p lang="ja">{esc(line["Japanese"])}</p><p class="translation">{esc(line["Chinese"])}</p><audio controls preload="none" src="{role}/{ident}.wav"></audio><a href="{role}/{ident}.wav" download>保存 WAV</a></article>')
    options = '<option value="">全部互动</option>' + ''.join(f'<option value="{esc(c, quote=True)}">{esc(c)}</option>' for c in sorted({l["Context"] for l in lines}))
    avatar = '<img src="../../assets/voice-avatar.png" alt="审核后的角色头像">' if portrait else '<span aria-hidden="true">♫</span>'
    return ('<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">'
            '<meta http-equiv="Content-Security-Policy" content="default-src &#39;none&#39;; style-src &#39;unsafe-inline&#39;; script-src &#39;unsafe-inline&#39;; img-src &#39;self&#39; file: data:; media-src &#39;self&#39; file:; connect-src &#39;none&#39;"><title>本地语音试听</title>'
            '<style>body{margin:0;background:#f1f6f5;color:#274b46;font:16px/1.6 system-ui,sans-serif}main{max-width:1120px;margin:auto;padding:32px 20px}header{display:flex;gap:20px;align-items:center}header img{width:90px;height:90px;object-fit:contain}header span{font-size:56px}.tools{display:flex;gap:12px;flex-wrap:wrap;margin:24px 0}input,select{font:inherit;padding:10px 14px;border:1px solid #bbd4cc;border-radius:14px;background:white}#cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:18px}article{padding:20px;background:white;border-radius:20px;border:1px solid #dae8e3;box-shadow:0 6px 20px #213a3510}small,.translation{color:#637c76}small{overflow-wrap:anywhere}audio{width:100%;height:40px}a{color:#397b69}p{white-space:pre-wrap;overflow-wrap:anywhere}[hidden]{display:none}</style>'
            f'<main><header>{avatar}<div><h1>{esc(role)} · 本地语音试听</h1><small>{len(lines)} 条 · 24 kHz / 单声道 / PCM16 · 零 API 请求</small></div></header><p>人工听辨语速、尾音、噪音与人物语气；格式校验不等于授权或克隆听感审核。</p><div class="tools"><label>场景 <select id="context">{options}</select></label><label>搜索 <input id="search" type="search"></label></div><section id="cards">'
            + ''.join(cards) + '</section></main>'
            '<script>const cards=[...document.querySelectorAll("article")],context=document.querySelector("#context"),search=document.querySelector("#search");function filter(){const q=search.value.toLocaleLowerCase();cards.forEach(c=>c.hidden=(context.value&&c.dataset.context!==context.value)||!c.textContent.toLocaleLowerCase().includes(q));}context.addEventListener("change",filter);search.addEventListener("input",filter);const players=[...document.querySelectorAll("audio")];players.forEach(a=>a.addEventListener("play",()=>players.forEach(b=>{if(a!==b)b.pause();})));</script></html>')
