"""Generate FITS fixtures with Astropy and compare them with CFITS dumps."""

from __future__ import annotations

import json
import math
import re
import sys
from pathlib import Path

import numpy as np
from astropy.io import fits
from astropy.io.fits.card import UNDEFINED


def main() -> None:
    command = sys.argv[1]
    if command == "generate":
        generate(Path(sys.argv[2]))
    elif command == "check-dump":
        mismatches = check_dump(Path(sys.argv[2]), Path(sys.argv[3]))
        if mismatches:
            print("\n".join(mismatches))
            sys.exit(1)
    elif command == "compare-files":
        mismatches = compare_files(Path(sys.argv[2]), Path(sys.argv[3]))
        if mismatches:
            print("\n".join(mismatches))
            sys.exit(1)
    else:
        raise SystemExit(f"Unknown command {command}")


def generate(directory: Path) -> None:
    directory.mkdir(parents=True, exist_ok=True)
    write_headers(directory / "headers.fits")
    write_images(directory / "images.fits")
    write_ascii(directory / "ascii.fits")
    write_binary(directory / "bintable.fits")
    write_heap(directory / "heap.fits")
    write_groups(directory / "groups.fits")
    write_special(directory / "special.fits")


def write_headers(path: Path) -> None:
    header = fits.Header()
    header["SIMPLE"] = True
    header["BITPIX"] = 8
    header["NAXIS"] = 0
    header["BOOL"] = True
    header["FLAG"] = False
    header["INT"] = -42
    header["REAL"] = 1.5
    header["TINY"] = np.float64(1.25e-8)
    header["STR"] = "O'HARA"
    header["EMPTY"] = ""
    header["UNIT"] = (7, "[s] exposure")
    header["LONG"] = "This is a long string value that definitely exceeds sixty-eight characters so CONTINUE is required."
    header["CPLX"] = complex(1.5, -2.25)
    header["CINT"] = complex(4, -5)
    header["UNDEF"] = UNDEFINED
    header["COMMENT"] = "hello"
    header["HISTORY"] = "wrote it"
    header.add_blank("blank card")
    header["EXTEND"] = True
    fits.PrimaryHDU(header=header).writeto(path, overwrite=True)


def write_images(path: Path) -> None:
    primary = np.array([[1, -5, 3], [4, 5, 6]], dtype=np.int16)
    primary_hdu = fits.PrimaryHDU(primary)
    primary_hdu.header["BLANK"] = -5
    primary_hdu.header["BSCALE"] = 2
    primary_hdu.header["BZERO"] = 10
    primary_hdu.header["EXTEND"] = True
    hdus: list[fits.PrimaryHDU | fits.ImageHDU] = [primary_hdu]
    samples = {
        "U8": np.array([[0, 1], [254, 255]], dtype=np.uint8),
        "I16": np.array([0, -2, 32767], dtype=np.int16),
        "I32": np.arange(-2, 4, dtype=np.int32).reshape(2, 3),
        "I64": np.array([0, -1, 2**40], dtype=np.int64),
        "F32": np.array([1.25, np.nan, np.inf, -np.inf], dtype=np.float32),
        "F64": np.array([[0.5, np.nan], [-np.inf, 1.0]], dtype=np.float64),
        "U16": np.arange(6, dtype=np.uint16).reshape(2, 3),
        "EMPTY": np.array([], dtype=np.int16),
    }
    for name, data in samples.items():
        if data.size == 0:
            image = fits.ImageHDU()
            image.header["EXTNAME"] = name
            hdus.append(image)
            continue
        image = fits.ImageHDU(data, name=name)
        hdus.append(image)
    volume = np.arange(24, dtype=np.int32).reshape(2, 3, 4)
    cube = fits.ImageHDU(volume, name="CUBE")
    hdus.append(cube)
    fits.HDUList(hdus).writeto(path, overwrite=True)


def write_ascii(path: Path) -> None:
    columns = [
        fits.Column(name="NAME", format="A8", array=np.array(["alpha", "beta", "gamma"])),
        fits.Column(name="COUNT", format="I6", array=np.array([3, -1, 12]), null=-1),
        fits.Column(name="FLUX", format="E10.3", array=np.array([1.5, 2.25, -4.5])),
        fits.Column(name="DIST", format="D12.4", array=np.array([10.5, 1e-3, 8.0])),
        fits.Column(name="WIDE", format="F8.2", array=np.array([3.5, np.nan, 0.0])),
    ]
    table = fits.TableHDU.from_columns(columns, name="ASCII")
    fits.HDUList([fits.PrimaryHDU(), table]).writeto(path, overwrite=True)


