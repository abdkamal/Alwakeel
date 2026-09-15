using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

public sealed partial class ErrorMapperTests
{
    private readonly IErrorMapper _mapper = new ErrorMapper();

    public static IEnumerable<object[]> MappedExceptions()
    {
        yield return [new SqliteException("boom", 11)];
        yield return [new IOException("boom")];
        yield return [new UnauthorizedAccessException("boom")];
        yield return [new ArgumentException("boom")];
        yield return [new InvalidOperationException("boom")]; // falls into the "unknown" bucket
        yield return [new NotSupportedException("boom")]; // also "unknown"
    }

    [Theory]
    [MemberData(nameof(MappedExceptions))]
    public void Map_TextFields_ContainNoDigits(Exception exception)
    {
        var error = _mapper.Map(exception);
        Assert.False(ContainsDigit(error.TitleAr), $"title contains a digit: {error.TitleAr}");
        Assert.False(ContainsDigit(error.MessageAr), $"message contains a digit: {error.MessageAr}");
        Assert.False(ContainsDigit(error.ActionAr), $"action contains a digit: {error.ActionAr}");
    }

    [Theory]
    [MemberData(nameof(MappedExceptions))]
    public void Map_TextFields_ContainNoLatinWords(Exception exception)
    {
        var error = _mapper.Map(exception);
        Assert.False(ContainsLatinLetters(error.TitleAr), $"title contains Latin letters: {error.TitleAr}");
        Assert.False(ContainsLatinLetters(error.MessageAr), $"message contains Latin letters: {error.MessageAr}");
        Assert.False(ContainsLatinLetters(error.ActionAr), $"action contains Latin letters: {error.ActionAr}");
    }

    [Theory]
    [MemberData(nameof(MappedExceptions))]
    public void Map_AlwaysReturnsNonEmptyReference(Exception exception)
    {
        var error = _mapper.Map(exception);
        Assert.False(string.IsNullOrWhiteSpace(error.Reference));
    }

    [Fact]
    public void Map_DifferentCalls_ProduceDifferentReferences()
    {
        var first = _mapper.Map(new InvalidOperationException("a"));
        var second = _mapper.Map(new InvalidOperationException("a"));
        Assert.NotEqual(first.Reference, second.Reference);
    }

    [Fact]
    public void Map_SqliteVsIoVsUnknown_ProduceDistinctTitles()
    {
        var sqlite = _mapper.Map(new SqliteException("boom", 11));
        var io = _mapper.Map(new IOException("boom"));
        var unknown = _mapper.Map(new NotSupportedException("boom"));

        Assert.NotEqual(sqlite.TitleAr, io.TitleAr);
        Assert.NotEqual(sqlite.TitleAr, unknown.TitleAr);
        Assert.NotEqual(io.TitleAr, unknown.TitleAr);
    }

    [Fact]
    public void Map_DbUpdateExceptionWrappingSqliteException_MapsLikeBareSqliteException()
    {
        // EF Core wraps every SaveChanges provider failure in DbUpdateException; the mapper must
        // unwrap it to find the SqliteException instead of falling into the generic bucket.
        var inner = new SqliteException("UNIQUE constraint failed", 19);
        var wrapped = new DbUpdateException("update failed", inner);

        var bare = _mapper.Map(inner);
        var mapped = _mapper.Map(wrapped);

        Assert.Equal(bare.TitleAr, mapped.TitleAr);
        Assert.Equal(bare.MessageAr, mapped.MessageAr);
        Assert.Equal(bare.ActionAr, mapped.ActionAr);
    }

    [Fact]
    public void Map_DoublyWrappedSqliteException_StillMapsLikeBareSqliteException()
    {
        var inner = new SqliteException("boom", 11);
        var wrapped = new DbUpdateException("outer", new DbUpdateException("inner", inner));

        var mapped = _mapper.Map(wrapped);
        var bare = _mapper.Map(inner);

        Assert.Equal(bare.TitleAr, mapped.TitleAr);
    }

    private static bool ContainsDigit(string text) => DigitRegex().IsMatch(text);

    private static bool ContainsLatinLetters(string text) => LatinRegex().IsMatch(text);

    [GeneratedRegex(@"[0-9٠-٩]")]
    private static partial Regex DigitRegex();

    [GeneratedRegex(@"[A-Za-z]")]
    private static partial Regex LatinRegex();
}
