from astropy.io import fits
import numpy as np
from pathlib import Path


def main():

    SAMPLES_DIR = Path.cwd() / "../Samples"
    sample_file_path = SAMPLES_DIR / "sample_1D.fits"

    print(sample_file_path.absolute())

    # Create a sample 1D array of ints FITS file
    data = np.arange(100, dtype=np.int32)
    hdu = fits.PrimaryHDU(data)
    hdu.writeto(sample_file_path, overwrite=True)

    print("Sample FITS file created")


if __name__ == "__main__":
    main()
