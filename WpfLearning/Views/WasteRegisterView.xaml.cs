using System.Windows.Controls;

namespace WpfLearning.Views;

/// <summary>
/// 収集記録の登録と編集。新規と編集で同じ画面を使い、処理は <see cref="ViewModels.WasteRegisterViewModel"/> に委譲する。
/// </summary>
public partial class WasteRegisterView : UserControl
{
    /// <summary>XAML で定義した入力欄を読み込む。</summary>
    public WasteRegisterView()
    {
        InitializeComponent();
    }
}
