using System.Data;
using Microsoft.Data.SqlClient;

namespace Homestay.DAL.Infrastructure;

public static class SqlParameterExtensions
{
    private static SqlParameter Add(SqlParameterCollection parameters, string name, SqlDbType type, object? value, int size = 0, byte precision = 0, byte scale = 0)
    {
        var parameter = new SqlParameter(name, type) { Value = value ?? DBNull.Value, Size = size, Precision = precision, Scale = scale };
        parameters.Add(parameter);
        return parameter;
    }
    public static SqlParameter AddVarChar(this SqlParameterCollection p, string name, int size, string? value) => Add(p, name, SqlDbType.VarChar, value, size);
    public static SqlParameter AddNVarChar(this SqlParameterCollection p, string name, int size, string? value) => Add(p, name, SqlDbType.NVarChar, value, size);
    public static SqlParameter AddInt32(this SqlParameterCollection p, string name, int? value) => Add(p, name, SqlDbType.Int, value);
    public static SqlParameter AddInt64(this SqlParameterCollection p, string name, long? value) => Add(p, name, SqlDbType.BigInt, value);
    public static SqlParameter AddBoolean(this SqlParameterCollection p, string name, bool? value) => Add(p, name, SqlDbType.Bit, value);
    public static SqlParameter AddGuid(this SqlParameterCollection p, string name, Guid? value) => Add(p, name, SqlDbType.UniqueIdentifier, value);
    public static SqlParameter AddMoney(this SqlParameterCollection p, string name, decimal? value) => Add(p, name, SqlDbType.Decimal, value, precision: 18, scale: 0);
    public static SqlParameter AddRate(this SqlParameterCollection p, string name, decimal? value) => Add(p, name, SqlDbType.Decimal, value, precision: 5, scale: 4);
    public static SqlParameter AddDate(this SqlParameterCollection p, string name, DateOnly? value) => Add(p, name, SqlDbType.Date, value?.ToDateTime(TimeOnly.MinValue));
    public static SqlParameter AddUtcDateTime(this SqlParameterCollection p, string name, DateTime? value)
    {
        if (value is { Kind: not DateTimeKind.Utc }) throw new ArgumentException("UTC DateTime required.", nameof(value));
        return Add(p, name, SqlDbType.DateTime2, value, scale: 3);
    }
    public static SqlParameter AddRowVersion(this SqlParameterCollection p, string name, byte[]? value)
    {
        if (value is not null && value.Length != 8) throw new ArgumentException("Rowversion must contain 8 bytes.", nameof(value));
        return Add(p, name, SqlDbType.Binary, value, 8);
    }
    public static SqlParameter AddVarBinary(this SqlParameterCollection p, string name, byte[]? value) => Add(p, name, SqlDbType.VarBinary, value, -1);
    public static SqlParameter AddEnum<T>(this SqlParameterCollection p, string name, int size, T? value) where T : struct, Enum => p.AddVarChar(name, size, value?.ToString());
}
