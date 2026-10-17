"""Generates Resources/pinyin.bin from the pinyin-data files in tools/data.

Reading rules ported from WinShun's tools/make_pinyin.py, with an output writer for this repo's
pinyin.bin layout (read by PinyinEngine.cs). Keep the two in step when either changes.

pinyin-data (https://github.com/mozillazg/pinyin-data, MIT licence, see tools/data/pinyin-data-LICENSE.txt)
provides:
  pinyin.txt      every reading found in several dictionaries, most common first
  kTGHZ2013.txt   the readings of the 8105 characters in the 2013 standard
                  《通用规范汉字字典》, without the rare and historic ones

For each character in CJK Extension A plus CJK Unified Ideographs (U+3400-U+9FFF)
the table keeps, without tones:
  - characters in the standard: their standard readings, most common first (at most three);
    so 胶 is only "jiao", but 行 is "xing" and "hang"
  - other (rare) characters: their most common reading
ü is written v, as pinyin keyboards do ("lv" for 绿).

The range opens at U+3400, not U+4E00: the char table indexes U+3007-U+9FFD anyway, so
Extension A costs no bytes it isn't already paying, and stopping at U+4E00 would drop the
5,787 characters the previous table covered there -- 77 of them listed in the 2013 standard
and used in people's names (㙍, 㫰...), whose files would silently stop answering pinyin.

Output layout (little-endian; PinyinEngine.cs reads exactly this):
  u16   syllable count
  i32   multi-table offset, bytes from file start
  i32   char-table offset, bytes from file start
  6 * 0x00 (reserved; the reader skips straight to offset 16)
  syllables: per entry 1-byte UTF-8 length + bytes, ascending
  multi table: per entry u16 reading count + count * u16 syllable ids
  char table: 28647 * u16, index = code point - 12295 (U+3007-U+9FFD):
      0xFFFF    no reading
      < 0x8000  single syllable id
      >= 0x8000 offset (in u16 units) into the multi table

Usage: python make_pinyin_bin.py   (from this folder or anywhere; writes ../Resources/pinyin.bin)
"""
import pathlib
import re
import struct
import unicodedata

HERE = pathlib.Path(__file__).resolve().parent
DATA = HERE / "data"
OUTPUT = HERE.parent / "Resources" / "pinyin.bin"

FIRST, LAST = 0x3400, 0x9FFF
TABLE_FIRST = 12295  # U+3007, as baked into PinyinEngine.CharTableOffset/IsChinese
TABLE_COUNT = 28647  # U+3007..U+9FFD, idem
MAX_READINGS = 3
DIAERESIS = "̈"


def toneless(reading):
    """'lǜ' -> 'lv', 'zhāng' -> 'zhang', 'ê̄' -> 'e'."""
    out = []
    for ch in unicodedata.normalize("NFD", reading):
        if ch == DIAERESIS and out and out[-1] == "u":
            out[-1] = "v"  # ü
        elif unicodedata.category(ch) != "Mn":
            out.append(ch)
    syllable = "".join(out)
    if not re.fullmatch(r"[a-z]+", syllable):
        raise SystemExit(f"unexpected reading {reading!r}")
    return syllable


def load(name):
    """{code point: [toneless readings, in file order, distinct]}"""
    table = {}
    line_re = re.compile(r"^U\+([0-9A-F]+):\s*([^#]+)")
    for line in (DATA / name).read_text(encoding="utf-8").splitlines():
        m = line_re.match(line)
        if not m:
            continue
        code = int(m.group(1), 16)
        if not FIRST <= code <= LAST:
            continue
        found = []
        for reading in m.group(2).strip().split(","):
            syllable = toneless(reading.strip())
            if syllable not in found:
                found.append(syllable)
        table[code] = found
    return table


def readings():
    common = load("pinyin.txt")
    standard = load("kTGHZ2013.txt")
    table = {}
    for code, ordered in common.items():
        if code in standard:
            allowed = standard[code]
            chosen = [s for s in ordered if s in allowed] + [s for s in allowed if s not in ordered]
            table[code] = chosen[:MAX_READINGS]
        else:
            table[code] = ordered[:1]
    return table


def main():
    table = readings()
    syllables = sorted({s for found in table.values() for s in found})
    assert len(syllables) < 0x8000
    number = {s: i for i, s in enumerate(syllables)}

    multi_index = {}
    multi_bytes = bytearray()
    char_values = []
    for code in range(TABLE_FIRST, TABLE_FIRST + TABLE_COUNT):
        found = table.get(code)
        if not found:
            char_values.append(0xFFFF)
            continue
        ids = [number[s] for s in found]
        if len(ids) == 1:
            char_values.append(ids[0])
            continue
        key = tuple(ids)
        offset = multi_index.get(key)
        if offset is None:
            offset = len(multi_bytes) // 2
            assert offset < 0x8000, "multi table overflow"
            multi_index[key] = offset
            multi_bytes += struct.pack("<H", len(ids))
            for i in ids:
                multi_bytes += struct.pack("<H", i)
        char_values.append(0x8000 + offset)

    syllable_bytes = bytearray()
    for s in syllables:
        b = s.encode("utf-8")
        assert 0 < len(b) < 256
        syllable_bytes.append(len(b))
        syllable_bytes += b

    multi_offset = 16 + len(syllable_bytes)
    char_offset = multi_offset + len(multi_bytes)
    out = bytearray()
    out += struct.pack("<H", len(syllables))
    out += struct.pack("<i", multi_offset)
    out += struct.pack("<i", char_offset)
    out += b"\x00" * 6
    out += syllable_bytes
    out += multi_bytes
    for value in char_values:
        out += struct.pack("<H", value)
    OUTPUT.write_bytes(out)

    several = sum(1 for found in table.values() if len(found) > 1)
    print(
        f"{OUTPUT}: {len(syllables)} syllables, {len(table)} characters, "
        f"{several} with several readings, {len(out)} bytes"
    )


if __name__ == "__main__":
    main()
