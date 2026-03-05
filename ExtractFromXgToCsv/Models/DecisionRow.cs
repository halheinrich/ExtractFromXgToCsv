namespace ExtractFromXgToCsv.Models;

/// <summary>
/// Represents a single checker-play or cube decision extracted from an .xg/.xgp file.
/// Mirrors the DecisionRow record yielded by XgDecisionIterator in ConvertXgToJson_Lib.
/// </summary>
public record DecisionRow(
    string Xgid,
    double Error,
    string MatchScore,
    int MatchLength,
    string Player,
    string Match,
    int Game,
    int MoveNum,
    string Roll,
    int AnalysisDepth,
    double Equity
)
{
    // CSV header matching column order
    public static string CsvHeader =>
        "Xgid,Error,MatchScore,MatchLength,Player,Match,Game,MoveNum,Roll,AnalysisDepth,Equity";

    public string ToCsvLine() =>
        $"\"{Xgid}\",{Error},{MatchScore},{MatchLength},\"{Player}\",\"{Match}\",{Game},{MoveNum},{Roll},{AnalysisDepth},{Equity}";
}
