namespace Homestay.DTO.Files;

public sealed record EvidenceFile(Guid Id, string FileName, string ContentType, byte[] Content);
