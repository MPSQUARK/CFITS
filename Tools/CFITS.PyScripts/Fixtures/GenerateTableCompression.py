import gzip, io
import numpy as np
from astropy.io import fits

def gz(data):
    buf = io.BytesIO()
    with gzip.GzipFile(fileobj=buf, mode='wb') as compressor:
        compressor.write(data)
    return np.frombuffer(buf.getvalue(), dtype=np.uint8)

def shuffle(data, width):
    count = len(data) // width
    out = bytearray(len(data))
    for item in range(count):
        for byte in range(width):
            out[(byte * count) + item] = data[(item * width) + byte]
    return bytes(out)

def pad(data):
    extra = (-len(data)) % 2880
    return data + (b' ' * extra if False else bytes(extra))

flux = np.array([1.5, -2.25, 4.0, 8.5], dtype='>f4')
ident = np.array([10, -3, 8, 4], dtype='>i2')
blobs = {'flux': [], 'id': []}
for start in (0, 2):
    raw = flux[start:start + 2].tobytes()
    blobs['flux'].append(gz(shuffle(raw, 4)))
    blobs['id'].append(gz(ident[start:start + 2].tobytes()))
table = fits.BinTableHDU.from_columns([
    fits.Column(name='flux', format='1QB', array=blobs['flux']),
    fits.Column(name='id', format='1QB', array=blobs['id']),
])
table.header['ZTABLE'] = True
table.header['ZNAXIS1'] = 6
table.header['ZNAXIS2'] = 4
table.header['ZPCOUNT'] = 0
table.header['ZFORM1'] = '1E'
table.header['ZFORM2'] = '1I'
table.header['ZCTYP1'] = 'GZIP_2'
table.header['ZCTYP2'] = 'GZIP_1'
table.header['ZTILELEN'] = 2
image = np.array([10, -3, 8, 4], dtype=np.int32)
fits.CompImageHDU(data=image, compression_type='RICE_1').writeto('rice-source.fits', overwrite=True)
with fits.open('rice-source.fits', disable_image_compression=True) as hdul:
    rice = [np.asarray(hdul[1].data['COMPRESSED_DATA'][0], dtype=np.uint8)]
rice_table = fits.BinTableHDU.from_columns([fits.Column(name='value', format='1QB', array=rice)])
rice_table.header['ZTABLE'] = True
rice_table.header['ZNAXIS1'] = 4
rice_table.header['ZNAXIS2'] = 4
rice_table.header['ZPCOUNT'] = 0
rice_table.header['ZFORM1'] = '1J'
rice_table.header['ZCTYP1'] = 'RICE_1'
rice_table.header['ZTILELEN'] = 4
payload_a = bytes(gz(np.array([1, 2], dtype='>i4').tobytes()))
payload_b = bytes(gz(np.array([3], dtype='>i4').tobytes()))
descriptors = len(payload_a).to_bytes(8, 'big') + (0).to_bytes(8, 'big')
descriptors += len(payload_b).to_bytes(8, 'big') + len(payload_a).to_bytes(8, 'big')
originals = (2).to_bytes(8, 'big') + (0).to_bytes(8, 'big') + (1).to_bytes(8, 'big') + (8).to_bytes(8, 'big')
bundle = bytes(gz(descriptors + originals))
heap = payload_a + payload_b + bundle
row = len(bundle).to_bytes(8, 'big') + (len(payload_a) + len(payload_b)).to_bytes(8, 'big')
data = row + heap
cards = [
    "XTENSION= 'BINTABLE'",
    'BITPIX  =                    8',
    'NAXIS   =                    2',
    'NAXIS1  =                   16',
    'NAXIS2  =                    1',
    f'PCOUNT  = {len(heap):20d}',
    'GCOUNT  =                    1',
    'TFIELDS =                    1',
    "TTYPE1  = 'samples'",
    "TFORM1  = '1QB'",
    'ZTABLE  =                    T',
    'ZNAXIS1 =                   16',
    'ZNAXIS2 =                    2',
    'ZPCOUNT =                   16',
    "ZFORM1  = '1QJ'",
    "ZCTYP1  = 'GZIP_1'",
    'ZTILELEN=                    2',
    'THEAP   =                   16',
]
header = b''.join(card.ljust(80).encode('ascii') for card in cards)
header += b'END'.ljust(80)
header += b' ' * ((-len(header)) % 2880)
payload = data + bytes((-len(data)) % 2880)
fits.HDUList([fits.PrimaryHDU(), table, rice_table]).writeto('ztable.fits', overwrite=True)
with open('ztable.fits', 'ab') as handle:
    handle.write(header + payload)