"""Transcode the game PCK's desktop-only textures to ASTC 4x4 for Android.

The shipped PCK carries only bptc (BC7) and s3tc (DXT1/DXT5) compressed
textures. Android GPUs support neither family, so Godot decompresses every
one of them to RGBA8 at load time: ~500MB of VRAM on device and the memory
bandwidth bill that makes the phone run hot. ASTC 4x4 is natively supported
by the device GPU at the same byte size as BC7, which keeps textures
compressed in VRAM (4x smaller than the RGBA8 fallback) and removes the
load-time conversion entirely.

Reads the original PCK, rewrites every matching .ctex payload, and emits a
new PCK with a rebuilt directory. RGTC normal-map formats are left alone
(nothing in a 2D card game leans on them, and their channel semantics do not
survive a naive RGBA transcode).

Usage:
  python transcode_textures.py <in.pck> <out.pck> [--limit N] [--block 4x4]
"""
import os
import struct
import subprocess
import sys
import tempfile
import concurrent.futures as futures

import texture2ddecoder
from PIL import Image as PILImage
import io as _io

ASTCENC = os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "..", "..", "tmp", "toolchain", "astcenc", "bin", "astcenc-avx2.exe",
)

GODOT_FORMATS = {
    17: ("bc1", 8),
    18: ("bc2", 16),
    19: ("bc3", 16),
    22: ("bc7", 16),
}
ASTC_4X4 = 35
ASTC_FILE_HEADER = 16


def decode_to_rgba(fmt, width, height, data):
    if fmt == 17:
        return texture2ddecoder.decode_bc1(data, width, height)
    if fmt == 18:
        return texture2ddecoder.decode_bc2(data, width, height)
    if fmt == 19:
        return texture2ddecoder.decode_bc3(data, width, height)
    if fmt == 22:
        return texture2ddecoder.decode_bc7(data, width, height)
    raise ValueError("unsupported format %d" % fmt)


def encode_astc(bgra, width, height, block, workdir):
    # texture2ddecoder yields BGRA rows top-down; a 32-bit TGA needs no extra
    # dependency and astcenc reads it directly.
    tga = os.path.join(workdir, "in.tga")
    out = os.path.join(workdir, "out.astc")
    header = struct.pack(
        "<BBBHHBHHHHBB", 0, 0, 2, 0, 0, 0, 0, 0, width, height, 32, 0x28
    )
    with open(tga, "wb") as handle:
        handle.write(header)
        handle.write(bgra)
    run = subprocess.run(
        [ASTCENC, "-cl", tga, out, block, "-medium", "-silent", "-perceptual"],
        capture_output=True,
        creationflags=0x08000000,
    )
    if run.returncode != 0:
        raise RuntimeError(
            "astcenc failed: %s" % run.stderr.decode(errors="replace")[:200]
        )
    with open(out, "rb") as handle:
        return handle.read()[ASTC_FILE_HEADER:]


MIN_LOSSLESS_SIDE = 128


