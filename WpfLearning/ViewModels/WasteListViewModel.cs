using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WpfLearning.Models;
using WpfLearning.Services;

namespace WpfLearning.ViewModels;

/// <summary>
/// 一覧画面。 <see cref="Records"/> の中身が変わると DataGrid が追従する。
/// 画像の本体は選択中の行だけ読み、表には枚数だけを出す。
/// </summary>
public sealed class WasteListViewModel : PageViewModel
{
    private readonly SqliteWasteStore _store;
    private readonly Action<string> _reportStatus;
    private readonly Action<WasteRecord, IReadOnlyList<RecordImage>> _onEdit;
    private readonly Action<byte[]> _showImage;
    private WasteRecord? _selectedRecord;

    /// <summary>保存先から一覧を読み、初回ならサンプル投入のメッセージを出す。</summary>
    /// <param name="store">記録の読み書きに使う保存先。</param>
    /// <param name="reportStatus">ステータスバーへメッセージを出す処理。</param>
    /// <param name="onEdit">選択行の編集を、登録画面へ渡す処理。</param>
    /// <param name="showImage">添付画像の JPEG を拡大表示する処理。</param>
    public WasteListViewModel(
        SqliteWasteStore store,
        Action<string> reportStatus,
        Action<WasteRecord, IReadOnlyList<RecordImage>> onEdit,
        Action<byte[]> showImage)
    {
        _store = store;
        _reportStatus = reportStatus;
        _onEdit = onEdit;
        _showImage = showImage;
        ReloadCommand = new RelayCommand(ReloadFromFile);
        EditCommand = new RelayCommand(EditSelected, () => SelectedRecord is not null);
        DeleteCommand = new RelayCommand(DeleteSelected, () => SelectedRecord is not null);
        Reload();
        _reportStatus(store.CreatedSampleData
            ? "初回起動のため、学習用サンプルを 3 件保存しました。"
            : "保存済みの記録を読み込みました。");
    }

    /// <inheritdoc />
    public override string Title => "収集記録の一覧";

    /// <inheritdoc />
    public override string Description =>
        "表の各行は WasteRecord です。選択した記録は、下の詳細から編集または削除できます。";

    /// <summary>表に出す記録。収集日の新しい順。</summary>
    public ObservableCollection<WasteRecord> Records { get; } = new();

    /// <summary>選択中の記録に付いた画像の縮小表示。クリックすると元の JPEG を拡大する。</summary>
    public ObservableCollection<ThumbnailImage> SelectedImagePreviews { get; } = new();

    /// <summary>表の上に出す件数。</summary>
    public string RecordCountText => $"{Records.Count} 件";

    /// <summary>表で選ばれている行。変わると詳細の画像を読み直す。</summary>
    public WasteRecord? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (!SetProperty(ref _selectedRecord, value))
                return;

            LoadSelectedImages();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>データベースから一覧を読み直す。</summary>
    public RelayCommand ReloadCommand { get; }

    /// <summary>選択中の記録を登録画面で開く。行が選ばれていないときは押せない。</summary>
    public RelayCommand EditCommand { get; }

    /// <summary>選択中の記録を、確認のあと削除する。行が選ばれていないときは押せない。</summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>一覧を読み直す。</summary>
    /// <param name="selectId">読み直したあと選択する記録。省略すると選択しない。</param>
    public void Reload(Guid? selectId = null)
    {
        var records = _store.Load();
        SelectedRecord = null;
        Records.Clear();
        foreach (var record in records)
            Records.Add(record);

        if (selectId is Guid id)
            SelectedRecord = Records.FirstOrDefault(record => record.Id == id);

        OnPropertyChanged(nameof(RecordCountText));
    }

    /// <summary>再読込ボタン。読み直したことをステータスバーへ出す。</summary>
    private void ReloadFromFile()
    {
        Reload();
        _reportStatus("ファイルから再読込しました。");
    }

    /// <summary>選択行と、その JPEG を登録画面の編集へ渡す。</summary>
    private void EditSelected()
    {
        if (SelectedRecord is not WasteRecord record)
            return;

        _onEdit(record, _store.LoadImages(record.Id));
    }

    /// <summary>選択行の JPEG を幅 240 ピクセルの縮小画像にする。壊れた画像は飛ばす。</summary>
    private void LoadSelectedImages()
    {
        SelectedImagePreviews.Clear();
        if (_selectedRecord is null)
            return;

        foreach (var image in _store.LoadImages(_selectedRecord.Id))
        {
            try
            {
                SelectedImagePreviews.Add(new ThumbnailImage(ImagePreview.FromJpeg(image.Jpeg, 240), image.Jpeg, _showImage));
            }
            catch (NotSupportedException)
            {
                _reportStatus("読み込めない画像があったため、その 1 枚は表示していません。");
            }
        }
    }

    /// <summary>確認ダイアログのあと、選択行と添付画像を削除して一覧を読み直す。</summary>
    private void DeleteSelected()
    {
        if (SelectedRecord is not WasteRecord record)
            return;

        var answer = MessageBox.Show(
            $"{record.CollectedOn:yyyy/MM/dd} {record.GeneratorName} の「{record.WasteName}」を削除しますか？",
            "削除の確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        _store.Delete(record.Id);
        Reload();
        _reportStatus("記録を削除しました。");
    }
}

/// <summary>
/// 一覧の詳細に出す縮小画像。クリックすると、縮小前の JPEG を拡大表示する。
/// </summary>
public sealed class ThumbnailImage
{
    /// <param name="thumbnail">詳細に出す縮小画像。</param>
    /// <param name="jpeg">拡大表示に使う、保存された JPEG。</param>
    /// <param name="open">拡大表示を開く処理。</param>
    public ThumbnailImage(BitmapSource thumbnail, byte[] jpeg, Action<byte[]> open)
    {
        Thumbnail = thumbnail;
        OpenCommand = new RelayCommand(() => open(jpeg));
    }

    /// <summary>詳細欄に出す縮小画像。</summary>
    public BitmapSource Thumbnail { get; }

    /// <summary>この縮小画像をクリックしたとき、元の JPEG を拡大する。</summary>
    public RelayCommand OpenCommand { get; }
}
