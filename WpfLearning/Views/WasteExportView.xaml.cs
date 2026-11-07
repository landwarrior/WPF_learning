using System.Windows.Controls;

namespace WpfLearning.Views;

/// <summary>
/// Excel 出力画面。表示とボタンは <see cref="ViewModels.WasteExportViewModel"/> へのバインドだけを持つ。
/// </summary>
public partial class WasteExportView : UserControl
{
    /// <summary>XAML で定義した Excel 出力画面を読み込む。</summary>
    public WasteExportView()
    {
        InitializeComponent();
    }
}
