using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using ExtractFromXgToCsv.Client;
using ExtractFromXgToCsv.Client.Services;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Home's numbering record in browser storage (the umbrella's
/// <c>SPEC-notices.md</c> §7.5): one document, written whole in one write; a
/// read has four outcomes — nothing stored, refused, a record, and a document
/// this version cannot read, kept as it is; and the keys it replaced are read
/// once to migrate — written as the document, and every one removed only once
/// it is stored. The document's text is the test's oracle
/// (<see cref="HomeStoragePlan.NumberingDocument"/>), independent of the
/// serializer.
/// </summary>
public class XgpNumberingStoreTests : BunitContext
{
    private const string Instance = "instance-a";
    private readonly BrowserStoragePlan _storage;
    private readonly XgpNumberingStore _store;

    public XgpNumberingStoreTests()
    {
        Services.AddClientServices();
        _storage = BrowserStoragePlan.On(JSInterop);
        _store = new XgpNumberingStore(Services.GetRequiredService<ReportingBrowserStorage>());
    }

    private static readonly OwedXgpJob Owed =
        new("job-1", Instance, new XgpExportOptions { NamePattern = "p{n}", StartNumber = 5, SuffixLength = 2 }, @"D:\xg\out");

    private static readonly XgpNumberingRecord Full = new()
    {
        Numbering = new XgpNumbering("p{n}", 4, 2),
        Mark = new LedgerMark(Instance, 3),
        Owed = [Owed],
    };

    private static readonly string FullDocument = HomeStoragePlan.NumberingDocument(
        "p{n}", 4, 2, (Instance, 3), HomeStoragePlan.OwedJob("job-1", Instance, "p{n}", 5, 2, @"D:\xg\out"));

    // ── The document, and the four outcomes of a read ───────────────────────

    [Fact]
    public async Task TheRecord_IsWrittenWhole_AsOneDocument_AndReadsBack()
    {
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.NumberingKey, FullDocument, BrowserStorageWriteAnswer.Succeeded);
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.NumberingKey, BrowserStorageReadAnswer.Stored(FullDocument));

        Assert.True(await _store.WriteAsync(Full));
        Assert.Equal(new NumberingRead.Valid(Full), await _store.ReadAsync());
        _storage.Verify();
    }

    [Fact]
    public async Task NothingStored_ReadsAsMissing()
    {
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.NumberingKey, BrowserStorageReadAnswer.Absent);

        Assert.Same(NumberingRead.Missing.Value, await _store.ReadAsync());
        _storage.Verify();
    }

    [Fact]
    public async Task ARefusedRead_ReadsAsRefused_NotAsNothingStored()
    {
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.NumberingKey, BrowserStorageReadAnswer.Refused);

        Assert.Same(NumberingRead.Refused.Value, await _store.ReadAsync());
        _storage.Verify();
    }

    /// <summary>
    /// What cannot be read as this version's document — malformed, another
    /// version, a value out of range — is unreadable, never nothing stored,
    /// and its text is kept as it was stored.
    /// </summary>
    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\":2,\"owed\":[]}")]
    [InlineData("{\"version\":1,\"numbering\":{\"pattern\":\"p{n}\",\"lastNumber\":-1,\"suffixLength\":3},\"owed\":[]}")]
    [InlineData("{\"version\":1,\"owed\":[{\"instance\":\"i\",\"options\":{},\"folder\":\"f\"}]}")]
    [InlineData("{\"version\":1,\"mark\":{\"instance\":\"i\",\"number\":0},\"owed\":[]}")]
    public async Task WhatCannotBeReadAsTheDocument_IsUnreadable_AndKeepsItsText(string stored)
    {
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.NumberingKey, BrowserStorageReadAnswer.Stored(stored));

        Assert.Equal(new NumberingRead.Unreadable(stored), await _store.ReadAsync());
        _storage.Verify();
    }

    [Fact]
    public async Task ADocumentWithoutOwedJobs_ReadsAsOwingNothing()
    {
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.NumberingKey, BrowserStorageReadAnswer.Stored("{\"version\":1}"));

        Assert.Equal(new NumberingRead.Valid(XgpNumberingRecord.Empty), await _store.ReadAsync());
        _storage.Verify();
    }

    // ── Migration ───────────────────────────────────────────────────────────

    /// <summary>
    /// The keys the record replaced are removed only once the document that
    /// holds what they held is stored: a refused write keeps them, to be
    /// migrated again next time, and the page still starts from what they
    /// held.
    /// </summary>
    [Fact]
    public async Task AMigrationWhoseWriteIsRefused_KeepsTheKeys()
    {
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.XgpPatternKey, BrowserStorageReadAnswer.Stored("m{n}"));
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.XgpPrefixKey, BrowserStorageReadAnswer.Absent);
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.XgpLastNumberKey, BrowserStorageReadAnswer.Stored("7"));
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.XgpSuffixLengthKey, BrowserStorageReadAnswer.Absent);
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.NumberingKey,
            HomeStoragePlan.NumberingDocument("m{n}", 7, 3), BrowserStorageWriteAnswer.Refused);

        var (record, stored) = (await _store.MigrateAsync())!.Value;

        Assert.Equal(new XgpNumbering("m{n}", 7, 3), record.Numbering);
        Assert.False(stored);
        _storage.Verify();
    }

    /// <summary>
    /// A legacy prefix that a pattern superseded holds nothing the document
    /// needs, and is removed with the other keys once the document is stored.
    /// </summary>
    [Fact]
    public async Task APrefixAPatternSuperseded_IsRemovedWithTheOtherKeys()
    {
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.XgpPatternKey, BrowserStorageReadAnswer.Stored("m{n}"));
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.XgpPrefixKey, BrowserStorageReadAnswer.Stored("old"));
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.XgpLastNumberKey, BrowserStorageReadAnswer.Stored("7"));
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.XgpSuffixLengthKey, BrowserStorageReadAnswer.Absent);
        _storage.ExpectWrite(BrowserStorageArea.Local, HomeStoragePlan.NumberingKey,
            HomeStoragePlan.NumberingDocument("m{n}", 7, 3), BrowserStorageWriteAnswer.Succeeded);
        _storage.ExpectRemove(BrowserStorageArea.Local, HomeStoragePlan.XgpPatternKey, BrowserStorageWriteAnswer.Succeeded);
        _storage.ExpectRemove(BrowserStorageArea.Local, HomeStoragePlan.XgpPrefixKey, BrowserStorageWriteAnswer.Succeeded);
        _storage.ExpectRemove(BrowserStorageArea.Local, HomeStoragePlan.XgpLastNumberKey, BrowserStorageWriteAnswer.Succeeded);

        var (record, stored) = (await _store.MigrateAsync())!.Value;

        Assert.Equal(new XgpNumbering("m{n}", 7, 3), record.Numbering);
        Assert.True(stored);
        _storage.Verify();
    }

    [Fact]
    public async Task NothingInTheReplacedKeys_MigratesNothing_AndWritesNothing()
    {
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.XgpPatternKey, BrowserStorageReadAnswer.Absent);
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.XgpPrefixKey, BrowserStorageReadAnswer.Absent);
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.XgpLastNumberKey, BrowserStorageReadAnswer.Absent);
        _storage.ExpectRead(BrowserStorageArea.Local, HomeStoragePlan.XgpSuffixLengthKey, BrowserStorageReadAnswer.Absent);

        Assert.Null(await _store.MigrateAsync());
        _storage.Verify();
    }
}
