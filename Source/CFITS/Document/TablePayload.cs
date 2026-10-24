namespace CFITS.Document;

internal readonly record struct TablePayload(FitsHeader Header, byte[] Data);
