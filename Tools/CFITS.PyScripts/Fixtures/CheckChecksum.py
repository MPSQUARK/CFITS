from pathlib import Path
import sys

from astropy.io import fits


with fits.open(Path(sys.argv[1]), checksum=True) as hdul:
    hdul.verify("exception")
    print(hdul[0].header["DATASUM"])
    print(hdul[0].header["CHECKSUM"])
