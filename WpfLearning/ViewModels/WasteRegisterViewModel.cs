using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WpfLearning.Models;
using WpfLearning.Services;

namespace WpfLearning.ViewModels;

/// <summary>
/// 登録画面。新規と編集で同じ入力欄を使い、カメラで撮影した JPEG を添付する。
/// 画像がデータベースに入るのは、登録または変更の保存が成功したときだけ。
/// </summary>
public sealed class WasteRegisterViewModel : PageViewModel
{
    private readonly SqliteWasteStore _store;
    private readonly CameraCapture _camera = new();
    private readonly Action<WasteRecord, bool> _onSaved;
    private readonly Action<string> _reportStatus;
    private readonly Action<byte[]> _showImage;

    private Guid? _editingId;
    private DateTime? _registeredAt;
    private bool _isEditing;
    private bool _cameraBusy;
    private DateTime? _collectedOn;
    private string _manifestNumber = "";
    private string _generatorName = "";
    private string _wasteName = "";
    private string _quantityText = "";
    private string _unit = "";
    private string _disposalMethod = "";
    private string _notes = "";
    private string _collectedOnError = "";
    private string _manifestNumberError = "";
    private string _generatorNameError = "";
    private string _wasteNameError = "";
    private string _quantityError = "";
    private string _unitError = "";
    private string _disposalMethodError = "";
    private string _cameraMessage = "";
    private BitmapSource? _cameraPreview;
    private byte[]? _latestPreviewJpeg;

    /// <summary>空の新規入力で登録画面を用意する。</summary>
    /// <param name="store">記録の読み書きに使う保存先。</param>
    /// <param name="onSaved">保存後に一覧へ戻す処理。第 2 引数が true のときは更新。</param>
    /// <param name="reportStatus">ステータスバーへメッセージを出す処理。</param>
    /// <param name="showImage">JPEG を拡大表示する処理。</param>
    public WasteRegisterViewModel(
        SqliteWasteStore store,
        Action<WasteRecord, bool> onSaved,
        Action<string> reportStatus,
        Action<byte[]> showImage)
    {
        _store = store;
        _onSaved = onSaved;
        _reportStatus = reportStatus;
        _showImage = showImage;
        RegisterCommand = new RelayCommand(Register);
        ClearCommand = new RelayCommand(ClearInput);
        ToggleCameraCommand = new RelayCommand(ToggleCamera, () => !_cameraBusy);
        CaptureCommand = new RelayCommand(Capture, () => IsCameraRunning && !_cameraBusy);
        EnlargePreviewCommand = new RelayCommand(EnlargePreview, () => _latestPreviewJpeg is not null);
        ResetFields();
    }

    /// <summary>既存の記録を編集しているとき true。新規入力のときは false。</summary>
    public bool IsEditing => _isEditing;

    /// <inheritdoc />
    public override string Title => _isEditing ? "収集記録の編集" : "収集記録の登録";

    /// <inheritdoc />
    public override string Description => _isEditing
        ? "一覧で選んだ記録を修正します。カメラで撮影した画像も、この記録に添付できます。"
        : "各入力は ViewModel のプロパティへ双方向バインドされます。カメラで撮影した画像も、一緒に保存します。";

    /// <summary>保存ボタンの文言。編集中は「変更を保存」。</summary>
    public string SubmitButtonText => _isEditing ? "変更を保存" : "登録する";

    /// <summary>クリアボタンの文言。編集中は編集をやめて新規入力へ戻す。</summary>
    public string ClearButtonText => _isEditing ? "新規入力に戻す" : "入力をクリア";

    /// <summary>カメラボタンの文言。起動中は停止になる。</summary>
    public string CameraButtonText => IsCameraRunning ? "カメラを停止" : "カメラを起動";

    /// <summary>カメラが映像を出しているあいだ true。</summary>
    public bool IsCameraRunning => _camera.IsRunning;

