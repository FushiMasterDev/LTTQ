namespace Homestay.DTO.Files;

/// <summary>Một tập tin chứng từ đã lưu (tên, loại, nội dung) để xem lại.</summary>
public sealed record EvidenceFile(Guid Id, string FileName, string ContentType, byte[] Content);
