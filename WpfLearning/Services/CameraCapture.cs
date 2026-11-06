using System.IO;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Storage.Streams;

namespace WpfLearning.Services;

/// <summary>
/// PC のカメラを起動し、表示用と添付用の JPEG を渡す。
/// 映像は幅 1280 ピクセル以下で最大の形式を優先し、長辺が 1600 ピクセルを超える JPEG は 1600 に縮小する。
/// 縦横比はカメラの映像のまま保つ。
/// </summary>
public sealed class CameraCapture
{
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly object _latestGate = new();
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private Action<byte[]>? _onPreview;
    private byte[]? _latestJpeg;
    /// <summary>停止の要求ごとに増やす。起動完了より先に停止されたら、その起動は採用しない。</summary>
    private int _generation;
    private int _frameBusy;
    private long _nextPreviewAt;
    private volatile bool _running;

    /// <summary>カメラが映像を受け取っているあいだ true。</summary>
    public bool IsRunning => _running;

    /// <summary>
    /// カメラを起動し、プレビュー用 JPEG をおよそ 0.2 秒ごとに渡す。
    /// カメラが無い、または使用が許可されていないときは例外になる。
    /// </summary>
    /// <param name="onPreview">プレビュー用 JPEG を受け取る処理。停止後に呼ばれることはない。</param>
    public async Task StartAsync(Action<byte[]> onPreview)
    {
        var generation = _generation;
        await _operation.WaitAsync();
        try
        {
            if (generation != _generation)
                return;

            await StopCoreAsync();
            _onPreview = onPreview;
            _capture = new MediaCapture();
            try
            {
                await _capture.InitializeAsync(new MediaCaptureInitializationSettings
                {
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu
                });
                var source = ChooseSource(_capture);
                await PreferSmallerFormatAsync(source);
                _reader = await _capture.CreateFrameReaderAsync(source);
                _reader.FrameArrived += OnFrameArrived;
                var status = await _reader.StartAsync();
                if (status != MediaFrameReaderStartStatus.Success)
                    throw new InvalidOperationException("カメラを起動できませんでした。");
            }
            catch (UnauthorizedAccessException)
            {
                await StopCoreAsync();
                throw;
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or COMException)
            {
                await StopCoreAsync();
                if (exception is InvalidOperationException)
                    throw;
                throw new InvalidOperationException("カメラを起動できませんでした。ほかのアプリがカメラを使っている可能性があります。", exception);
            }

            if (generation != _generation)
            {
                await StopCoreAsync();
                return;
            }

            _running = true;
        }
        finally
        {
            _operation.Release();
        }
    }

    /// <summary>直近のプレビューフレームを JPEG で返す。まだ 1 枚も来ていなければ null。</summary>
    public Task<byte[]?> CaptureJpegAsync()
    {
        lock (_latestGate)
            return Task.FromResult(_latestJpeg is null ? null : _latestJpeg.ToArray());
    }

