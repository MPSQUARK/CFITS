from pathlib import Path

import numpy as np
from astropy.io import fits


output_directory = Path(__file__).resolve().parents[3] / "Benchmarks" / "Data"
output_directory.mkdir(parents=True, exist_ok=True)
data = (np.arange(128 * 128, dtype=np.int16).reshape(128, 128) % 500) - 100
fits.CompImageHDU(data=data, compression_type="RICE_1").writeto(output_directory / "rice.fits", overwrite=True)
fits.CompImageHDU(data=data, compression_type="HCOMPRESS_1").writeto(output_directory / "hcomp.fits", overwrite=True)
