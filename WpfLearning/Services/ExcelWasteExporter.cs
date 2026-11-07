using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using WpfLearning.Models;

namespace WpfLearning.Services;

/// <summary>
/// 収集記録を Excel（.xlsx）へ書き出す。1 件につき 1 シート。
/// 登録画面のカード＋入力欄に近いフォーム配置で、フォントはメイリオ。
/// </summary>
public static class ExcelWasteExporter
{
    private const string FontName = "メイリオ";

    private static readonly XLColor PageFill = XLColor.FromHtml("#F3F5F7");
    private static readonly XLColor CardFill = XLColor.White;
    private static readonly XLColor CardBorder = XLColor.FromHtml("#E5E7EB");
    private static readonly XLColor FieldBorder = XLColor.FromHtml("#D1D5DB");
    private static readonly XLColor LabelColor = XLColor.FromHtml("#4B5563");
    private static readonly XLColor TitleColor = XLColor.FromHtml("#111827");

    /// <summary>チェックされた記録を、それぞれ別シートへ書き出して保存する。</summary>
    /// <param name="path">保存先の .xlsx パス。</param>
    /// <param name="records">出力する記録。並びはこの順でシートになる。</param>
    public static void Export(string path, IReadOnlyList<WasteRecord> records)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (records.Count == 0)
            throw new ArgumentException("出力する記録がありません。", nameof(records));