    /// <summary>廃棄物の種類の選択肢。</summary>
    public IReadOnlyList<string> WasteNames { get; } =
    [
        "燃え殻",
        "汚泥",
        "廃油",
        "廃酸",
        "廃アルカリ",
        "廃プラスチック類",
        "紙くず",
        "木くず",
        "繊維くず",
        "金属くず",
        "ガラスくず・コンクリートくず・陶磁器くず",
        "がれき類"
    ];

    /// <summary>数量の単位の選択肢。</summary>
    public IReadOnlyList<string> Units { get; } = ["kg", "t", "m³", "個"];

    /// <summary>処分方法の選択肢。</summary>
    public IReadOnlyList<string> DisposalMethods { get; } = ["再生利用", "焼却", "埋立", "中間処理"];

    /// <summary>この入力に添付している画像。保存ボタンを押すまでデータベースには書かない。</summary>
    public ObservableCollection<AttachedImage> Images { get; } = [];

    /// <summary>収集日。</summary>
    public DateTime? CollectedOn
    {
        get => _collectedOn;
        set => SetProperty(ref _collectedOn, value);
    }

    /// <summary>マニフェスト番号。空でもよい。</summary>
    public string ManifestNumber
    {
        get => _manifestNumber;
        set => SetProperty(ref _manifestNumber, value);
    }

    /// <summary>排出事業者名。</summary>
    public string GeneratorName
    {
        get => _generatorName;
        set => SetProperty(ref _generatorName, value);
    }

    /// <summary>廃棄物の種類。</summary>
    public string WasteName
    {
        get => _wasteName;
        set => SetProperty(ref _wasteName, value);
    }

    /// <summary>数量の入力文字列。全角数字も保存時に数値として解釈する。</summary>
    public string QuantityText
    {
        get => _quantityText;
        set => SetProperty(ref _quantityText, value);
    }

    /// <summary>数量の単位。</summary>
    public string Unit
    {
        get => _unit;
        set => SetProperty(ref _unit, value);
    }

    /// <summary>処分方法。</summary>
    public string DisposalMethod
    {
        get => _disposalMethod;
        set => SetProperty(ref _disposalMethod, value);
    }

    /// <summary>備考。空でもよい。</summary>
    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    /// <summary>収集日のエラー。空のときは画面に出さない。</summary>
    public string CollectedOnError
    {
        get => _collectedOnError;
        private set => SetProperty(ref _collectedOnError, value);
    }

    /// <summary>マニフェスト番号のエラー。空のときは画面に出さない。</summary>
    public string ManifestNumberError
    {
        get => _manifestNumberError;
        private set => SetProperty(ref _manifestNumberError, value);
    }

    /// <summary>排出事業者名のエラー。空のときは画面に出さない。</summary>
    public string GeneratorNameError
    {
        get => _generatorNameError;
        private set => SetProperty(ref _generatorNameError, value);
    }

    /// <summary>廃棄物の種類のエラー。空のときは画面に出さない。</summary>
    public string WasteNameError
    {
        get => _wasteNameError;
        private set => SetProperty(ref _wasteNameError, value);
    }

    /// <summary>数量のエラー。空のときは画面に出さない。</summary>
    public string QuantityError
    {
        get => _quantityError;
        private set => SetProperty(ref _quantityError, value);
    }

    /// <summary>単位のエラー。空のときは画面に出さない。</summary>
    public string UnitError
    {
        get => _unitError;
        private set => SetProperty(ref _unitError, value);
    }

    /// <summary>処分方法のエラー。空のときは画面に出さない。</summary>
    public string DisposalMethodError
    {
        get => _disposalMethodError;
        private set => SetProperty(ref _disposalMethodError, value);
    }

    /// <summary>カメラのエラー。空のときは画面に出さない。</summary>
    public string CameraMessage
    {
        get => _cameraMessage;
        private set => SetProperty(ref _cameraMessage, value);
    }

    /// <summary>カメラの縮小プレビュー。起動していないときは null。クリックで元の JPEG を拡大する。</summary>
    public BitmapSource? CameraPreview
    {
        get => _cameraPreview;
        private set => SetProperty(ref _cameraPreview, value);
    }

