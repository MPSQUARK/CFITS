"""Time the CFITS benchmark files with Astropy and write a CSV."""

import statistics
import sys
import time
import gc
import tracemalloc
from io import BytesIO
from pathlib import Path

import numpy as np
from astropy.io import fits
from astropy.wcs import WCS

WARMUP = 2
ITERATIONS = 6
MINIMUM_SECONDS = 0.1


def measure(action):
    for _ in range(WARMUP):
        action()
    samples = []
    for _ in range(ITERATIONS):
        count = 0
        started = time.perf_counter()
        while True:
            action()
            count += 1
            elapsed = time.perf_counter() - started
            if elapsed >= MINIMUM_SECONDS:
                break
        samples.append(elapsed / count * 1000.0)
    return statistics.mean(samples), statistics.stdev(samples)


def measure_memory(action):
    samples = []
    tracemalloc.start()
    try:
        for _ in range(2):
            action()
        for _ in range(6):
            gc.collect()
            tracemalloc.reset_peak()
            baseline, _ = tracemalloc.get_traced_memory()
            action()
            _, peak = tracemalloc.get_traced_memory()
            samples.append(float(max(0, peak - baseline)))
    finally:
        tracemalloc.stop()
    return statistics.mean(samples), statistics.stdev(samples)


def image_sum(path, hdu=0):
    with fits.open(path) as hdul:
        return float(np.nansum(hdul[hdu].data))


def column_sum(path, name):
    with fits.open(path) as hdul:
        return float(np.nansum(hdul[1].data[name]))


def heap_sum(path):
    with fits.open(path) as hdul:
        total = 0.0
        for row in hdul[1].data["samples"]:
            total += float(np.sum(row))
        return total


def image_sum_checked(path):
    with fits.open(path, checksum=True) as hdul:
        hdul.verify("exception")
        return float(np.nansum(hdul[0].data))


def write_image():
    data = (np.arange(512 * 512, dtype=np.int16).reshape(512, 512) % 1000)
    buffer = BytesIO()
    fits.PrimaryHDU(data).writeto(buffer)
    return buffer.tell()


def main():
    directory = Path(sys.argv[1])
    sdss = Path(sys.argv[2])
    destination = Path(sys.argv[3])
    world = WCS(naxis=2)
    world.wcs.crpix = [128, 128]
    world.wcs.crval = [83.633, -5.391]
    world.wcs.cdelt = [-0.01, 0.01]
    world.wcs.ctype = ["RA---TAN", "DEC--TAN"]
    pixels = np.column_stack(
        [np.arange(100_000) % 400 + 1, np.arange(100_000) // 400 + 1]
    ).astype(float)

    scenarios = [
        ("open_tiny", lambda: image_sum(directory / "tiny.fits")),
        ("read_image256", lambda: image_sum(directory / "image256.fits")),
        ("read_image1024", lambda: image_sum(directory / "image1024.fits")),
        ("scan_binary", lambda: column_sum(directory / "bintable.fits", "flux")),
        ("read_ascii", lambda: column_sum(directory / "ascii.fits", "value")),
        ("read_heap", lambda: heap_sum(directory / "heap.fits")),
        ("write_image", write_image),
        ("decompress_gzip", lambda: image_sum(directory / "gzip.fits", 1)),
        ("decompress_gzip2", lambda: image_sum(directory / "gzip2.fits", 1)),
        ("decompress_rice", lambda: image_sum(directory / "rice.fits", 1)),
        ("decompress_hcomp", lambda: image_sum(directory / "hcomp.fits", 1)),
        ("open_checksum", lambda: image_sum_checked(directory / "checksum.fits")),
        ("tan_wcs", lambda: float(np.nansum(world.wcs_pix2world(pixels, 1)))),
    ]
    if sdss.exists():
        scenarios.append(("read_sdss", lambda: column_sum(sdss, "flux")))

    # tracemalloc reports Python-managed peak growth; NumPy/Astropy native buffers
    # are not included and are intentionally labeled as such in the output.
    lines = ["key,mean_ms,stddev_ms,python_peak_bytes,python_peak_stddev_bytes"]
    for key, action in scenarios:
        print(key, flush=True)
        try:
            mean, deviation = measure(action)
            allocated, allocated_deviation = measure_memory(action)
            lines.append(
                f"{key},{mean:.17g},{deviation:.17g},"
                f"{allocated:.17g},{allocated_deviation:.17g}"
            )
        except Exception as error:
            print(f"  unsupported: {error}", flush=True)
            lines.append(f"{key},unsupported,,,")
    destination.write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
