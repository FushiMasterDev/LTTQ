using System.Data;
using System.Data.Common;

namespace Homestay.DAL.Infrastructure;

/// <summary>Hàm tiện ích đọc cột từ SqlDataReader theo tên, xử lý giá trị NULL, ngày giờ UTC và RowVer.</summary>
public static class DataReaderExtensions
{
    public static string GetString(this IDataRecord reader, string column) => reader.GetString(reader.GetOrdinal(column));
    public static int GetInt32(this IDataRecord reader, string column) => reader.GetInt32(reader.GetOrdinal(column));
    public static long GetInt64(this IDataRecord reader, string column) => reader.GetInt64(reader.GetOrdinal(column));
    public static decimal GetDecimal(this IDataRecord reader, string column) => reader.GetDecimal(reader.GetOrdinal(column));
    public static bool GetBoolean(this IDataRecord reader, string column) => reader.GetBoolean(reader.GetOrdinal(column));
    public static Guid GetGuid(this IDataRecord reader, string column) => reader.GetGuid(reader.GetOrdinal(column));
    public static string? GetNullableString(this DbDataReader reader, string column) => reader.IsDBNull(reader.GetOrdinal(column)) ? null : reader.GetString(column);
    public static int? GetNullableInt32(this DbDataReader reader, string column) => reader.IsDBNull(reader.GetOrdinal(column)) ? null : reader.GetInt32(column);
    public static decimal? GetNullableDecimal(this DbDataReader reader, string column) => reader.IsDBNull(reader.GetOrdinal(column)) ? null : reader.GetDecimal(column);
    public static DateOnly GetDateOnly(this DbDataReader reader, string column) => DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal(column)));
    public static DateOnly? GetNullableDateOnly(this DbDataReader reader, string column) => reader.IsDBNull(reader.GetOrdinal(column)) ? null : reader.GetDateOnly(column);
    public static DateTime GetUtcDateTime(this DbDataReader reader, string column) => DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal(column)), DateTimeKind.Utc);
    public static DateTime? GetNullableUtcDateTime(this DbDataReader reader, string column) => reader.IsDBNull(reader.GetOrdinal(column)) ? null : reader.GetUtcDateTime(column);
    public static byte[] GetRowVersion(this DbDataReader reader, string column) => reader.GetFieldValue<byte[]>(reader.GetOrdinal(column));
    public static T GetEnum<T>(this DbDataReader reader, string column) where T : struct, Enum => Enum.Parse<T>(reader.GetString(column), ignoreCase: false);
}
