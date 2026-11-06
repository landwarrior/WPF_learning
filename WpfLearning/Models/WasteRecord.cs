using System.Text.Json.Serialization;

namespace WpfLearning.Models;

/// <summary>
/// 収集記録 1 件。一覧の表と詳細は、このオブジェクトのプロパティをそのまま表示する。
/// </summary>
public sealed class WasteRecord
{
    /// <summary>記録の主キー。データベースの WasteRecord.Id と対応する。</summary>
    public Guid Id { get; set; }

    /// <summary>収集日。時刻は持たず、日付だけを使う。</summary>
    public DateTime CollectedOn { get; set; }

    /// <summary>マニフェスト番号。空でも保存できる。入っているときは大文字小文字を区別せず一意。</summary>
    public string ManifestNumber { get; set; } = "";

    /// <summary>排出事業者名。</summary>
    public string GeneratorName { get; set; } = "";

    /// <summary>廃棄物の種類。</summary>
    public string WasteName { get; set; } = "";

    /// <summary>数量。0 より大きい値だけを登録できる。</summary>
    public decimal Quantity { get; set; }

    /// <summary>数量の単位。kg、t、m³、個のいずれか。</summary>
    public string Unit { get; set; } = "";

    /// <summary>処分方法。</summary>
    public string DisposalMethod { get; set; } = "";

    /// <summary>備考。空でもよい。</summary>
    public string Notes { get; set; } = "";

    /// <summary>最初に登録した日時。編集では変えない。</summary>
    public DateTime RegisteredAt { get; set; }

    /// <summary>
    /// 保存するときだけ使う添付画像。一覧の読み込みでは本体を入れず、<see cref="ImageCount"/> だけを埋める。
    /// </summary>
    [JsonIgnore]
    public List<RecordImage> Images { get; set; } = [];

    /// <summary>この記録に付いている画像の枚数。一覧の「画像」列に出す。</summary>
    [JsonIgnore]
    public int ImageCount { get; set; }

    /// <summary>一覧と詳細に出す「数量 単位」の表示用文字列。</summary>
    [JsonIgnore]
    public string QuantityDisplay => $"{Quantity:0.###} {Unit}";

    /// <summary>詳細に出すマニフェスト番号。未入力のときは「（なし）」。</summary>
    [JsonIgnore]
    public string ManifestDisplay =>
        string.IsNullOrWhiteSpace(ManifestNumber)
            ? "マニフェスト番号: （なし）"
            : $"マニフェスト番号: {ManifestNumber}";
}
