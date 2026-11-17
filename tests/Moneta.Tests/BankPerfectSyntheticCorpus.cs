using System.Globalization;
using System.Text;
using Moneta.BankPerfect;

namespace Moneta.Tests;

internal static class BankPerfectSyntheticCorpus
{
    internal static byte[] Create(int count)
    {
        var text = new StringBuilder("""
            utilisateur=Corpus de charge fictif
            guid=moneta-test-load
            compte0=;;;Compte A;;1;€;6,55957;F;;;;;;real;A;
            compte1=;;;Compte B;;1;€;6,55957;F;;;;;;real;B;
            categ=0=Vie;1=     Épicerie;2=     Santé;3=Transport
            modes=-Carte étude
            bp_script_data_fabiobptransfer=_41_42
            """);
        text.Append('\n');
        var firstCount = count / 2;
        var pairs = count / 100;
        for (var account = 0; account < 2; account++)
        {
            text.Append("TNSLPPBNTSOGPTQBC280673");
            var offset = account * firstCount;
            var length = account == 0 ? firstCount : count - firstCount;
            var mode = account == 0 ? 40 : 42;
            var opening = account == 0 ? "Création du compte courant" : "Solde initial";
            text.Append(CultureInfo.InvariantCulture, $"44900;R;{mode};{opening};;-1;1000,00;44900;UId={offset + 1}\n");
            for (var index = 1; index < length; index++)
            {
                var id = offset + index + 1;
                var date = 45000 + index % 400;
                var state = index % 9 == 0 ? "R" : index % 3 == 0 ? "P" : "";
                if (index <= pairs)
                {
                    var sisterAccount = account == 0 ? "B" : "A";
                    var sisterRow = (1 - account) * firstCount + index + 1;
                    var signed = account == 0 ? "-25,00" : "25,00";
                    var transferMode = account == 0 ? 24 : 42;
                    text.Append(CultureInfo.InvariantCulture,
                        $"{date};{state};{transferMode};Virement fictif;Essai lié;-1;{signed};{date};UId={id}<||>#sisterAccUid={sisterAccount}<||>#sisterLineUid={sisterRow}\n");
                }
                else
                {
                    var signed = index % 2 == 0 ? "-12,34" : "15,67";
                    var payment = index % 2 == 0 ? 20 : 40;
                    text.Append(CultureInfo.InvariantCulture,
                        $"{date};{state};{payment};Tiers fictif {index % 83};Essai €;{index % 4};{signed};{date};UId={id}\n");
                }
            }
        }
        return Encode(text.ToString());
    }

    internal static byte[] Encode(string body, char version = '5')
    {
        var encoding = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
        var bytes = encoding.GetBytes($"BP-FILE-{version}" + body);
        for (var index = 0; index < bytes.Length; index++)
        {
            var position = V5Codec.Alphabet.IndexOf(bytes[index]);
            if (position >= 0)
                bytes[index] = V5Codec.Alphabet[(position + bytes.Length +
                    V5Codec.Deltas[(index + 1) % V5Codec.Deltas.Length]) % V5Codec.Alphabet.Length];
        }
        return bytes;
    }
}
