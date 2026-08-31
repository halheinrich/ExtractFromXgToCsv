using System.Text.Json;
using BgDataTypes_Lib;
using Bunit;
using ExtractFromXgToCsv.Client.Components;
using ExtractFromXgToCsv.Client.Shared;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;
using Xunit;

namespace ExtractFromXgToCsv.Tests;

/// <summary>
/// Pins the token kind of every enum on the Local-mode wire
/// (halheinrich/backgammon#164, halheinrich/backgammon#37): they cross as
/// declaration names, and an ordinal payload is rejected rather than bound.
///
/// <para>This was a live defect, not a latent one — the roll's only live catch.
/// The panel used to POST through a private options object registering no enum
/// converter, and the server binds with a bare <c>AddControllers()</c> whose
/// stock options register none either, so <see cref="DecisionTypeOption"/>,
/// <see cref="ContactType"/>, <see cref="PositionType"/>,
/// <see cref="PlayType"/>, <see cref="OutputFormat"/> and
/// <see cref="JobPhase"/> all crossed as bare integers. Both ends agreed on the
/// numbering, so it round-tripped and nothing failed — until any of those enums
/// was reordered.</para>
///
/// <para>The fix put strict converters on the types, so neither end configures
/// anything: the panel's own options object is gone entirely. That is what
/// these tests pin — not that the panel is configured right, but that the wire
/// is right with nothing configured.</para>
///
/// <para>Sibling of <see cref="LocalModePanelFilterWireTests"/>, which pins that
/// the filter's values cross intact; this file pins the spelling they cross in.
/// Neither implies the other.</para>
/// </summary>
public class LocalModePanelEnumTokenWireTests : BunitContext
{
    public LocalModePanelEnumTokenWireTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>
    /// The bytes on the wire, asserted directly. Binding hides the question: a
    /// payload of ordinals binds to the same values as a payload of names,
    /// which is exactly why this went unnoticed.
    /// </summary>
    [Fact]
    public async Task RunLocal_EveryEnumCrossesAsAName_NotAnOrdinal()
    {
        var handler = await PostAsync(new FilterConfig
        {
            DecisionType = DecisionTypeOption.CubeOnly,
            ContactTypes = { ContactType.Race },
            PositionTypes = { PositionType.VsTwoPlusUp },
            PlayTypes = { PlayType.Make20Pt },
            IncludeEvaluations = true,
            EvaluationLevels = { AnalysisLevel.Ply3Red },
        });

        var json = handler.CapturedJson!;

        Assert.Contains("\"CubeOnly\"", json);
        Assert.Contains("\"Race\"", json);
        Assert.Contains("\"VsTwoPlusUp\"", json);
        Assert.Contains("\"Make20Pt\"", json);
        Assert.Contains("\"Ply3Red\"", json);
        Assert.Contains("\"DiagramJson\"", json);
    }

    /// <summary>
    /// And the names bind back to the same members through the server's real
    /// options — the strictness costs no legitimate value.
    /// </summary>
    [Fact]
    public async Task RunLocal_NamedTokens_BindBackToTheSameMembers()
    {
        var handler = await PostAsync(new FilterConfig
        {
            DecisionType = DecisionTypeOption.CubeOnly,
            ContactTypes = { ContactType.Race },
            IncludeEvaluations = true,
            EvaluationLevels = { AnalysisLevel.Ply3Red },
        });

        var bound = handler.Captured!;

        Assert.Equal(OutputFormat.DiagramJson, bound.OutputFormat);
        Assert.Equal(DecisionTypeOption.CubeOnly, bound.Filters.DecisionType);
        Assert.Equal([ContactType.Race], bound.Filters.ContactTypes);
        Assert.Equal([AnalysisLevel.Ply3Red], bound.Filters.EvaluationLevels);
    }

    /// <summary>
    /// The server's real failure mode for an ordinal payload: model binding
    /// throws <see cref="JsonException"/>, which ASP.NET Core surfaces as a
    /// 400 — a loud rejection, not a silently-bound wrong member. Bound with
    /// <see cref="JsonSerializerDefaults.Web"/> because that is what a bare
    /// <c>AddControllers()</c> hands the controller.
    /// </summary>
    [Theory]
    [InlineData("{\"OutputFormat\":1}")]
    [InlineData("{\"Filters\":{\"DecisionType\":1}}")]
    [InlineData("{\"Filters\":{\"ContactTypes\":[0]}}")]
    [InlineData("{\"Filters\":{\"PositionTypes\":[0]}}")]
    [InlineData("{\"Filters\":{\"PlayTypes\":[0]}}")]
    [InlineData("{\"Filters\":{\"EvaluationLevels\":[5]}}")]
    public void ServerBinding_OrdinalPayload_IsRejected(string body) =>
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<ProcessRequest>(
                body, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    /// <summary>
    /// The status direction too: <see cref="JobPhase"/> travels server-to-client
    /// on <see cref="ProcessingProgress"/>, so it gets the same treatment.
    /// </summary>
    [Fact]
    public void ProcessingProgress_JobPhase_IsNameOnly()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        Assert.Equal(
            JobPhase.Rendering,
            JsonSerializer.Deserialize<ProcessingProgress>("{\"Phase\":\"Rendering\"}", web)!.Phase);

        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<ProcessingProgress>("{\"Phase\":1}", web));
    }

    /// <summary>
    /// Every member of every wire enum survives a round trip under the server's
    /// options, so the strictness cannot silently drop one as members are added.
    /// </summary>
    [Fact]
    public void EveryWireEnumMember_RoundTripsUnderTheServersOptions()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        AssertRoundTrips<OutputFormat>();
        AssertRoundTrips<JobPhase>();
        AssertRoundTrips<DecisionTypeOption>();
        AssertRoundTrips<ContactType>();
        AssertRoundTrips<PositionType>();
        AssertRoundTrips<PlayType>();
        AssertRoundTrips<AnalysisLevel>();

        void AssertRoundTrips<TEnum>()
            where TEnum : struct, Enum
        {
            foreach (TEnum member in Enum.GetValues<TEnum>())
            {
                var json = JsonSerializer.Serialize(member, web);
                Assert.Equal("\"" + member + "\"", json);
                Assert.Equal(member, JsonSerializer.Deserialize<TEnum>(json, web));
            }
        }
    }

    /// <summary>
    /// Renders the panel with the given config applied, clicks Run, and returns
    /// the handler holding both the raw POST body and what the server would
    /// have bound from it.
    /// </summary>
    private async Task<CapturingProcessHandler> PostAsync(FilterConfig config)
    {
        var handler = new CapturingProcessHandler();
        Services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });

        var cut = Render<LocalModePanel>(p => p
            .Add(c => c.OutputFormat, OutputFormat.DiagramJson)
            .Add(c => c.FilterConfig, config)
            .Add(c => c.FilterApplied, true)
            .Add(c => c.XgpOptions, new XgpExportOptions())
            .Add(c => c.XgpAnonymize, false)
            .Add(c => c.FolderPath, "D:\\xg"));

        bUnitTestHelpers.SetPrivateField(cut.Instance, "_outputPath", "D:\\xg\\decisions.json");
        cut.Render();

        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());

        Assert.NotNull(handler.Captured);
        Assert.NotNull(handler.CapturedJson);
        return handler;
    }
}
