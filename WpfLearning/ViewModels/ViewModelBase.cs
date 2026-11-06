using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfLearning.ViewModels;

/// <summary>
/// プロパティの変更を、バインドしている画面へ知らせる基底クラス。
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    /// <summary>バインド先が変更を受け取るためのイベント。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 値が変わったときだけフィールドを更新し、変更を通知する。
    /// </summary>
    /// <param name="field">更新するフィールド。</param>
    /// <param name="value">新しく入る値。</param>
    /// <param name="propertyName">通知するプロパティ名。省略すると呼び出し元の名前になる。</param>
    /// <returns>値を更新したとき true。同じ値なら false。</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>指定したプロパティの変更を通知する。</summary>
    /// <param name="propertyName">通知するプロパティ名。省略すると呼び出し元の名前になる。</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// <c>MainWindow</c> の ContentControl に出す画面の ViewModel。見出しは <see cref="Title"/> と <see cref="Description"/>。
/// </summary>
public abstract class PageViewModel : ViewModelBase
{
    /// <summary>ウィンドウ上部に出す画面名。</summary>
    public abstract string Title { get; }

    /// <summary>画面名の下に出す説明。</summary>
    public abstract string Description { get; }
}
