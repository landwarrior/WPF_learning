using System.Windows;
using WpfLearning.Services;
using WpfLearning.ViewModels;

namespace WpfLearning;

/// <summary>
/// アプリケーションの入口。保存先を開いてからメインウィンドウを出す。
/// データベースを開けないときはメッセージを出して終了する。
/// </summary>
public partial class App : Application
{
    /// <summary>保存処理と画面の状態を組み立て、ウィンドウへ渡す。</summary>
    /// <param name="e">起動時の引数。このアプリでは使わない。</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var store = new SqliteWasteStore();
            var mainWindow = new MainWindow
            {
                DataContext = new MainViewModel(store)
            };
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "起動できませんでした", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}
