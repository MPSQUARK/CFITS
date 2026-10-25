import sys

import numpy as np
from astropy.wcs import WCS


projection = sys.argv[1]
world = WCS(naxis=2)
world.wcs.crpix = [1.0, 1.0]
world.wcs.crval = [30.0, 40.0]
world.wcs.cdelt = [-0.1, 0.1]
world.wcs.ctype = ["RA---" + projection, "DEC--" + projection]
pv = {
    "AZP": [(2, 1, 0.0), (2, 2, 0.0)],
    "CEA": [(2, 1, 1.0)],
    "TAN": [(2, 1, 45.0)],
}.get(projection, [])
world.wcs.set_pv(pv)
value = world.wcs_pix2world(np.array([[2.0, 3.0]]), 1)[0]
print(f"{value[0]:.17g} {value[1]:.17g}")