    /// <summary>入力を確認し、新規なら追加、編集なら同じ行を更新する。</summary>
    public RelayCommand RegisterCommand { get; }

    /// <summary>入力を消す。編集中なら新規入力へ戻す。</summary>
    public RelayCommand ClearCommand { get; }

    /// <summary>カメラを起動または停止する。</summary>
    public RelayCommand ToggleCameraCommand { get; }

    /// <summary>いまの映像を JPEG として添付する。カメラが止まっているときは押せない。</summary>
    public RelayCommand CaptureCommand { get; }

    /// <summary>カメラ映像を、保存前の JPEG のまま拡大表示する。</summary>
    public RelayCommand EnlargePreviewCommand { get; }

    /// <summary>
    /// 既存の記録を入力欄へ読み込む。登録日時は保持し、保存時も変えない。
    /// 画像はデータベースへ書き戻すまで、この画面上の一覧として持つ。
    /// </summary>
    /// <param name="record">入力欄へ読み込む記録。</param>
    /// <param name="images">その記録に付いている JPEG。画面上の添付一覧になる。</param>
    public void BeginEdit(WasteRecord record, IReadOnlyList<RecordImage> images)
    {
        StopCamera();
        _editingId = record.Id;
        _registeredAt = record.RegisteredAt;
        _isEditing = true;
        CollectedOn = DateTime.SpecifyKind(record.CollectedOn.Date, DateTimeKind.Unspecified);
        ManifestNumber = record.ManifestNumber;
        GeneratorName = record.GeneratorName;
        WasteName = record.WasteName;
        QuantityText = record.Quantity.ToString("0.###", CultureInfo.CurrentCulture);
        Unit = record.Unit;
        DisposalMethod = record.DisposalMethod;
        Notes = record.Notes;
        ClearErrors();
        ReplaceImages(images);
        CameraMessage = "";
        CameraPreview = null;
        NotifyMode();
    }

    /// <summary>カメラを止め、編集状態を捨てて、今日の日付が入った新規入力へ戻す。</summary>
    public void BeginNew()
    {
        StopCamera();
        var wasEditing = _isEditing;
        _editingId = null;
        _registeredAt = null;
        _isEditing = false;
        ResetFields();
        Images.Clear();
        CameraMessage = "";
        CameraPreview = null;
        if (wasEditing)
            NotifyMode();
    }

    /// <summary>画面を離れるときにカメラを止める。完了は待たない。</summary>
    public void StopCamera()
    {
        _ = StopCameraAsync();
    }

    /// <summary>入力が正しければ保存し、一覧へ戻す。不正ならフィールドの下にエラーを出す。</summary>
    private void Register()
    {
        try
        {
            if (!TryBuildRecord(out var record))
            {
                _reportStatus("入力内容を確認してください。");
                return;
            }

            var isEditing = IsEditing;
            if (isEditing)
                _store.Update(record);
            else
                _store.Add(record);

            BeginNew();
            _onSaved(record, isEditing);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            _reportStatus(exception.Message);
        }
    }

    private void ClearInput()
    {
        var wasEditing = IsEditing;
        BeginNew();
        _reportStatus(wasEditing ? "新規入力に戻しました。" : "入力をクリアしました。");
    }

