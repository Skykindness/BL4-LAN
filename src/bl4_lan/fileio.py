"""Atomic local IPC. Files contain narrow data, never executable commands."""
import json
import math
import os
from pathlib import Path
import uuid

def write_json(path: Path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + ".tmp-" + uuid.uuid4().hex)
    try:
        temp.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
        os.replace(temp, path)
    finally:
        temp.unlink(missing_ok=True)

def read_request(path: Path, instance: str, now: float):
    try:
        if path.stat().st_size > 8192:
            return None
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        if not isinstance(data, dict) or data.get("instance") != instance:
            return None
        if data.get("action") not in ("host", "join", "status"):
            return None
        if not isinstance(data.get("id"), str) or not 1 <= len(data["id"]) <= 64:
            return None
        expires = data.get("expires")
        if isinstance(expires, bool) or not isinstance(expires, (int, float)) or not math.isfinite(expires) or not now <= expires <= now + 120:
            return None
        return data
    except (OSError, ValueError, TypeError):
        return None
