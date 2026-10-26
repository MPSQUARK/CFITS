# Performance Round 3

Status: **evidence-gated**. Changes below are kept only where a differential
test passed and a paired or same-machine measurement showed a real gain.
Release suite: 436 passed, 0 failed.

## Fair comparison

`SharpReadWhole` now reads `BasicHDU.Kernel`. CSharpFITS `Fits.Read` parses
headers and leaves image and table payloads deferred, which is why the old
Firefly read reported 0.24 ms and 181 KB. Two 4096² float maps are 128 MB.
Reading them in 0.24 ms would be about 530 GB/s, above RAM. After the fix,
CSharpFITS takes about 1.07 s and allocates about 1.07 GB. CFITS stays near
one copy of the files (134 MB, about 70 ms).

`SharpWriteWhole` stays a `Read` plus `Write`. `Write` is what pulls the
deferred payload. Touching `Kernel` first closes the input stream and makes
`Write` throw `ObjectDisposedException`. The 512² CSharpFITS image write is
still absent because that library throws `NullReferenceException`.

Astropy's byte column is `tracemalloc` Python-heap growth. Native NumPy
buffers are not in that number. Do not treat it as a managed-allocation
comparison. The column is named `astropy_python_peak_bytes` for that reason.

The pre-change fair read is
`Benchmarks/Results/fits-comparison-round3-read-baseline.csv`.
The current `fits-comparison.csv` is the optimized library on the same harness.

## Accepted

| Area | Change | Evidence |
|---|---|---|
| Endian | `FitsEndian` swaps 16/32/64-bit elements with `Vector128.Shuffle` when SSSE3 or AdvSimd.Arm64 is present, and `BinaryPrimitives` otherwise. Same-width image copies and identity sums use it. | Every `int16` bit pattern, partial vector lengths, and float/double NaN payloads match the scalar primitives. 1,048,576 `int32` values: scalar 2,372,388 ns, vector 703,862 ns, both 24 B. |
| Image planes | Rank-2 integer copies with scale 1, zero 0, and no blank fill the contiguous array. The fallback walks with a running column index. | Domain is non-negative indexes over a validated `n2 * n1` plane. Image and endian tests pass. |
| Owned cards | Parsed cards keep 80-byte slices of the file buffer and copy only when the card is not already a slice. | Header round-trips and the full suite pass. |
| Document write | Data padding uses one shared 2880-byte zero block. `Build` measures the file, writes into that exact buffer, and parses it in place. Image pixels are byte-swapped into the output in chunks instead of a second full encoded buffer. | 512² int16 write: 4,770,840 B → 2,654,304 B and 0.77 ms → 0.56 ms. The floor is the caller's `short[]`, the retained values, the file buffer, and `ToArray`. |
| Snapshots | Adjacent header and payload slices share the file buffer. | Firefly generated read: 268,524,480 B → 134,289,728 B and 82.5 ms → 58.0 ms. |
| Direct images | Uncompressed generated image reads decode into the destination array and drop the file bytes. Rank-2 and rank-3 writes pass the contiguous array through without a flatten copy. Checksum verification and compressed HDUs still use `FitsDocument`. | 256² generated read: 1.16 ms / 276,216 B → 0.124 ms / 150,072 B. 1024²: 5.21 ms / 8,403,128 B → 2.09 ms / 4,213,308 B. Checksum open: 0.276 ms / 81,328 B → 0.043 ms / 52,960 B. |

On those image reads the generated path is faster than the runtime document
sum and allocates about the destination array. The runtime sum does not
allocate a decoded array, so its memory stays near the file size. Both are
at that floor.

## Rejected

| Candidate | Result |
|---|---|
| Direct scalar-table scan | A generated 20,000-row scan went from 0.41 ms and 1,071,456 B through the document bulk copy to 0.61 ms and 1,075,528 B. An SDSS spectrum got faster only because the scan skipped unrelated HDUs. The column decoder itself was not a win, so the type was removed. |
| 50% less memory on a retained image | A 1024² float32 image is 4 MB. Runtime allocation stays about 4.21 MB. Cutting that in half would drop pixels. |
| Chasing Astropy `tracemalloc` | The published Python peak excludes native arrays. CFITS is compared with CSharpFITS managed bytes and with the payload size. |
| Repeating rejected Round 1/2 algorithms | Rice, PLIO, and the checksum rewrite that slowed verification were not reopened. |

## Competitor result on the fair harness

CFITS runtime and the generated image path are faster than CSharpFITS on
every scenario CSharpFITS completes, including the Firefly read that used to
look instantaneous. They also use fewer managed bytes than CSharpFITS on
those rows. Generated whole-file reads now match the runtime's single file
buffer instead of copying every HDU again.

Generated compressed reads still open a `FitsDocument` after a direct probe
rejects the binary table, so GZIP_1 source-gen stays slower than the runtime
sum. That is the fallback, not a second pixel copy.