    /// <summary>カメラを起動または停止する。許可されていないときは設定の案内を出す。</summary>
    private async void ToggleCamera()
    {
        if (_cameraBusy)
            return;

        _cameraBusy = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            if (_camera.IsRunning)
            {
                await _camera.StopAsync();
                CameraPreview = null;
                CameraMessage = "";
                return;
            }

            CameraMessage = "";
            await _camera.StartAsync(ShowPreview);
        }
        catch (UnauthorizedAccessException)
        {
            CameraMessage = "カメラの使用が許可されていません。Windows の「設定」→「プライバシーとセキュリティ」→「カメラ」で、デスクトップ アプリからのアクセスを許可してください。";
        }
        catch (InvalidOperationException exception)
        {
            CameraMessage = exception.Message;
        }
        catch (Exception exception) when (exception is IOException or COMException)
        {
            CameraMessage = "カメラを起動できませんでした。ほかのアプリがカメラを使っている可能性があります。";
        }
        finally
        {
            _cameraBusy = false;
            OnPropertyChanged(nameof(IsCameraRunning));
            OnPropertyChanged(nameof(CameraButtonText));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>直近のフレームを添付一覧へ加える。データベースへの書き込みは保存ボタンまで行わない。</summary>
    private async void Capture()
    {
        if (_cameraBusy || !_camera.IsRunning)
            return;

        try
        {
            var jpeg = await _camera.CaptureJpegAsync();
            if (jpeg is null || jpeg.Length == 0)
            {
                CameraMessage = "撮影できる映像がまだありません。";
                return;
            }

            Images.Add(CreateAttached(Guid.NewGuid(), jpeg));
            CameraMessage = "";
            _reportStatus("画像を追加しました。保存すると記録に残ります。");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or NotSupportedException or COMException)
        {
            CameraMessage = "画像を添付できませんでした。";
        }
    }

    /// <summary>カメラのスレッドから届いた JPEG を、UI スレッドでプレビューにする。</summary>
    /// <param name="jpeg">直近のフレームを符号化した JPEG。</param>
    private void ShowPreview(byte[] jpeg)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        if (dispatcher.CheckAccess())
            ApplyPreview(jpeg);
        else
            dispatcher.InvokeAsync(() => ApplyPreview(jpeg));
    }

    /// <summary>カメラが動いているときだけ、幅 640 ピクセルのプレビューに差し替える。</summary>
    /// <param name="jpeg">プレビューと、拡大表示に使う JPEG。</param>
    private void ApplyPreview(byte[] jpeg)
    {
        if (!IsCameraRunning)
            return;

        try
        {
            _latestPreviewJpeg = jpeg;
            CameraPreview = ImagePreview.FromJpeg(jpeg, 640);
            CommandManager.InvalidateRequerySuggested();
        }
        catch (NotSupportedException)
        {
            CameraMessage = "カメラの映像を表示できませんでした。";
        }
    }

    private async Task StopCameraAsync()
    {
        try
        {
            await _camera.StopAsync();
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException or COMException or IOException)
        {
        }

        _latestPreviewJpeg = null;
        CameraPreview = null;
        OnPropertyChanged(nameof(IsCameraRunning));
        OnPropertyChanged(nameof(CameraButtonText));
        CommandManager.InvalidateRequerySuggested();
    }