def transcode_ctex(blob, block, workdir):
    if blob[:4] != b"GST2":
        return None, "not GST2"
    dataformat, = struct.unpack_from("<I", blob, 36)
    w16, h16 = struct.unpack_from("<HH", blob, 40)
    mips, fmt = struct.unpack_from("<II", blob, 44)

    if dataformat == 0 and fmt in GODOT_FORMATS:
        name, block_bytes = GODOT_FORMATS[fmt]
        blocks_w = (w16 + 3) // 4
        blocks_h = (h16 + 3) // 4
        base_size = blocks_w * blocks_h * block_bytes
        payload = blob[52:]
        if len(payload) < base_size:
            return None, "short payload %d < %d" % (len(payload), base_size)
        bgra = decode_to_rgba(fmt, w16, h16, payload[:base_size])
    elif dataformat in (1, 2) and fmt in (4, 5):
        # Lossless PNG/WebP payloads decompress to RGBA8 in VRAM at load
        # time; they are the dominant memory class in this pack. Each mip
        # blob is length-prefixed, and only the base level exists here.
        # Float/SDF formats and small crisp icons stay untouched.
        if min(w16, h16) < MIN_LOSSLESS_SIDE:
            return None, "small %dx%d" % (w16, h16)
        blob_len, = struct.unpack_from("<I", blob, 52)
        image_bytes = blob[56:56 + blob_len]
        name = "webp" if dataformat == 2 else "png"
        try:
            decoded = PILImage.open(_io.BytesIO(image_bytes)).convert("RGBA")
        except Exception as ex:
            return None, "decode failed %s" % ex
        if decoded.size != (w16, h16):
            return None, "decoded %s != header %dx%d" % (decoded.size, w16, h16)
        bgra = decoded.tobytes("raw", "BGRA")
    else:
        return None, "dataformat=%d fmt=%d" % (dataformat, fmt)

    astc = encode_astc(bgra, w16, h16, block, workdir)

    expected = ((w16 + 3) // 4) * ((h16 + 3) // 4) * 16
    if len(astc) != expected:
        return None, "astc size %d != %d" % (len(astc), expected)

    out = bytearray()
    out += blob[:44]
    out += struct.pack("<II", 0, ASTC_4X4)
    out += astc
    return bytes(out), "%s->astc %dx%d" % (name, w16, h16)


def main():
    src, dst = sys.argv[1], sys.argv[2]
    limit = 0
    block = "4x4"
    if "--limit" in sys.argv:
        limit = int(sys.argv[sys.argv.index("--limit") + 1])
    if "--block" in sys.argv:
        block = sys.argv[sys.argv.index("--block") + 1]

    with open(src, "rb") as f:
        header = f.read(100)
        magic = header[:4]
        fmt_ver = struct.unpack_from("<I", header, 4)[0]
        assert magic == b"GDPC" and fmt_ver == 3, "expected PCK v3"
        file_base = struct.unpack_from("<Q", header, 24)[0]
        dir_off = struct.unpack_from("<Q", header, 32)[0]
        f.seek(dir_off)
        count = struct.unpack("<I", f.read(4))[0]
        entries = []
        for _ in range(count):
            plen = struct.unpack("<I", f.read(4))[0]
            path = f.read(plen)
            off, size = struct.unpack("<QQ", f.read(16))
            md5 = f.read(16)
            flags = struct.unpack("<I", f.read(4))[0]
            entries.append([path, off, size, md5, flags])

    def is_candidate(path):
        path = path.rstrip(bytes([0]))
        if not path.endswith(b".ctex"):
            return False
        if path.endswith((b".etc2.ctex", b".astc.ctex")):
            return False
        return True

    candidates = [e for e in entries if is_candidate(e[0])]
    if limit:
        candidates = candidates[:limit]
    print("entries=%d transcoding=%d" % (count, len(candidates)))

    def work(entry):
        with open(src, "rb") as fh:
            fh.seek(entry[1] + file_base)
            blob = fh.read(entry[2])
        with tempfile.TemporaryDirectory() as wd:
            try:
                new, note = transcode_ctex(blob, block, wd)
            except Exception as ex:
                return entry, None, "error %s" % ex
        return entry, new, note

    results = {}
    done = skipped = 0
    with futures.ThreadPoolExecutor(max_workers=16) as pool:
        for entry, new, note in pool.map(work, candidates):
            if new is None:
                skipped += 1
                if skipped <= 10:
                    print("SKIP", entry[0].rstrip(b"\x00").decode()[-70:], note)
            else:
                results[id(entry)] = new
                done += 1
                if done % 100 == 0:
                    print("  %d/%d" % (done, len(candidates)))
    print("transcoded=%d skipped=%d" % (done, skipped))

    # Data first (from file_base, preserving original order), directory
    # after it, and the header's directory-offset field patched to match.
    # The first attempt placed data after the original end-of-file directory
    # offset, which left a 1.9GB sparse hole at the front of the pack.
    with open(src, "rb") as fh, open(dst, "wb") as out:
        out.write(header[:100])
        out.seek(file_base)
        for e in sorted(entries, key=lambda e: e[1]):
            blob = results.get(id(e))
            if blob is None:
                fh.seek(e[1] + file_base)
                blob = fh.read(e[2])
            position = out.tell()
            out.write(blob)
            e[1] = position - file_base
            e[2] = len(blob)
            padding = (-len(blob)) % 32
            if padding:
                out.write(bytes(padding))
        new_dir_off = out.tell()
        out.write(struct.pack("<I", count))
        for e in entries:
            out.write(struct.pack("<I", len(e[0])))
            out.write(e[0])
            out.write(struct.pack("<QQ", e[1], e[2]))
            out.write(e[3])
            out.write(struct.pack("<I", e[4]))
        out.seek(32)
        out.write(struct.pack("<Q", new_dir_off))
    print("wrote", dst)


if __name__ == "__main__":
    main()