        using var workbook = new XLWorkbook();
        workbook.Style.Font.FontName = FontName;
        workbook.Style.Font.FontSize = 11;

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ClosedXML 0.105 の空ブックにはシートが無いので、記録ごとに Add する。
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            var sheet = workbook.Worksheets.Add(CreateSheetName(record, i + 1, usedNames));
            WriteRecord(sheet, record);
        }

        workbook.SaveAs(path);
        // styles / テーマに残る Calibri・ＭＳ Ｐゴシックをメイリオへ揃える。
        ForceMeiryoFonts(path);
    }

    /// <summary>
    /// 登録画面と同じ並びで 1 件を書き込む。
    /// 外側にカード枠、各入力は枠線付き。収集日とマニフェスト、数量と単位は横並び。
    /// </summary>
    private static void WriteRecord(IXLWorksheet sheet, WasteRecord record)
    {
        // 余白列と、横並び項目のあいだのすき間を取る。
        sheet.Column(1).Width = 3;
        sheet.Column(2).Width = 16;
        sheet.Column(3).Width = 16;
        sheet.Column(4).Width = 3;
        sheet.Column(5).Width = 16;
        sheet.Column(6).Width = 20;
        sheet.Column(7).Width = 3;

        sheet.Style.Font.FontName = FontName;
        sheet.Style.Font.FontSize = 11;
        sheet.Style.Fill.BackgroundColor = PageFill;

        const int cardTop = 2;
        const int cardLeft = 1;
        const int cardRight = 7;
        // 先にカード内を白く塗ってから入力欄を置く（あとから全体へ Fill すると枠線が消える）。
        sheet.Range(cardTop, cardLeft, cardTop + 40, cardRight).Style.Fill.BackgroundColor = CardFill;
        sheet.Range(cardTop, cardLeft, cardTop + 40, cardRight).Style.Font.FontName = FontName;

        var row = cardTop + 1;

        // 見出し
        WriteLabel(sheet, row, 2, "収集記録", TitleColor, fontSize: 14);
        sheet.Range(row, 2, row, 6).Merge();
        sheet.Row(row).Height = 24;
        row += 2;

        // 収集日 | マニフェスト番号
        WriteLabel(sheet, row, 2, "収集日");
        WriteLabel(sheet, row, 5, "マニフェスト番号（任意）");
        row++;
        WriteField(sheet, row, 2, 3, record.CollectedOn.ToString("yyyy/MM/dd"));
        WriteField(
            sheet,
            row,
            5,
            6,
            string.IsNullOrWhiteSpace(record.ManifestNumber) ? "" : record.ManifestNumber);
        row += 2;

        // 排出事業者
        WriteLabel(sheet, row, 2, "排出事業者");
        row++;
        WriteField(sheet, row, 2, 6, record.GeneratorName);
        row += 2;

        // 廃棄物の種類
        WriteLabel(sheet, row, 2, "廃棄物の種類");
        row++;
        WriteField(sheet, row, 2, 6, record.WasteName);
        row += 2;

        // 数量 | 単位
        WriteLabel(sheet, row, 2, "数量");
        WriteLabel(sheet, row, 5, "単位");
        row++;
        WriteField(sheet, row, 2, 3, record.Quantity.ToString("0.###"));
        WriteField(sheet, row, 5, 6, record.Unit);
        row += 2;

        // 処分方法
        WriteLabel(sheet, row, 2, "処分方法");
        row++;
        WriteField(sheet, row, 2, 6, record.DisposalMethod);
        row += 2;

        // 備考
        WriteLabel(sheet, row, 2, "備考");
        row++;
        WriteField(sheet, row, 2, 6, record.Notes, height: 72);
        sheet.Cell(row, 2).Style.Alignment.WrapText = true;
        sheet.Cell(row, 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        row += 2;

        // 画像
        WriteLabel(sheet, row, 2, "画像");
        row++;
        WriteField(sheet, row, 2, 6, $"{record.ImageCount} 枚");
        row += 2;

        // 登録日時
        WriteLabel(sheet, row, 2, "登録日時");
        row++;
        WriteField(sheet, row, 2, 6, record.RegisteredAt.ToString("yyyy/MM/dd HH:mm"));
        row++;

        var cardBottom = row;
        var card = sheet.Range(cardTop, cardLeft, cardBottom, cardRight);
        card.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        card.Style.Border.OutsideBorderColor = CardBorder;

        sheet.Row(cardTop).Height = 12;
        sheet.Row(cardBottom).Height = 12;

        // カード下の仮塗りをページ色へ戻す。
        if (cardBottom < cardTop + 40)
            sheet.Range(cardBottom + 1, cardLeft, cardTop + 40, cardRight).Style.Fill.BackgroundColor = PageFill;
    }

    /// <summary>入力欄の上に置くラベル。</summary>
    private static void WriteLabel(
        IXLWorksheet sheet,
        int row,
        int column,
        string text,
        XLColor? color = null,
        double fontSize = 11)
    {
        var cell = sheet.Cell(row, column);
        cell.Value = text;
        cell.Style.Font.FontName = FontName;
        cell.Style.Font.FontSize = fontSize;
        cell.Style.Font.FontColor = color ?? LabelColor;
        cell.Style.Fill.BackgroundColor = CardFill;
    }

    /// <summary>登録画面の入力欄に見立てた枠付きセル。必要なら横に結合する。</summary>
    private static void WriteField(
        IXLWorksheet sheet,
        int row,
        int fromColumn,
        int toColumn,
        string value,
        double? height = null)
    {
        if (fromColumn != toColumn)
            sheet.Range(row, fromColumn, row, toColumn).Merge();

        var cell = sheet.Cell(row, fromColumn);
        cell.Value = value;

        var range = sheet.Range(row, fromColumn, row, toColumn);
        range.Style.Font.FontName = FontName;
        range.Style.Font.FontSize = 11;
        range.Style.Font.FontColor = TitleColor;
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorderColor = FieldBorder;
        range.Style.Fill.BackgroundColor = CardFill;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.Indent = 1;

        sheet.Row(row).Height = height ?? 24;
    }

    /// <summary>
    /// ClosedXML が残す Calibri / テーマの ＭＳ Ｐゴシックをメイリオへ置き換える。
    /// </summary>
    private static void ForceMeiryoFonts(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        ReplaceInZipEntry(zip, "xl/styles.xml", xml =>
        {
            xml = Regex.Replace(xml, @"<x:name val=""Calibri""\s*/>", @"<x:name val=""メイリオ""/>");
            xml = Regex.Replace(xml, @"<x:name val=""Cambria""\s*/>", @"<x:name val=""メイリオ""/>");
            return xml;
        });
        ReplaceInZipEntry(zip, "xl/theme/theme1.xml", xml =>
            xml.Replace("ＭＳ Ｐゴシック", "メイリオ", StringComparison.Ordinal));
    }

    /// <summary>xlsx 内の 1 エントリを読み、置換して書き戻す。</summary>
    private static void ReplaceInZipEntry(ZipArchive zip, string entryName, Func<string, string> rewrite)
    {
        var entry = zip.GetEntry(entryName);
        if (entry is null)
            return;

        string original;
        using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
            original = reader.ReadToEnd();

        var updated = rewrite(original);
        if (updated == original)
            return;

        entry.Delete();
        var created = zip.CreateEntry(entryName);
        using var writer = new StreamWriter(created.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(updated);
    }

    /// <summary>
    /// Excel のシート名（最大 31 文字、禁止文字なし、重複なし）を作る。
    /// </summary>
    private static string CreateSheetName(WasteRecord record, int index, HashSet<string> usedNames)
    {
        var generator = SanitizeSheetName(record.GeneratorName);
        var baseName = string.IsNullOrEmpty(generator)
            ? $"記録{index}_{record.CollectedOn:yyyyMMdd}"
            : $"{record.CollectedOn:yyyyMMdd}_{generator}";

        baseName = Truncate(baseName, 31);
        if (string.IsNullOrEmpty(baseName))
            baseName = $"記録{index}";

        var name = baseName;
        var suffix = 2;
        while (!usedNames.Add(name))
        {
            var suffixText = $"_{suffix}";
            name = Truncate(baseName, 31 - suffixText.Length) + suffixText;
            suffix++;
        }

        return name;
    }

    /// <summary>シート名に使えない文字を取り除く。</summary>
    private static string SanitizeSheetName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        foreach (var ch in value.Trim())
        {
            if (ch is ':' or '\\' or '/' or '?' or '*' or '[' or ']')
                continue;

            buffer[length++] = ch;
        }

        return new string(buffer[..length]);
    }

    /// <summary>文字列を指定長までに切り詰める。</summary>
    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0)
            return "";

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