    private void ResetFields()
    {
        CollectedOn = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);
        ManifestNumber = "";
        GeneratorName = "";
        WasteName = "廃プラスチック類";
        QuantityText = "";
        Unit = "kg";
        DisposalMethod = "再生利用";
        Notes = "";
        ClearErrors();
    }

    private void ClearErrors()
    {
        CollectedOnError = "";
        ManifestNumberError = "";
        GeneratorNameError = "";
        WasteNameError = "";
        QuantityError = "";
        UnitError = "";
        DisposalMethodError = "";
    }

    /// <summary>
    /// 入力から保存用の記録を作る。編集中は同じ Id と登録日時を使う。
    /// マニフェスト番号の重複確認では、編集中の記録自身を除外する。
    /// </summary>
    /// <param name="record">入力が正しいときに作った記録。不正なときは使わない。</param>
    private bool TryBuildRecord(out WasteRecord record)
    {
        ClearErrors();
        var valid = true;

        if (CollectedOn is null)
        {
            CollectedOnError = "収集日を入力してください。";
            valid = false;
        }

        if (string.IsNullOrWhiteSpace(GeneratorName))
        {
            GeneratorNameError = "排出事業者名を入力してください。";
            valid = false;
        }

        if (string.IsNullOrWhiteSpace(WasteName))
        {
            WasteNameError = "廃棄物の種類を選択してください。";
            valid = false;
        }

        if (string.IsNullOrWhiteSpace(Unit))
        {
            UnitError = "単位を選択してください。";
            valid = false;
        }

        if (string.IsNullOrWhiteSpace(DisposalMethod))
        {
            DisposalMethodError = "処分方法を選択してください。";
            valid = false;
        }

        var quantity = 0m;
        if (string.IsNullOrWhiteSpace(QuantityText))
        {
            QuantityError = "数量を入力してください。";
            valid = false;
        }
        else if (!TryParseQuantity(QuantityText, out quantity))
        {
            QuantityError = "数量は数値で入力してください。";
            valid = false;
        }
        else if (quantity <= 0)
        {
            QuantityError = "数量は 0 より大きい値にしてください。";
            valid = false;
        }

        var manifestNumber = ManifestNumber.Trim();
        if (manifestNumber.Length > 0 &&
            _store.ManifestNumberExists(manifestNumber, _editingId))
        {
            ManifestNumberError = "同じマニフェスト番号が既に登録されています。";
            valid = false;
        }

        if (!valid || CollectedOn is null)
        {
            record = null!;
            return false;
        }

        record = new WasteRecord
        {
            Id = _editingId ?? Guid.NewGuid(),
            CollectedOn = DateTime.SpecifyKind(CollectedOn.Value.Date, DateTimeKind.Unspecified),
            ManifestNumber = manifestNumber,
            GeneratorName = GeneratorName.Trim(),
            WasteName = WasteName.Trim(),
            Quantity = quantity,
            Unit = Unit,
            DisposalMethod = DisposalMethod,
            Notes = Notes.Trim(),
            RegisteredAt = _registeredAt ?? DateTime.Now,
            Images = Images.Select(image => new RecordImage { Id = image.Id, Jpeg = image.Jpeg }).ToList()
        };
        return true;
    }

    /// <summary>添付一覧を、渡された JPEG で置き換える。表示できない画像は飛ばす。</summary>
    /// <param name="images">編集対象に保存されている画像。</param>
    private void ReplaceImages(IReadOnlyList<RecordImage> images)
    {
        Images.Clear();
        foreach (var image in images)
        {
            try
            {
                Images.Add(CreateAttached(image.Id, image.Jpeg));
            }
            catch (NotSupportedException)
            {
                _reportStatus("読み込めない画像があったため、その 1 枚は表示していません。");
            }
        }
    }

    private void EnlargePreview()
    {
        if (_latestPreviewJpeg is not null)
            _showImage(_latestPreviewJpeg);
    }

    /// <summary>JPEG から、縮小表示と拡大と削除ができる添付画像を作る。</summary>
    /// <param name="id">画像の主キー。</param>
    /// <param name="jpeg">保存と拡大に使う JPEG。</param>
    private AttachedImage CreateAttached(Guid id, byte[] jpeg) =>
        new(id, jpeg, ImagePreview.FromJpeg(jpeg, 320), _showImage, RemoveImage);

    /// <summary>保存前に、添付一覧から 1 枚外す。</summary>
    /// <param name="image">外す画像。</param>
    private void RemoveImage(AttachedImage image) => Images.Remove(image);

    /// <summary>新規と編集の切り替えで、見出しとボタン文言の表示を更新する。</summary>
    private void NotifyMode()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(SubmitButtonText));
        OnPropertyChanged(nameof(ClearButtonText));
    }

    /// <summary>数量の入力を数値にする。現在の文化圏で解釈できなければ、不変文化圏でも試す。</summary>
    /// <param name="text">入力欄の文字列。全角数字を含んでよい。</param>
    /// <param name="quantity">解釈できた数量。失敗したときは 0。</param>
    private static bool TryParseQuantity(string text, out decimal quantity)
    {
        var normalized = NormalizeDigits(text);
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.CurrentCulture, out quantity)
            || decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out quantity);
    }

    /// <summary>全角数字と全角の小数点を、数量として解釈できる半角へ置き換える。</summary>
    /// <param name="text">入力欄の文字列。</param>
    private static string NormalizeDigits(string text)
    {
        var chars = text.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '０' and <= '９')
                chars[i] = (char)('0' + (chars[i] - '０'));
            else if (chars[i] is '．' or '。')
                chars[i] = '.';
        }

        return new string(chars);
    }
}
