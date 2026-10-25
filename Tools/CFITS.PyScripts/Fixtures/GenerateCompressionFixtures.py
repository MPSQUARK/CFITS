import sys
from pathlib import Path

import numpy as np
from astropy.io import fits

directory = Path(sys.argv[1])
directory.mkdir(parents=True, exist_ok=True)
images = {
    'rice': np.arange(64, dtype=np.int16).reshape(8, 8),
    'gzip': np.arange(48, dtype=np.int32).reshape(6, 8),
    'gzip2': np.array([1.5, np.nan, -2.25, 4], dtype=np.float32).reshape(2, 2),
    'hcomp': np.arange(42, dtype=np.int16).reshape(6, 7) - 20,
}
for name, data in images.items():
    kind = {'rice': 'RICE_1', 'gzip': 'GZIP_1', 'gzip2': 'GZIP_2', 'hcomp': 'HCOMPRESS_1'}[name]
    fits.CompImageHDU(data=data, compression_type=kind).writeto(directory / f'{name}.fits', overwrite=True)
smooth = (np.arange(64, dtype=np.int16).reshape(8, 8) * 10)
comp = fits.CompImageHDU(data=smooth, compression_type='HCOMPRESS_1', hcomp_scale=4, hcomp_smooth=True)
comp.writeto(directory / 'hcomp-smooth.fits', overwrite=True)
with fits.open(directory / 'hcomp-smooth.fits') as hdul:
    np.savetxt(directory / 'hcomp-smooth.txt', hdul[1].data.reshape(-1), fmt='%d')
tiled = np.arange(35, dtype=np.int16).reshape(5, 7)
fits.CompImageHDU(data=tiled, compression_type='GZIP_1', tile_shape=(3, 2)).writeto(directory / 'tiled.fits', overwrite=True)