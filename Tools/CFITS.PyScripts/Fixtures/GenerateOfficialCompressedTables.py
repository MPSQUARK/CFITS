import gzip
import os
import struct
import sys

import numpy as np
from astropy.io import fits


directory = sys.argv[1]


def card(name, value):
    return (name.ljust(8) + "= " + str(value).rjust(20)).ljust(80).encode("ascii")


def table(path, algorithm, payload, form="1J", descriptors=False):
    width = 16 if form.startswith("1Q") else 8
    body = bytearray()
    heap = bytearray()
    if descriptors:
        chunks = [gzip.compress(struct.pack(">2i", 10, 20)), gzip.compress(struct.pack(">1i", 30))]
        heap = bytearray()
        compressed_descriptors = bytearray()
        original_descriptors = bytearray()
        original_width = width
        for index, chunk in enumerate(chunks):
            compressed_descriptors.extend(len(chunk).to_bytes(8, "big"))
            compressed_descriptors.extend(len(heap).to_bytes(8, "big"))
            heap.extend(chunk)
            original_descriptors.extend((2 if index == 0 else 1).to_bytes(original_width // 2, "big"))
            original_descriptors.extend((0 if index == 0 else 8).to_bytes(original_width // 2, "big"))
        bundle = compressed_descriptors + original_descriptors
        compressed_bundle = gzip.compress(bytes(bundle))
        body.extend(len(compressed_bundle).to_bytes(8, "big") + len(heap).to_bytes(8, "big"))
        heap.extend(compressed_bundle)
        rows = 1
    else:
        body.extend(len(payload).to_bytes(8, "big") + (0).to_bytes(8, "big"))
        heap.extend(payload)
        rows = 1
    cards = [
        card("XTENSION", "'BINTABLE'"), card("BITPIX", 8), card("NAXIS", 2),
        card("NAXIS1", 16), card("NAXIS2", rows), card("PCOUNT", len(heap)),
        card("GCOUNT", 1), card("TFIELDS", 1), card("TTYPE1", "'VALUES'"),
        card("TFORM1", "'1QB'"), card("ZTABLE", "T"),
        card("ZNAXIS1", width if descriptors else 4), card("ZNAXIS2", 2 if descriptors else 4),
        card("ZPCOUNT", len(heap)), card("ZFORM1", "'" + form + "'"),
        card("ZCTYP1", "'" + algorithm + "'"), card("ZTILELEN", 2 if descriptors else 4),
        card("THEAP", 16),
    ]
    header = b"".join(cards + [b"END".ljust(80)])
    header += bytes((-len(header)) % 2880)
    data = bytes(body) + bytes(heap)
    data += bytes((-len(data)) % 2880)
    primary = b"".join([card("SIMPLE", "T"), card("BITPIX", 8), card("NAXIS", 0), b"END".ljust(80)])
    primary += bytes((-len(primary)) % 2880)
    with open(path, "wb") as output:
        output.write(primary + header + data)


values = np.array([10, -3, 8, 4], dtype=">i4").tobytes()
table(os.path.join(directory, "gzip1.fits"), "GZIP_1", gzip.compress(values))
shuffled = bytearray(len(values))
element_count = len(values) // 4
for byte in range(4):
    shuffled[byte * element_count:(byte + 1) * element_count] = values[byte::4]
table(os.path.join(directory, "gzip2.fits"), "GZIP_2", gzip.compress(bytes(shuffled)))
table(os.path.join(directory, "gzip2-plain.fits"), "GZIP_2", gzip.compress(values))

source = fits.CompImageHDU(data=np.array([10, -3, 8, 4], dtype=np.int32), compression_type="RICE_1")
source_path = os.path.join(directory, "rice-source.fits")
fits.HDUList([fits.PrimaryHDU(), source]).writeto(source_path, overwrite=True)
with fits.open(source_path, disable_image_compression=True) as handle:
    rice_payload = bytes(np.asarray(handle[1].data["COMPRESSED_DATA"][0], dtype=np.uint8))
table(os.path.join(directory, "rice.fits"), "RICE_1", rice_payload)
table(os.path.join(directory, "p.fits"), "GZIP_1", b"", "1PJ", True)
table(os.path.join(directory, "q.fits"), "GZIP_1", b"", "1QJ", True)