def write_binary(path: Path) -> None:
    columns = [
        fits.Column(name="L", format="L", array=np.array([True, False, True])),
        fits.Column(name="X", format="3X", array=np.array([[1, 0, 1], [0, 1, 0], [1, 1, 0]], dtype=np.uint8)),
        fits.Column(name="B", format="B", array=np.array([0, 1, 255], dtype=np.uint8)),
        fits.Column(name="I", format="I", array=np.array([1, -2, 3], dtype=np.int16), null=-2),
        fits.Column(name="J", format="J", array=np.array([3, 4, -9], dtype=np.int32)),
        fits.Column(name="K", format="K", array=np.array([5, 6, 2**40], dtype=np.int64)),
        fits.Column(name="A", format="6A", array=np.array(["ab", "cd  e", "xyz"])),
        fits.Column(name="E", format="E", array=np.array([1.25, np.nan, -2.5], dtype=np.float32)),
        fits.Column(name="D", format="D", array=np.array([2.5, np.inf, -0.5])),
        fits.Column(name="C", format="C", array=np.array([1 + 2j, 3 + 4j, np.nan + 1j], dtype=np.complex64)),
        fits.Column(name="M", format="M", array=np.array([1 + 2j, 5 + 6j, 7 - 8j], dtype=np.complex128)),
        fits.Column(name="U", format="I", array=np.array([-32768, -32767, 32767], dtype=np.int16)),
        fits.Column(name="VEC", format="6J", dim="(2,3)", array=np.arange(18, dtype=np.int32).reshape(3, 3, 2)),
        fits.Column(name="SCALED", format="J", array=np.array([1, 2, 3], dtype=np.int32)),
    ]
    table = fits.BinTableHDU.from_columns(columns, name="EVENTS")
    table.header["TZERO12"] = 32768
    table.header["TSCAL12"] = 1
    table.header["TZERO14"] = 10
    table.header["TSCAL14"] = 2
    fits.HDUList([fits.PrimaryHDU(), table]).writeto(path, overwrite=True)


def write_heap(path: Path) -> None:
    integers = fits.Column(name="ARR", format="PJ()", array=[[1, 2, 3], [4], []])
    names = fits.Column(name="NAME", format="PA()", array=["ab", "cdef", ""])
    doubles = fits.Column(name="FLUX", format="QD()", array=[[1.5, np.nan], [np.inf], []])
    table = fits.BinTableHDU.from_columns([integers, names, doubles], name="HEAP")
    gap = 8
    table.header["THEAP"] = table.header["NAXIS1"] * table.header["NAXIS2"] + gap
    fits.HDUList([fits.PrimaryHDU(), table]).writeto(path, overwrite=True)


def write_groups(path: Path) -> None:
    data = np.arange(6, dtype=np.float32).reshape(2, 3)
    parameters = np.array([[1, 2], [3, 4]], dtype=np.float32)
    groups = fits.GroupData(data, parnames=["UU", "VV"], pardata=parameters, bitpix=-32)
    fits.GroupsHDU(groups).writeto(path, overwrite=True)


def write_special(path: Path) -> None:
    fits.PrimaryHDU(np.arange(4, dtype=np.int16)).writeto(path, overwrite=True)
    marker = b"SPECIAL!" + (b"\0" * (2880 - 8))
    with path.open("ab") as handle:
        handle.write(marker)


def check_dump(fits_path: Path, dump_path: Path) -> list[str]:
    document = json.loads(dump_path.read_text(encoding="utf-8"))
    with fits.open(fits_path) as hdul:
        mismatches: list[str] = []
        if len(hdul) != len(document["hdus"]):
            return [f"{fits_path.name}: HDU count {len(document['hdus'])} != {len(hdul)}"]
        for index, (hdu, dumped) in enumerate(zip(hdul, document["hdus"])):
            mismatches.extend(compare_cards(fits_path.name, index, hdu.header, dumped["cards"]))
            mismatches.extend(compare_payload(fits_path.name, index, hdu, dumped))
        return mismatches


def compare_files(left: Path, right: Path) -> list[str]:
    with fits.open(left) as a, fits.open(right) as b:
        if len(a) != len(b):
            return [f"HDU count {len(a)} != {len(b)}"]
        mismatches: list[str] = []
        for index, (ha, hb) in enumerate(zip(a, b)):
            mismatches.extend(compare_cards(left.name, index, ha.header, cards_from_header(hb.header)))
            left_payload = payload_from_hdu(ha)
            right_payload = payload_from_hdu(hb)
            if left_payload != right_payload:
                mismatches.append(
                    f"{left.name} hdu {index}: round trip payload mismatch\n left  {truncate(left_payload)}\n right {truncate(right_payload)}"
                )
        return mismatches


def compare_cards(name: str, index: int, header: fits.Header, cards: list[dict]) -> list[str]:
    expected = cards_from_header(header)
    if len(expected) != len(cards):
        return [f"{name} hdu {index}: card count {len(cards)} != {len(expected)}"]
    mismatches = []
    for actual, dumped in zip(expected, cards):
        if actual != dumped:
            mismatches.append(f"{name} hdu {index}: card {dumped.get('keyword')} {dumped} != {actual}")
    return mismatches


def cards_from_header(header: fits.Header) -> list[dict]:
    cards = []
    for card in header.cards:
        cards.append(card_dict(card.keyword, card.value, card.comment or ""))
    return cards


