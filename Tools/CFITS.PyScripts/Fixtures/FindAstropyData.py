import importlib.resources as resources
import sys


print(resources.files("astropy").joinpath(
    "io", "fits", "hdu", "compressed", "tests", "data", sys.argv[1]
))
