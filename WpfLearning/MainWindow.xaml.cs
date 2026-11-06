using System.Windows;

namespace WpfLearning;

/// <summary>
/// ウィンドウの枠。メニュー、見出し、ステータスバーを持ち、中央の中身は ViewModel で切り替える。
/// 入力の処理はコードビハインドに書かない。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>XAML で定義した枠を読み込む。</summary>
    public MainWindow()
    {
        InitializeComponent();
    }
}
