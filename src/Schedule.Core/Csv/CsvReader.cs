using System.Text;

namespace Schedule.Core.Csv;

/// <summary>RFC 4180 兼容的 CSV 解析器（支持引号包裹、字段内换行、双引号转义、分隔符自动探测）。</summary>
public static class CsvReader
{
    private static readonly char[] Candidates = { ',', ';', '\t', '|' };

    /// <summary>根据前若干行中出现次数最一致的分隔符进行探测。</summary>
    public static char DetectDelimiter(string text)
    {
        if (string.IsNullOrEmpty(text)) return ',';

        var lines = text.Split('\n').Take(20).ToList();
        var best = ',';
        var bestScore = -1.0;

        foreach (var candidate in Candidates)
        {
            var counts = new List<int>();
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                counts.Add(CountOutsideQuotes(line, candidate));
            }

            if (counts.Count == 0) continue;
            var positive = counts.Where(c => c > 0).ToList();
            if (positive.Count == 0) continue;

            // 一致性优先：绝大多数行拥有相同的列数
            var mode = positive.GroupBy(c => c).OrderByDescending(g => g.Count()).First();
            var score = mode.Key * 100.0 + mode.Count() * 10.0 - (counts.Count - positive.Count);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private static int CountOutsideQuotes(string line, char delimiter)
    {
        var count = 0;
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
            }
            else if (c == delimiter && !inQuotes)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>解析为行集合；全空行会被丢弃。</summary>
    public static List<string[]> Parse(string text, char delimiter)
    {
        var rows = new List<string[]>();
        if (string.IsNullOrEmpty(text)) return rows;

        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var hasContent = false;

        void EndField()
        {
            row.Add(field.ToString());
            field.Clear();
        }

        void EndRow()
        {
            EndField();
            if (hasContent && row.Any(v => !string.IsNullOrWhiteSpace(v)))
            {
                rows.Add(row.ToArray());
            }
            else if (hasContent && row.Count > 0 && row.Any(v => v.Length > 0))
            {
                rows.Add(row.ToArray());
            }

            row = new List<string>();
            hasContent = false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    if (field.Length == 0)
                    {
                        inQuotes = true;
                        hasContent = true;
                    }
                    else
                    {
                        field.Append(c);
                        hasContent = true;
                    }

                    break;

                case '\r':
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    EndRow();
                    break;

                case '\n':
                    EndRow();
                    break;

                default:
                    if (c == delimiter)
                    {
                        EndField();
                        hasContent = true;
                    }
                    else
                    {
                        field.Append(c);
                        if (!char.IsWhiteSpace(c)) hasContent = true;
                    }

                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            EndRow();
        }

        return rows;
    }
}
