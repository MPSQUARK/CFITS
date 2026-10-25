import os
import sys

import numpy as np
from astropy.io import fits


directory = sys.argv[1]
cases = [
    ("gzip1", "GZIP_1", np.arange(35, dtype=np.int16).reshape(5, 7), (3, 2)),
    ("gzip2", "GZIP_2", np.arange(35, dtype=np.int16).reshape(5, 7), (3, 2)),
    ("rice", "RICE_1", np.arange(35, dtype=np.int16).reshape(5, 7), (3, 2)),
    ("hcompress", "HCOMPRESS_1", np.arange(64, dtype=np.int32).reshape(8, 8) - 32, (4, 4)),
    ("plio", "PLIO_1", np.arange(35, dtype=np.int32).reshape(5, 7), (3, 2)),
]
for name, algorithm, image, tile_shape in cases:
    fits.CompImageHDU(
        data=image, compression_type=algorithm, tile_shape=tile_shape
    ).writeto(os.path.join(directory, name + ".fits"), overwrite=True)
