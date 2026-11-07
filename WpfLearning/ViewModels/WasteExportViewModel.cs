using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using WpfLearning.Models;
using WpfLearning.Services;

namespace WpfLearning.ViewModels;

/// <summary>
/// Excel 出力画面。チェックした記録を、1 件につき 1 シートの .xlsx へ書き出す。
/// </summary>
public sealed class WasteExportViewModel : PageViewModel
{
    private readonly SqliteWasteStore _store;
    private readonly Action<string> _reportStatus;

    /// <summary>保存先から一覧を読み、チェックなしの状態で用意する。</summary>
    /// <param name="store">記録の読み書きに使う保存先。</param>
    /// <param name="reportStatus">ステータスバーへメッセージを出す処理。</param>
    public WasteExportViewModel(SqliteWasteStore store, Action<string> reportStatus)
    {
        _store = store;
        _reportStatus = reportStatus;
        ReloadCommand = new RelayCommand(ReloadFromFile);
        SelectAllCommand = new RelayCommand(SelectAll, () => Items.Count > 0);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => SelectedCount > 0);
        ExportCommand = new RelayCommand(Export, () => SelectedCount > 0);
        Reload();
    }

    /// <inheritdoc />
    public override string Title => "Excel へ出力";

    /// <inheritdoc />
    public override string Description =>
        "出力する記録にチェックを付けます。複数選んだときは、Excel のシートを記録ごとに分けます。添付画像もシートへ埋め込みます。";

    /// <summary>表に出す記録。各行にチェック状態を持つ。</summary>
    public ObservableCollection<ExportRecordItem> Items { get; } = new();

    /// <summary>表の上に出す件数と選択数。</summary>
    public string SelectionSummaryText => $"{Items.Count} 件中 {SelectedCount} 件を選択";

    /// <summary>チェックが付いている件数。</summary>
    public int SelectedCount => Items.Count(item => item.IsSelected);

    /// <summary>データベースから一覧を読み直す。</summary>
    public RelayCommand ReloadCommand { get; }

    /// <summary>すべての行にチェックを付ける。</summary>
    public RelayCommand SelectAllCommand { get; }

    /// <summary>すべてのチェックを外す。</summary>
    public RelayCommand ClearSelectionCommand { get; }

    /// <summary>チェックした記録を Excel へ書き出す。1 件も選んでいないときは押せない。</summary>
    public RelayCommand ExportCommand { get; }

    /// <summary>一覧を読み直し、チェックはすべて外す。</summary>
    public void Reload()
    {
        var records = _store.Load();
        Items.Clear();
        foreach (var record in records)
            Items.Add(new ExportRecordItem(record, OnSelectionChanged));

        NotifySelectionChanged();
    }

    /// <summary>再読込ボタン。読み直したことをステータスバーへ出す。</summary>
    private void ReloadFromFile()
    {
        Reload();
        _reportStatus("ファイルから再読込しました。");
    }

    /// <summary>すべての行を選択する。</summary>
    private void SelectAll()
    {
        foreach (var item in Items)
            item.IsSelected = true;
    }

    /// <summary>すべての選択を外す。</summary>
    private void ClearSelection()
    {
        foreach (var item in Items)
            item.IsSelected = false;
    }

    /// <summary>保存ダイアログのあと、チェックした記録をシート分割して書き出す。</summary>
    private void Export()
    {
        var selected = Items.Where(item => item.IsSelected).Select(item => item.Record).ToList();
        if (selected.Count == 0)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Excel ファイルの保存先",
            Filter = "Excel ブック (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            FileName = $"収集記録_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            foreach (var record in selected)
                record.Images = _store.LoadImages(record.Id);

            ExcelWasteExporter.Export(dialog.FileName, selected);
            _reportStatus($"{selected.Count} 件を Excel へ出力しました。");
            MessageBox.Show(
                $"{selected.Count} 件を出力しました。\n{dialog.FileName}",
                "Excel 出力",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _reportStatus("Excel への出力に失敗しました。");
            MessageBox.Show(
                $"Excel への出力に失敗しました。\n{ex.Message}",
                "Excel 出力",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>行のチェックが変わったときに、件数表示とボタンの有効状態を更新する。</summary>
    private void OnSelectionChanged() => NotifySelectionChanged();

    /// <summary>選択数の表示とコマンドの再評価をまとめて行う。</summary>
    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionSummaryText));
        CommandManager.InvalidateRequerySuggested();
    }
}

/// <summary>
/// Excel 出力表の 1 行。記録本体と、出力対象かどうかのチェックを持つ。
/// </summary>
public sealed class ExportRecordItem : ViewModelBase
{
    private readonly Action _onSelectionChanged;
    private bool _isSelected;

    /// <param name="record">表に出す記録。</param>
    /// <param name="onSelectionChanged">チェックが変わったときに親へ知らせる処理。</param>
    public ExportRecordItem(WasteRecord record, Action onSelectionChanged)
    {
        Record = record;
        _onSelectionChanged = onSelectionChanged;
    }

    /// <summary>この行の収集記録。</summary>
    public WasteRecord Record { get; }

    /// <summary>Excel へ出力する対象なら true。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value))
                return;

            _onSelectionChanged();
        }
    }
}
