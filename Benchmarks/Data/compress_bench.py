import numpy as np
from astropy.io import fits
data = (np.arange(128 * 128, dtype=np.int16).reshape(128, 128) % 500) - 100
fits.CompImageHDU(data=data, compression_type='RICE_1').writeto(r'D:/Repos/CFITS/Benchmarks/Data/rice.fits', overwrite=True)
fits.CompImageHDU(data=data, compression_type='HCOMPRESS_1').writeto(r'D:/Repos/CFITS/Benchmarks/Data/hcomp.fits', overwrite=True)
