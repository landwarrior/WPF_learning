namespace WpfLearning.Models;

/// <summary>
/// 記録に添付する JPEG 1 枚。データベースの WasteRecordImage 1 行に対応する。
/// </summary>
public sealed class RecordImage
{
    /// <summary>画像の主キー。</summary>
    public Guid Id { get; set; }

    /// <summary>JPEG のバイト列。拡大表示はこの内容を、縮小表示せずに復号する。</summary>
    public byte[] Jpeg { get; set; } = [];
}
