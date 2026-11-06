using System.Windows.Media.Imaging;

namespace WpfLearning.ViewModels;

/// <summary>
/// 登録画面に出している画像 1 枚。保存前の撮影分も含む。
/// 画面の縮小表示は <see cref="Preview"/>、保存と拡大は <see cref="Jpeg"/> を使う。
/// </summary>
public sealed class AttachedImage
{
    /// <summary>縮小表示、拡大、削除の操作をまとめて作る。</summary>
    /// <param name="id">画像の主キー。新規撮影のときは新しい Guid。</param>
    /// <param name="jpeg">保存と拡大に使う JPEG。</param>
    /// <param name="preview">登録画面に出す縮小画像。</param>
    /// <param name="open">拡大表示を開く処理。引数は元の JPEG。</param>
    /// <param name="remove">この画像を添付一覧から外す処理。</param>
    public AttachedImage(Guid id, byte[] jpeg, BitmapSource preview, Action<byte[]> open, Action<AttachedImage> remove)
    {
        Id = id;
        Jpeg = jpeg;
        Preview = preview;
        OpenCommand = new RelayCommand(() => open(jpeg));
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    /// <summary>画像の主キー。新規撮影のときは新しい Guid。</summary>
    public Guid Id { get; }

    /// <summary>保存する JPEG。縦横比は撮影時のまま。</summary>
    public byte[] Jpeg { get; }

    /// <summary>登録画面の縮小表示用。幅 320 ピクセルに復号してある。</summary>
    public BitmapSource Preview { get; }

    /// <summary>縮小画像をクリックしたとき、元の JPEG を拡大表示する。</summary>
    public RelayCommand OpenCommand { get; }

    /// <summary>保存前に、この画像を添付一覧から外す。</summary>
    public RelayCommand RemoveCommand { get; }
}
