using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace CFITS;

internal readonly record struct TablePayload(FitsHeader Header, byte[] Data);
