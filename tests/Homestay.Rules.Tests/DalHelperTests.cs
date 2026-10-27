using System.Data;
using Homestay.DAL.Infrastructure;
using Homestay.DTO.Enums;
using Microsoft.Data.SqlClient;

namespace Homestay.Rules.Tests;

public sealed class DalHelperTests
{
    [Fact]
    public void ParametersUseExplicitSqlTypesAndNullValues()
    {
        using var command = new SqlCommand();
        var p = command.Parameters;
        Assert.Equal(SqlDbType.VarChar, p.AddVarChar("@v", 30, "NV0001").SqlDbType);
        Assert.Equal(30, p["@v"].Size);
        Assert.Equal(SqlDbType.NVarChar, p.AddNVarChar("@n", 100, "Đà Lạt").SqlDbType);
        Assert.Equal(SqlDbType.Int, p.AddInt32("@i", 1).SqlDbType);
        Assert.Equal(SqlDbType.BigInt, p.AddInt64("@l", 2L).SqlDbType);
        Assert.Equal(SqlDbType.Decimal, p.AddMoney("@m", 100000).SqlDbType);
        Assert.Equal((byte)18, p["@m"].Precision);
        Assert.Equal((byte)0, p["@m"].Scale);
        p.AddRate("@r", 0.1234m);
        Assert.Equal((byte)5, p["@r"].Precision);
        Assert.Equal((byte)4, p["@r"].Scale);
        Assert.Equal(SqlDbType.Date, p.AddDate("@d", new DateOnly(2026, 10, 27)).SqlDbType);
        Assert.Equal(new DateTime(2026, 10, 27), p["@d"].Value);
        Assert.Equal(SqlDbType.DateTime2, p.AddUtcDateTime("@dt", new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc)).SqlDbType);
        Assert.Equal((byte)3, p["@dt"].Scale);
        Assert.Equal(SqlDbType.Bit, p.AddBoolean("@b", true).SqlDbType);
        Assert.Equal(SqlDbType.UniqueIdentifier, p.AddGuid("@g", Guid.Empty).SqlDbType);
        Assert.Equal(SqlDbType.Binary, p.AddRowVersion("@rv", new byte[8]).SqlDbType);
        Assert.Equal(8, p["@rv"].Size);
        Assert.Equal(SqlDbType.VarBinary, p.AddVarBinary("@bytes", new byte[10]).SqlDbType);
        Assert.Equal(-1, p["@bytes"].Size);
        Assert.Equal("Staff", p.AddEnum<Position>("@enum", 20, Position.Staff).Value);
        Assert.Equal(DBNull.Value, p.AddEnum<Position>("@nullEnum", 20, null).Value);
        Assert.Equal(DBNull.Value, p.AddNVarChar("@nullString", 100, null).Value);
        Assert.Equal(DBNull.Value, p.AddInt32("@nullInt", null).Value);
        Assert.Equal(DBNull.Value, p.AddDate("@nullDate", null).Value);
        Assert.Throws<ArgumentException>(() => p.AddUtcDateTime("@local", DateTime.Now));
        Assert.Throws<ArgumentException>(() => p.AddRowVersion("@short", new byte[7]));
    }

    [Fact]
    public void ReaderMapsByNameAndAssignsUtcKindWithoutLocalConversion()
    {
        using var table = new DataTable();
        foreach (var (name, type) in new[] { ("S", typeof(string)), ("I", typeof(int)), ("L", typeof(long)), ("M", typeof(decimal)), ("B", typeof(bool)), ("G", typeof(Guid)), ("D", typeof(DateTime)), ("RV", typeof(byte[])), ("E", typeof(string)), ("Null", typeof(string)) })
            table.Columns.Add(name, type);
        var guid = Guid.NewGuid();
        var date = new DateTime(2026, 10, 27, 8, 30, 0, DateTimeKind.Unspecified);
        table.Rows.Add("Tiếng Việt", 12, 123L, 0.3m, true, guid, date, new byte[8], "Staff", DBNull.Value);
        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        Assert.Equal("Tiếng Việt", reader.GetString("S"));
        Assert.Equal("Tiếng Việt", reader.GetNullableString("S"));
        Assert.Equal(12, reader.GetInt32("I"));
        Assert.Equal(12, reader.GetNullableInt32("I"));
        Assert.Equal(123L, reader.GetInt64("L"));
        Assert.Equal(0.3m, reader.GetDecimal("M"));
        Assert.Equal(0.3m, reader.GetNullableDecimal("M"));
        Assert.True(reader.GetBoolean("B"));
        Assert.Equal(guid, reader.GetGuid("G"));
        Assert.Equal(new DateOnly(2026, 10, 27), reader.GetDateOnly("D"));
        Assert.Equal(new DateOnly(2026, 10, 27), reader.GetNullableDateOnly("D"));
        var utc = reader.GetUtcDateTime("D");
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(date.Ticks, utc.Ticks);
        Assert.Equal(DateTimeKind.Utc, reader.GetNullableUtcDateTime("D")!.Value.Kind);
        Assert.Equal(new byte[8], reader.GetRowVersion("RV"));
        Assert.Equal(Position.Staff, reader.GetEnum<Position>("E"));
        Assert.Null(reader.GetNullableString("Null"));
        Assert.Null(reader.GetNullableInt32("Null"));
        Assert.Null(reader.GetNullableDecimal("Null"));
        Assert.Null(reader.GetNullableDateOnly("Null"));
        Assert.Null(reader.GetNullableUtcDateTime("Null"));
    }
}
