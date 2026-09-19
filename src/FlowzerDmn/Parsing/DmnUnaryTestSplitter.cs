using System.Text;

namespace FlowzerDmn.Parsing;

/// <summary>
/// Zerlegt einen FEEL-Unary-Test an den Kommas der obersten Ebene.
/// </summary>
/// <remarks>
/// Aus <c>"Fall","Winter"</c> werden zwei Eintraege, aus <c>[1..10], &gt; 20</c> ebenfalls —
/// Kommas innerhalb von Zeichenketten, Klammern, eckigen Klammern und geschweiften Klammern
/// zaehlen nicht. Das reicht fuer <c>inputValues</c> und <c>outputValues</c>; eine vollstaendige
/// FEEL-Grammatik ist hier bewusst nicht nachgebaut, die steckt in der
/// <see cref="Evaluation.IFeelEngine"/>.
/// </remarks>
internal static class DmnUnaryTestSplitter
{
    internal static IReadOnlyList<string> Split(string text)
    {
        var entries = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        var inString = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (inString)
            {
                current.Append(character);
                if (character == '\\' && index + 1 < text.Length)
                {
                    current.Append(text[index + 1]);
                    index++;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    inString = true;
                    current.Append(character);
                    break;
                case '(' or '[' or '{':
                    depth++;
                    current.Append(character);
                    break;
                case ')' or ']' or '}':
                    depth--;
                    current.Append(character);
                    break;
                case ',' when depth == 0:
                    AddEntry(entries, current);
                    break;
                default:
                    current.Append(character);
                    break;
            }
        }

        AddEntry(entries, current);
        return entries;
    }

    private static void AddEntry(List<string> entries, StringBuilder current)
    {
        var entry = current.ToString().Trim();
        current.Clear();

        if (entry.Length > 0)
        {
            entries.Add(entry);
        }
    }
}
