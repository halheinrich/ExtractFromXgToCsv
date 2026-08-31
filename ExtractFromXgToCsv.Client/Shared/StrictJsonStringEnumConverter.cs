using System.Text.Json.Serialization;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// The string-token-exact enum converter: a
/// <see cref="JsonStringEnumConverter{TEnum}"/> that rejects numeric tokens on
/// read (and refuses to write an undefined value as a number), for
/// attribute-form registration — where the base type's
/// <c>allowIntegerValues: false</c> knob is otherwise unreachable, because an
/// attribute can only name a converter type, not pass it constructor arguments.
///
/// <para>Bundled onto this app's own wire enums so the guarantee lives on the
/// type rather than in one particular <see cref="System.Text.Json.JsonSerializerOptions"/>.
/// That is what lets both ends of the local-mode wire be configuration-free:
/// the client posts with <c>HttpClient</c>'s stock options and the server binds
/// with a bare <c>AddControllers()</c>, and the enums still cross as names
/// because the types say so. Before this, they crossed as bare integer
/// ordinals — both ends agreed, so it worked, and it would have silently
/// rebound on any reorder (halheinrich/backgammon#164,
/// halheinrich/backgammon#37).</para>
///
/// <para>Deliberately no naming policy: the declared name is the token, matching
/// how <c>XgFilter_Lib</c>'s filter enums and <c>BgDataTypes_Lib</c>'s
/// <c>AnalysisLevel</c> cross the same wire, so one payload has one spelling
/// convention throughout. Name matching on read stays case-insensitive — the
/// base converter's behavior, which has no knob — so the strictness closed here
/// is token kind, not case.</para>
/// </summary>
/// <typeparam name="TEnum">The enum type the converter handles.</typeparam>
public sealed class StrictJsonStringEnumConverter<TEnum> : JsonStringEnumConverter<TEnum>
    where TEnum : struct, Enum
{
    /// <summary>Creates the converter; attribute-form registration uses this.</summary>
    public StrictJsonStringEnumConverter()
        : base(namingPolicy: null, allowIntegerValues: false)
    {
    }
}
