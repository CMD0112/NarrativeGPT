namespace ChatGPTWrapper.Adventure.Services;

internal enum DiffLineKind
{
    Unchanged,
    Added,
    Removed,
}

internal sealed class DiffLine
{
    public DiffLineKind Kind { get; init; }

    public string Text { get; init; } = "";

    public int? LeftLineNumber { get; init; }

    public int? RightLineNumber { get; init; }
}

internal static class TextDiffService
{
    public static IReadOnlyList<DiffLine> ComputeLineDiff(string leftText, string rightText)
    {
        var leftLines = SplitLines(leftText);
        var rightLines = SplitLines(rightText);
        var lcs = BuildLcsTable(leftLines, rightLines);
        return BacktrackDiff(leftLines, rightLines, lcs);
    }

    public static string FormatUnifiedDiff(
        IReadOnlyList<DiffLine> lines,
        string leftLabel,
        string rightLabel)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"--- {leftLabel}");
        sb.AppendLine($"+++ {rightLabel}");
        foreach (var line in lines)
        {
            var prefix = line.Kind switch
            {
                DiffLineKind.Added => "+",
                DiffLineKind.Removed => "-",
                _ => " ",
            };
            sb.Append(prefix);
            sb.AppendLine(line.Text);
        }

        return sb.ToString();
    }

    private static string[] SplitLines(string text) =>
        string.IsNullOrEmpty(text)
            ? []
            : text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static int[,] BuildLcsTable(string[] left, string[] right)
    {
        var rows = left.Length + 1;
        var cols = right.Length + 1;
        var table = new int[rows, cols];

        for (var i = rows - 2; i >= 0; i--)
        {
            for (var j = cols - 2; j >= 0; j--)
            {
                table[i, j] = string.Equals(left[i], right[j], StringComparison.Ordinal)
                    ? table[i + 1, j + 1] + 1
                    : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        return table;
    }

    private static List<DiffLine> BacktrackDiff(string[] left, string[] right, int[,] lcs)
    {
        var result = new List<DiffLine>();
        var i = 0;
        var j = 0;
        var leftNum = 1;
        var rightNum = 1;

        while (i < left.Length && j < right.Length)
        {
            if (string.Equals(left[i], right[j], StringComparison.Ordinal))
            {
                result.Add(new DiffLine
                {
                    Kind = DiffLineKind.Unchanged,
                    Text = left[i],
                    LeftLineNumber = leftNum++,
                    RightLineNumber = rightNum++,
                });
                i++;
                j++;
            }
            else if (lcs[i + 1, j] >= lcs[i, j + 1])
            {
                result.Add(new DiffLine
                {
                    Kind = DiffLineKind.Removed,
                    Text = left[i],
                    LeftLineNumber = leftNum++,
                });
                i++;
            }
            else
            {
                result.Add(new DiffLine
                {
                    Kind = DiffLineKind.Added,
                    Text = right[j],
                    RightLineNumber = rightNum++,
                });
                j++;
            }
        }

        while (i < left.Length)
        {
            result.Add(new DiffLine
            {
                Kind = DiffLineKind.Removed,
                Text = left[i],
                LeftLineNumber = leftNum++,
            });
            i++;
        }

        while (j < right.Length)
        {
            result.Add(new DiffLine
            {
                Kind = DiffLineKind.Added,
                Text = right[j],
                RightLineNumber = rightNum++,
            });
            j++;
        }

        return result;
    }
}
