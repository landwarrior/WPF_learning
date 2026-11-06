using System.IO;
using System.Windows.Media.Imaging;

namespace WpfLearning.ViewModels;

/// <summary>JPEG のバイト列を、画面にバインドできる画像へ変換する。</summary>
public static class ImagePreview
{
    /// <summary>
    /// JPEG を <see cref="BitmapSource"/> にする。呼び出し元のスレッドで使ってよいよう Freeze する。
    /// </summary>
    /// <param name="jpeg">変換する JPEG。</param>
    /// <param name="decodeWidth">
    /// 縮小表示の幅。null のときは保存されたピクセル数のまま復号し、拡大表示に使う。
    /// 縦横比は変えない。
    /// </param>
    public static BitmapSource FromJpeg(byte[] jpeg, int? decodeWidth = null)
    {
        using var stream = new MemoryStream(jpeg, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        if (decodeWidth is int width)
            image.DecodePixelWidth = width;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
