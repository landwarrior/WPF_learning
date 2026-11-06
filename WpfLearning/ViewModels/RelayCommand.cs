using System.Windows.Input;

namespace WpfLearning.ViewModels;

/// <summary>
/// ボタンの <c>Command</c> に渡す実装。クリックされると <c>execute</c> を呼ぶ。
/// 実行できるかは <c>canExecute</c> で決め、WPF がコマンドを再評価するたびに問い合わせる。
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    /// <summary>クリック時の処理と、ボタンを押せる条件を受け取る。</summary>
    /// <param name="execute">ボタンが押されたときに呼ぶ処理。</param>
    /// <param name="canExecute">ボタンを押せる条件。省略すると常に押せる。</param>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>WPF のコマンド再評価のタイミングで購読される。</summary>
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <summary>ボタンを有効にするか。<c>canExecute</c> が無ければ常に有効。</summary>
    /// <param name="parameter">WPF から渡されるコマンド引数。この実装では使わない。</param>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    /// <summary>ボタンが押されたときの処理。</summary>
    /// <param name="parameter">WPF から渡されるコマンド引数。この実装では使わない。</param>
    public void Execute(object? parameter) => _execute();
}
