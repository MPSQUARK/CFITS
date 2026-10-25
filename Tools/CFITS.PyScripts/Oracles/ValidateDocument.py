import sys

from astropy.io import fits


with fits.open(sys.argv[1], checksum=True) as hdul:
    hdul.verify("exception")
    assert len(hdul) == 2
    assert hdul[0].header["EXPTIME"] == 12.75
    assert hdul[1].header["EXTNAME"] == "EVENTS"
