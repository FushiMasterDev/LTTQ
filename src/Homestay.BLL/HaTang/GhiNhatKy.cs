using System.Text.Encodings.Web;
using System.Text.Json;
using Homestay.DAL.Infrastructure;

namespace Homestay.BLL.Infrastructure;

/// <summary>Một dòng nhật ký hệ thống: ai làm gì, trên bảng/bản ghi nào, giá trị cũ và mới, ghi chú, máy nào.</summary>
public sealed record AuditEntry(int? UserId, string ActionCode, string TableName, string RecordId, object? OldValues, object? NewValues, string? Note, string MachineName);

/// <summary>Ghi nhật ký hệ thống (AuditLogs) cho mọi thao tác thay đổi dữ liệu quan trọng.</summary>
public sealed class AuditWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Người gọi phải loại mật khẩu/hash/CCCD đầy đủ và dữ liệu nhạy cảm trước khi truyền vào.
    public async Task WriteAsync(UnitOfWork uow, AuditEntry entry, CancellationToken cancellationToken)
    {
        using var command = uow.CreateCommand("""
            INSERT INTO dbo.AuditLogs (UserId, ActionCode, TableName, RecordId, OldValues, NewValues, Note, MachineName)
            VALUES (@userId, @action, @table, @record, @old, @new, @note, @machine);
            """);
        command.Parameters.AddInt32("@userId", entry.UserId);
        command.Parameters.AddVarChar("@action", 50, entry.ActionCode);
        command.Parameters.AddVarChar("@table", 50, entry.TableName);
        command.Parameters.AddVarChar("@record", 50, entry.RecordId);
        command.Parameters.AddNVarChar("@old", -1, entry.OldValues is null ? null : JsonSerializer.Serialize(entry.OldValues, JsonOptions));
        command.Parameters.AddNVarChar("@new", -1, entry.NewValues is null ? null : JsonSerializer.Serialize(entry.NewValues, JsonOptions));
        command.Parameters.AddNVarChar("@note", 500, entry.Note);
        command.Parameters.AddNVarChar("@machine", 100, entry.MachineName);
        await uow.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }
}
