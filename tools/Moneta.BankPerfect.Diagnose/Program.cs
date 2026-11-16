using System.Text.Json;
using System.Text.Json.Serialization;
using Moneta.BankPerfect;

var details = args.Length == 2 && args[0] == "--details";
if ((!details && args.Length != 1) || args[0] is "--help" or "-h" || args[^1].StartsWith("--", StringComparison.Ordinal))
{
    Console.Error.WriteLine("Usage : Moneta.BankPerfect.Diagnose [--details] <fichier.bp>");
    Console.Error.WriteLine("Le rapport JSON par défaut masque les valeurs sources. --details expose les données du modèle.");
    return args.Length == 1 && args[0] is "--help" or "-h" ? 0 : 64;
}

var result = V5Reader.ReadFile(args[^1]);
var options = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter() }
};
using var stdout = Console.OpenStandardOutput();
if (details)
    JsonSerializer.Serialize(stdout, result, options);
else
    JsonSerializer.Serialize(stdout, result.Report, options);
stdout.WriteByte((byte)'\n');
return result.Document is null ? 2 : result.Report.Coverage == V5Coverage.Partial ? 1 : 0;
