using System.Globalization;
using System.Text;
using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public sealed class ElevateResultMetricsService
{
    public ElevateResultMetrics? ReadLatestMetrics(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return null;
        }

        string batchResultsPath = Path.Combine(path, "batch_results.csv");
        if (File.Exists(batchResultsPath) &&
            TryReadMetricsFile(batchResultsPath, TryReadBatchResultsMetrics, out ElevateResultMetrics? batchMetrics))
        {
            return batchMetrics;
        }

        foreach (string csvPath in Directory
            .EnumerateFiles(path, "*_elvx.csv", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            if (TryReadMetricsFile(csvPath, TryReadNamedColumnMetrics, out ElevateResultMetrics? metrics))
            {
                return metrics;
            }
        }

        return null;
    }

    private static bool TryReadMetricsFile(
        string path,
        TryReadMetrics readMetrics,
        out ElevateResultMetrics? metrics)
    {
        try
        {
            return readMetrics(path, out metrics);
        }
        catch (IOException)
        {
            metrics = null;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            metrics = null;
            return false;
        }
    }

    public static string Format(ElevateResultMetrics metrics, CultureInfo culture)
    {
        return string.Format(
            culture,
            "HC5: {0}%   AWT: {1:0.#} s",
            metrics.HandlingCapacityFiveMinute,
            metrics.AverageWaitingTimeSeconds);
    }

    private static bool TryReadBatchResultsMetrics(string path, out ElevateResultMetrics? metrics)
    {
        metrics = null;
        CsvTable table = CsvTable.Load(path);
        List<int> dataRows = [];

        for (int row = 2; row <= table.RowCount; row++)
        {
            if (!string.IsNullOrWhiteSpace(table.Get(row, 1)) ||
                !string.IsNullOrWhiteSpace(table.Get(row, 7)) ||
                !string.IsNullOrWhiteSpace(table.Get(row, 11)))
            {
                dataRows.Add(row);
            }
        }

        for (int index = dataRows.Count - 1; index >= 0; index--)
        {
            int row = dataRows[index];
            if (!TryParseLocalizedDecimal(table.Get(row, 7), out double awt))
            {
                continue;
            }

            metrics = new ElevateResultMetrics(index + 1, awt);
            return true;
        }

        return false;
    }

    private delegate bool TryReadMetrics(string path, out ElevateResultMetrics? metrics);

    private static bool TryReadNamedColumnMetrics(string path, out ElevateResultMetrics? metrics)
    {
        metrics = null;
        CsvTable table = CsvTable.Load(path);

        for (int headerRow = 1; headerRow <= Math.Min(table.RowCount, 20); headerRow++)
        {
            int awtColumn = 0;
            int hcColumn = 0;
            for (int column = 1; column <= table.ColumnCount; column++)
            {
                string header = NormalizeHeader(table.Get(headerRow, column));
                if (awtColumn == 0 && header.Contains("AWT", StringComparison.Ordinal))
                {
                    awtColumn = column;
                }

                if (hcColumn == 0 &&
                    (header.Contains("HC5", StringComparison.Ordinal) ||
                     header.Contains("HANDLINGCAPACITY", StringComparison.Ordinal)))
                {
                    hcColumn = column;
                }
            }

            if (awtColumn == 0)
            {
                continue;
            }

            for (int row = table.RowCount; row > headerRow; row--)
            {
                if (!TryParseLocalizedDecimal(table.Get(row, awtColumn), out double awt))
                {
                    continue;
                }

                int handlingCapacity = row - headerRow;
                if (hcColumn > 0 &&
                    TryParseLocalizedDecimal(table.Get(row, hcColumn), out double parsedHandlingCapacity))
                {
                    handlingCapacity = Math.Max(0, (int)Math.Round(parsedHandlingCapacity));
                }

                metrics = new ElevateResultMetrics(handlingCapacity, awt);
                return true;
            }
        }

        return false;
    }

    private static string NormalizeHeader(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToUpperInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private static bool TryParseLocalizedDecimal(string text, out double result)
    {
        result = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string normalized = text.Trim();
        int lastComma = normalized.LastIndexOf(',');
        int lastDot = normalized.LastIndexOf('.');

        if (lastComma >= 0 && lastDot >= 0)
        {
            char decimalSeparator = lastComma > lastDot ? ',' : '.';
            char groupSeparator = decimalSeparator == ',' ? '.' : ',';
            normalized = normalized.Replace(groupSeparator.ToString(), string.Empty, StringComparison.Ordinal);
        }

        if (normalized.Contains(',', StringComparison.Ordinal))
        {
            if (double.TryParse(normalized, NumberStyles.Any, CultureInfo.GetCultureInfo("ru-RU"), out result))
            {
                return true;
            }

            normalized = normalized.Replace(',', '.');
        }

        return double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }

    private sealed class CsvTable
    {
        private readonly List<string[]> rows;

        private CsvTable(List<string[]> rows, int columnCount)
        {
            this.rows = rows;
            ColumnCount = columnCount;
        }

        public int RowCount => rows.Count;

        public int ColumnCount { get; }

        public static CsvTable Load(string path)
        {
            string[] lines = ReadCsvLines(path);
            char delimiter = DetectDelimiter(lines);
            List<string[]> rows = new(lines.Length);
            int columnCount = 0;

            foreach (string line in lines)
            {
                string[] parsed = ParseCsvLine(line, delimiter);
                rows.Add(parsed);
                columnCount = Math.Max(columnCount, parsed.Length);
            }

            return new CsvTable(rows, columnCount);
        }

        public string Get(int row, int column)
        {
            if (row < 1 || row > rows.Count || column < 1)
            {
                return string.Empty;
            }

            string[] rowData = rows[row - 1];
            return column <= rowData.Length
                ? rowData[column - 1]
                : string.Empty;
        }

        private static char DetectDelimiter(IEnumerable<string> lines)
        {
            int semicolon = 0;
            int comma = 0;
            int tab = 0;

            foreach (string line in lines.Take(20))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                semicolon += CountCharOutsideQuotes(line, ';');
                comma += CountCharOutsideQuotes(line, ',');
                tab += CountCharOutsideQuotes(line, '\t');
            }

            if (tab > semicolon && tab > comma)
            {
                return '\t';
            }

            return semicolon >= comma
                ? ';'
                : ',';
        }

        private static int CountCharOutsideQuotes(string text, char target)
        {
            bool inQuotes = false;
            int count = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char current = text[i];
                if (current == '"')
                {
                    if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        i++;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && current == target)
                {
                    count++;
                }
            }

            return count;
        }

        private static string[] ParseCsvLine(string line, char delimiter)
        {
            List<string> values = [];
            StringBuilder current = new();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (!inQuotes && ch == delimiter)
                {
                    values.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(ch);
            }

            values.Add(current.ToString());
            return values.ToArray();
        }

        private static string[] ReadCsvLines(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Encoding encoding = DetectCsvEncoding(bytes);

            using MemoryStream stream = new(bytes);
            using StreamReader reader = new(stream, encoding, detectEncodingFromByteOrderMarks: true);

            List<string> lines = [];
            while (!reader.EndOfStream)
            {
                lines.Add(reader.ReadLine() ?? string.Empty);
            }

            return lines.ToArray();
        }

        private static Encoding DetectCsvEncoding(byte[] bytes)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            if (HasPrefix(bytes, Encoding.UTF8.GetPreamble()))
            {
                return Encoding.UTF8;
            }

            if (HasPrefix(bytes, Encoding.Unicode.GetPreamble()))
            {
                return Encoding.Unicode;
            }

            if (HasPrefix(bytes, Encoding.BigEndianUnicode.GetPreamble()))
            {
                return Encoding.BigEndianUnicode;
            }

            try
            {
                _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            }
            catch
            {
                return Encoding.GetEncoding(1251);
            }
        }

        private static bool HasPrefix(byte[] bytes, byte[] prefix)
        {
            if (prefix.Length == 0 || bytes.Length < prefix.Length)
            {
                return false;
            }

            for (int i = 0; i < prefix.Length; i++)
            {
                if (bytes[i] != prefix[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