def card_dict(keyword: str, value, comment: str) -> dict:
    if value is UNDEFINED or value is None:
        kind, payload = "undefined", None
    elif isinstance(value, (bool, np.bool_)):
        kind, payload = "logical", bool(value)
    elif isinstance(value, (np.integer, int)) and not isinstance(value, bool):
        kind, payload = "integer", int(value)
    elif isinstance(value, (complex, np.complexfloating)):
        kind, payload = "complex", [canon_float(value.real), canon_float(value.imag)]
    elif isinstance(value, (float, np.floating)):
        kind, payload = "real", canon_float(value)
    else:
        kind, payload = "string", str(value)
    return {"keyword": keyword, "kind": kind, "value": payload, "comment": comment}


def compare_payload(name: str, index: int, hdu, dumped: dict) -> list[str]:
    expected = payload_from_hdu(hdu)
    actual = dumped.get("payload")
    if expected != actual:
        return [f"{name} hdu {index}: payload mismatch\n expected {truncate(expected)}\n actual   {truncate(actual)}"]
    return []


def payload_from_hdu(hdu) -> dict | None:
    if isinstance(hdu, fits.GroupsHDU):
        columns = {}
        for field in hdu.data.names:
            array = np.array(hdu.data[field])
            if array.ndim > 1:
                columns[field] = [canon_value(row) for row in array.tolist()]
            else:
                columns[field] = canon_array(array)
        return {"groups": columns}
    data = hdu.data
    if data is None:
        return None
    if isinstance(hdu, fits.TableHDU):
        columns = {}
        raw = np.ndarray(hdu.data.shape, dtype=hdu.data.dtype, buffer=hdu.data.data)
        for index, column in enumerate(hdu.columns, start=1):
            columns[column.name] = canon_ascii(raw[column.name], column.format, hdu.header.get(f"TNULL{index}"))
        return {"columns": columns}
    if isinstance(hdu, fits.BinTableHDU):
        columns = {}
        for index, column in enumerate(hdu.columns, start=1):
            values = canon_column(hdu.data[column.name])
            if "A" in column.format.upper():
                values = [join_characters(item) for item in values]
                values = split_character_fields(values, column.format)
            tnull = hdu.header.get(f"TNULL{index}")
            if tnull is not None:
                values = apply_null(values, int(tnull))
            columns[column.name] = values
        return {"columns": columns}
    return {"image": canon_array(np.array(data))}


def canon_ascii(fields, form: str, null_value) -> list:
    values = []
    code = form.strip()[0]
    null_text = None if null_value in (None, "") else str(null_value).strip()
    for field in fields.tolist():
        text = field.decode("ascii").strip()
        if text == "" or (null_text is not None and text == null_text):
            values.append(None)
            continue
        if code == "A":
            values.append(text)
        elif code == "I":
            values.append(int(text))
        else:
            values.append(canon_float(float(text.replace("D", "E").replace("d", "e"))))
    return values


def split_character_fields(values, form: str) -> list:
    match = re.fullmatch(r"(\d+)A(\d+)", form.strip().upper())
    if match is None:
        return values
    total = int(match.group(1))
    width = int(match.group(2))
    if width == 0 or width >= total:
        return values
    split = []
    for item in values:
        text = "" if item is None else str(item).ljust(total)
        split.append([text[index:index + width].rstrip() for index in range(0, total, width)])
    return split


def join_characters(value):
    if isinstance(value, list) and all(isinstance(item, str) and len(item) <= 1 for item in value):
        return "".join(value)
    return value


def apply_null(values, marker: int):
    if isinstance(values, list):
        return [apply_null(item, marker) for item in values]
    if values == marker:
        return None
    return values


def canon_column(values) -> list:
    array = np.array(values, dtype=object) if values.dtype == object else np.asarray(values)
    if isinstance(values, np.ma.MaskedArray):
        rows = []
        for item, masked in zip(values.tolist(), np.array(values.mask).tolist()):
            rows.append(None if masked is True or masked == True else canon_value(item))
        return rows
    return [canon_value(item) for item in array.tolist()]


def canon_array(array: np.ndarray) -> list:
    if isinstance(array, np.ma.MaskedArray):
        return canon_column(array)
    return [canon_value(item) for item in np.asarray(array).ravel(order="C").tolist()]


def canon_value(value):
    if value is None:
        return None
    if isinstance(value, (list, tuple, np.ndarray)):
        return [canon_value(item) for item in np.asarray(value, dtype=object).tolist()]
    if isinstance(value, (bytes, np.bytes_)):
        return value.decode("ascii", "replace").rstrip(" \x00")
    if isinstance(value, (bool, np.bool_)):
        return bool(value)
    if isinstance(value, (complex, np.complexfloating)):
        return [canon_float(value.real), canon_float(value.imag)]
    if isinstance(value, (np.integer, int)) and not isinstance(value, bool):
        return int(value)
    if isinstance(value, (float, np.floating)):
        return canon_float(value)
    if isinstance(value, str):
        return value.rstrip(" \x00")
    return str(value)


def canon_float(value) -> str:
    number = float(value)
    if math.isnan(number):
        return "nan"
    if math.isinf(number):
        return "inf" if number > 0 else "-inf"
    return format(number, ".17g")


def truncate(value) -> str:
    text = repr(value)
    return text if len(text) < 800 else text[:800] + "..."


if __name__ == "__main__":
    main()