    /// <summary>カメラを止める。起動の途中でも、完了後に映像を残さない。</summary>
    public async Task StopAsync()
    {
        Interlocked.Increment(ref _generation);
        _running = false;
        _onPreview = null;
        await _operation.WaitAsync();
        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _operation.Release();
        }
    }

    /// <summary>フレーム受信を解除し、カメラと直近の JPEG を破棄する。</summary>
    private async Task StopCoreAsync()
    {
        _running = false;
        var reader = _reader;
        _reader = null;
        if (reader is not null)
        {
            reader.FrameArrived -= OnFrameArrived;
            try
            {
                await reader.StopAsync();
            }
            catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException or COMException)
            {
            }

            reader.Dispose();
        }

        _capture?.Dispose();
        _capture = null;
        lock (_latestGate)
            _latestJpeg = null;
    }

    /// <summary>
    /// フレームを JPEG にしてプレビューへ渡す。処理中のフレームがあるときは新しいフレームを捨て、
    /// 1 フレームの失敗ではカメラを止めない。
    /// </summary>
    /// <param name="sender">フレームを受け取ったリーダー。</param>
    /// <param name="args">到着通知。中身は使わず、最新フレームを取得し直す。</param>
    private async void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (!_running || Environment.TickCount64 < _nextPreviewAt)
            return;
        if (Interlocked.Exchange(ref _frameBusy, 1) == 1)
            return;

        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            var video = frame?.VideoMediaFrame;
            if (video is null)
                return;

            SoftwareBitmap? bitmap = video.SoftwareBitmap;
            SoftwareBitmap? owned = null;
            if (bitmap is null && video.Direct3DSurface is not null)
            {
                owned = await SoftwareBitmap.CreateCopyFromSurfaceAsync(video.Direct3DSurface);
                bitmap = owned;
            }

            if (bitmap is null || !_running)
                return;

            try
            {
                var jpeg = await EncodeJpegAsync(bitmap);
                if (!_running)
                    return;

                lock (_latestGate)
                    _latestJpeg = jpeg;
                _nextPreviewAt = Environment.TickCount64 + 200;
                _onPreview?.Invoke(jpeg);
            }
            finally
            {
                owned?.Dispose();
            }
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException or COMException or IOException)
        {
        }
        finally
        {
            Interlocked.Exchange(ref _frameBusy, 0);
        }
    }

    /// <summary>カラー映像を優先し、無ければ最初に見つかった映像を使う。</summary>
    /// <param name="capture">初期化済みのカメラ。</param>
    private static MediaFrameSource ChooseSource(MediaCapture capture)
    {
        var sources = capture.FrameSources.Values;
        return sources.FirstOrDefault(source => source.Info.SourceKind == MediaFrameSourceKind.Color)
            ?? sources.FirstOrDefault()
            ?? throw new InvalidOperationException("カメラが見つかりませんでした。");
    }

    /// <summary>幅 1280 ピクセル以下で最大の形式があれば、それに切り替える。失敗しても既定の形式のまま続ける。</summary>
    /// <param name="source">形式を選ぶ映像ソース。</param>
    private static async Task PreferSmallerFormatAsync(MediaFrameSource source)
    {
        var format = source.SupportedFormats
            .Where(item => item.VideoFormat is { Width: > 0 and <= 1280 })
            .OrderByDescending(item => item.VideoFormat.Width)
            .FirstOrDefault();
        if (format is null)
            return;

        try
        {
            await source.SetFormatAsync(format);
        }
        catch (Exception exception) when (exception is InvalidOperationException or COMException)
        {
        }
    }

    /// <summary>フレームを JPEG にする。長辺が 1600 ピクセルを超えるときだけ、比率を保って縮小する。</summary>
    /// <param name="source">カメラから受け取った 1 フレーム。</param>
    private static async Task<byte[]> EncodeJpegAsync(SoftwareBitmap source)
    {
        using var converted = SoftwareBitmap.Convert(source, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var longEdge = Math.Max(converted.PixelWidth, converted.PixelHeight);
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
        encoder.SetSoftwareBitmap(converted);
        if (longEdge > 1600)
        {
            var scale = 1600d / longEdge;
            encoder.BitmapTransform.ScaledWidth = Even(converted.PixelWidth * scale);
            encoder.BitmapTransform.ScaledHeight = Even(converted.PixelHeight * scale);
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        }

        await encoder.FlushAsync();
        stream.Seek(0);
        var size = (uint)stream.Size;
        var bytes = new byte[size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync(size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    /// <summary>エンコーダが奇数の辺を拒むことがあるため、縮小後の辺を偶数にする。</summary>
    /// <param name="value">縮小後の辺の長さ。</param>
    private static uint Even(double value)
    {
        var rounded = (uint)Math.Max(2, Math.Round(value));
        return rounded % 2 == 0 ? rounded : rounded - 1;
    }
}
