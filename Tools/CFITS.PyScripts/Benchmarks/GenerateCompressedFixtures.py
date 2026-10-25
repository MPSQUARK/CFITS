import sys

import numpy as np
from astropy.io import fits

rice_path, hcompress_path = sys.argv[1:3]
data = (np.arange(128 * 128, dtype=np.int16).reshape(128, 128) % 500) - 100
fits.CompImageHDU(data=data, compression_type='RICE_1').writeto(rice_path, overwrite=True)
fits.CompImageHDU(data=data, compression_type='HCOMPRESS_1').writeto(hcompress_path, overwrite=True)
