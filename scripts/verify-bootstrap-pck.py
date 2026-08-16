#!/usr/bin/env python3
"""Regression check for the generated bootstrap PCK.

Builds the bootstrap PCK with make-bootstrap-pck.py, then re-parses the binary
from disk and asserts the settings the launcher depends on, most importantly
that the handheld orientation is SCREEN_SENSOR_PORTRAIT (5). The engine applies
this value before any managed code runs, so a wrong value here reintroduces the
startup landscape flash no matter what the patches do later.
"""

import importlib.util
import os
import struct
import sys

HEADER_SIZE = 104
SENSOR_PORTRAIT = 5


def load_generator():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    path = os.path.join(script_dir, "make-bootstrap-pck.py")
    spec = importlib.util.spec_from_file_location("make_bootstrap_pck", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def read_pck_entries(path):
    with open(path, "rb") as f:
        data = f.read()

    magic, version, major, minor, patch, flags = struct.unpack_from("<6I", data, 0)
    if magic != 0x43504447:
        raise AssertionError(f"bad PCK magic: {magic:#x}")
    file_base, dir_base = struct.unpack_from("<QQ", data, 24)

    relative = bool(flags & 0x02)
    offset = dir_base
    (count,) = struct.unpack_from("<I", data, offset)
    offset += 4

    entries = {}
    for _ in range(count):
        (path_len,) = struct.unpack_from("<I", data, offset)
        offset += 4
        raw_path = data[offset : offset + path_len].rstrip(b"\x00").decode("utf-8")
        offset += path_len
        file_offset, size = struct.unpack_from("<QQ", data, offset)
        offset += 16 + 16 + 4  # offsets + md5 + entry flags
        absolute = file_base + file_offset if relative else file_offset
        entries[raw_path] = data[absolute : absolute + size]
    return entries


def parse_project_binary(blob):
    if blob[:4] != b"ECFG":
        raise AssertionError("project.binary does not start with ECFG")
    (count,) = struct.unpack_from("<I", blob, 4)
    offset = 8

    settings = {}
    for _ in range(count):
        (key_len,) = struct.unpack_from("<I", blob, offset)
        offset += 4
        key = blob[offset : offset + key_len].decode("utf-8")
        offset += key_len
        (variant_len,) = struct.unpack_from("<I", blob, offset)
        offset += 4
        variant = blob[offset : offset + variant_len]
        offset += variant_len

        (variant_type,) = struct.unpack_from("<I", variant, 0)
        if variant_type == 2:  # int
            (value,) = struct.unpack_from("<I", variant, 4)
            settings[key] = value
        else:
            settings[key] = variant
    return settings


def main():
    generator = load_generator()
    generator.main()

    script_dir = os.path.dirname(os.path.abspath(__file__))
    pck_path = os.path.join(script_dir, "..", "android", "assets", "bootstrap.pck")

    entries = read_pck_entries(pck_path)
    failures = []

    binary = entries.get("res://project.binary")
    if binary is None:
        failures.append("res://project.binary missing from bootstrap PCK")
    else:
        settings = parse_project_binary(binary)
        orientation = settings.get("display/window/handheld/orientation")
        if orientation != SENSOR_PORTRAIT:
            failures.append(
                "project.binary orientation is "
                f"{orientation!r}, expected {SENSOR_PORTRAIT} (SCREEN_SENSOR_PORTRAIT)"
            )

    text = entries.get("res://project.godot")
    if text is None:
        failures.append("res://project.godot missing from bootstrap PCK")
    elif b"window/handheld/orientation=5" not in text:
        failures.append("project.godot does not pin orientation=5")

    if b"res://bootstrap.tscn" not in (entries.get("res://project.binary") or b"") and (
        "res://bootstrap.tscn" not in entries
    ):
        failures.append("bootstrap scene missing from PCK")

    if failures:
        for failure in failures:
            print(f"FAIL: {failure}")
        return 1

    print(
        "OK: bootstrap.pck orientation is SCREEN_SENSOR_PORTRAIT (5) "
        f"in both project.binary and project.godot ({len(entries)} entries)"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
