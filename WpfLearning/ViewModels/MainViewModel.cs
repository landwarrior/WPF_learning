using System.Windows.Media.Imaging;
using WpfLearning.Models;
using WpfLearning.Services;

namespace WpfLearning.ViewModels;

/// <summary>
/// ウィンドウ全体の状態。いま出している画面を <see cref="CurrentViewModel"/> で持つ。
/// 一覧と登録は起動時に 1 つずつ作り、画面を切り替えても入力の途中経過を捨てない。
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private PageViewModel _current;
    private string _statusText = "";
    private BitmapSource? _enlargedImage;

    /// <summary>一覧・登録・Excel 出力を同じ保存先でつなぎ、最初は一覧を出す。</summary>
    /// <param name="store">記録の読み書きに使う保存先。</param>
    public MainViewModel(SqliteWasteStore store)
    {
        StorePath = store.FilePath;
        CloseEnlargedCommand = new RelayCommand(CloseEnlarged);
        List = new WasteListViewModel(store, SetStatus, BeginEdit, ShowEnlarged);
        Register = new WasteRegisterViewModel(store, OnSaved, SetStatus, ShowEnlarged);
        Export = new WasteExportViewModel(store, SetStatus);
        _current = List;
    }

    /// <summary>一覧画面。アプリの生存中は同じインスタンスを使う。</summary>
    public WasteListViewModel List { get; }

    /// <summary>登録と編集の画面。アプリの生存中は同じインスタンスを使う。</summary>
    public WasteRegisterViewModel Register { get; }

    /// <summary>Excel 出力画面。アプリの生存中は同じインスタンスを使う。</summary>
    public WasteExportViewModel Export { get; }

    /// <summary>データベースファイルの絶対パス。ステータスバーの保存先に出す。</summary>
    public string StorePath { get; }

    /// <summary>
    /// 中央に出している画面。一覧へ移るときはカメラを止め、拡大表示も閉じる。
    /// </summary>
    public PageViewModel CurrentViewModel
    {
        get => _current;
        private set
        {
            if (!SetProperty(ref _current, value))
                return;

            if (!ReferenceEquals(_current, Register))
                Register.StopCamera();

            CloseEnlarged();

            OnPropertyChanged(nameof(IsListSelected));
            OnPropertyChanged(nameof(IsRegisterSelected));
            OnPropertyChanged(nameof(IsExportSelected));
        }
    }

    /// <summary>左メニューの「一覧」が選ばれているか。</summary>
    public bool IsListSelected
    {
        get => ReferenceEquals(CurrentViewModel, List);
        set
        {
            if (value)
                CurrentViewModel = List;
        }
    }

    /// <summary>
    /// 左メニューの「新規登録」が選ばれているか。
    /// 編集中の画面から一覧へ戻ったあと、もう一度「新規登録」を選ぶと入力を新しい記録へ戻す。
    /// 登録画面にいるあいだにラジオボタンが再通知されても、編集中の内容は消さない。
    /// </summary>
    public bool IsRegisterSelected
    {
        get => ReferenceEquals(CurrentViewModel, Register);
        set
        {
            if (!value)
                return;

            if (Register.IsEditing && !ReferenceEquals(CurrentViewModel, Register))
                Register.BeginNew();

            CurrentViewModel = Register;
        }
    }

    /// <summary>
    /// 左メニューの「Excel 出力」が選ばれているか。
    /// 開くたびに一覧を読み直し、最新の記録を選べるようにする。
    /// </summary>
    public bool IsExportSelected
    {
        get => ReferenceEquals(CurrentViewModel, Export);
        set
        {
            if (!value)
                return;

            Export.Reload();
            CurrentViewModel = Export;
        }
    }

    /// <summary>ウィンドウ最下部の左側に出す状態メッセージ。</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>拡大表示中の画像。null のときはオーバーレイを出さない。復号は保存されたピクセル数のまま。</summary>
    public BitmapSource? EnlargedImage
    {
        get => _enlargedImage;
        private set
        {
            if (!SetProperty(ref _enlargedImage, value))
                return;

            OnPropertyChanged(nameof(EnlargedSizeText));
        }
    }

    /// <summary>拡大中の画像のピクセルサイズ。出ていないときは空。</summary>
    public string EnlargedSizeText =>
        _enlargedImage is null ? "" : $"{_enlargedImage.PixelWidth} × {_enlargedImage.PixelHeight} ピクセル";

    /// <summary>拡大表示を閉じる。背景、「閉じる」、Esc から呼ぶ。</summary>
    public RelayCommand CloseEnlargedCommand { get; }

    /// <summary>一覧で選んだ記録を登録画面へ読み込み、その画面へ切り替える。</summary>
    /// <param name="record">編集する記録。画像本体は含めなくてよい。</param>
    /// <param name="images">その記録に付いている JPEG。</param>
    private void BeginEdit(WasteRecord record, IReadOnlyList<RecordImage> images)
    {
        Register.BeginEdit(record, images);
        CurrentViewModel = Register;
    }

    /// <summary>保存が終わったら一覧へ戻り、保存した行を選択し直す。Excel 出力用の一覧も更新する。</summary>
    /// <param name="record">保存した記録。選択の復元に Id を使う。</param>
    /// <param name="isUpdate">既存行の更新なら true。新規登録なら false。</param>
    private void OnSaved(WasteRecord record, bool isUpdate)
    {
        CurrentViewModel = List;
        List.Reload(record.Id);
        Export.Reload();
        StatusText = isUpdate
            ? $"「{record.GeneratorName} / {record.WasteName}」の変更を保存しました。"
            : $"「{record.GeneratorName} / {record.WasteName}」を登録しました。";
    }

    /// <summary>一覧と登録から渡された状態メッセージを、ステータスバーへ出す。</summary>
    /// <param name="message">ステータスバーに出す文言。</param>
    private void SetStatus(string message) => StatusText = message;

    /// <summary>JPEG を元のピクセル数で復号し、ウィンドウ全体の拡大表示にする。</summary>
    /// <param name="jpeg">拡大する JPEG。縮小表示用の画像ではない。</param>
    private void ShowEnlarged(byte[] jpeg)
    {
        try
        {
            EnlargedImage = ImagePreview.FromJpeg(jpeg);
        }
        catch (NotSupportedException)
        {
            StatusText = "この画像は拡大表示できません。";
        }
    }

    /// <summary>拡大表示を消す。画像が無いときに呼んでも何もしない。</summary>
    private void CloseEnlarged() => EnlargedImage = null;
}
