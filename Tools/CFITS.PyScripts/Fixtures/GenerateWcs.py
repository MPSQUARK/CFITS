import sys
from pathlib import Path

import numpy as np
from astropy.io import fits
from astropy.wcs import WCS

directory = Path(sys.argv[1])
directory.mkdir(parents=True, exist_ok=True)

def write(name, ctype, crval, cdelt, pc=None):
    w = WCS(naxis=2)
    w.wcs.crpix = [2, 3]
    w.wcs.crval = crval
    w.wcs.cdelt = cdelt
    w.wcs.ctype = ctype
    if pc is not None:
        w.wcs.pc = pc
    pixels = np.array([[2.0, 3.0], [3.0, 3.0], [2.0, 4.0], [4.0, 6.0]])
    world = w.wcs_pix2world(pixels, 1)
    header = w.to_header()
    fits.PrimaryHDU(header=header).writeto(directory / (name + '.fits'), overwrite=True)
    np.savetxt(directory / (name + '.txt'), world, fmt='%.12g')
    np.savetxt(directory / (name + '-pix.txt'), w.wcs_world2pix(world, 1), fmt='%.12g')

write('linear', ['WAVE', 'PIXEL'], [1000.0, 0.0], [0.5, 1.0])
write('tan', ['RA---TAN', 'DEC--TAN'], [83.633, -5.391], [-0.01, 0.01])
write('sin', ['RA---SIN', 'DEC--SIN'], [83.633, -5.391], [-0.01, 0.01])
write('arc', ['RA---ARC', 'DEC--ARC'], [10.0, 20.0], [-0.2, 0.2])
write('zea', ['RA---ZEA', 'DEC--ZEA'], [10.0, 20.0], [-0.2, 0.2])
write('stg', ['RA---STG', 'DEC--STG'], [10.0, 20.0], [-0.2, 0.2])
write('car', ['RA---CAR', 'DEC--CAR'], [10.0, 20.0], [-0.2, 0.2])
write('pc', ['RA---TAN', 'DEC--TAN'], [83.633, -5.391], [-0.01, 0.01], pc=[[0.0, -1.0], [1.0, 0.0]])