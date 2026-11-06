using System.Windows.Controls;

namespace WpfLearning.Views;

/// <summary>
/// 収集記録の一覧。表示とボタンは <see cref="ViewModels.WasteListViewModel"/> へのバインドだけを持つ。
/// </summary>
public partial class WasteListView : UserControl
{
    /// <summary>XAML で定義した一覧を読み込む。</summary>
    public WasteListView()
    {
        InitializeComponent();
    }
}
