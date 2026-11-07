# 収集記録（WPF 学習）

WPF で画面を組み立て、データを登録する流れを確認するための学習用アプリです。題材は収集記録の一覧と登録です。

対象フレームワークは .NET 10（`net10.0-windows10.0.19041.0`）です。カメラの起動に、Windows 10 バージョン 2004 以降の API を使います。

## 起動

配布・確認用の実行ファイルは、アプリ本体と .NET 10 の実行環境を同梱した 1 ファイルです。

`publish\WpfLearning.exe`

このファイルだけを別のフォルダへコピーしてダブルクリックしても起動します。PC 全体に .NET 10 の SDK や実行環境が入っている必要はありません。

`bin\Debug\net10.0-windows10.0.19041.0` には、開発用のビルド成果物が多数のファイルに分かれて出力されます。ここにある `WpfLearning.exe` だけを移動しても起動しません。渡すファイルは `publish\WpfLearning.exe` です。

## 開発時のビルドと発行

ビルドと発行は、.NET 10 SDK の `dotnet` で行います。

```powershell
dotnet build "WpfLearning"
dotnet publish "WpfLearning" -c Release -o "publish"
```

発行設定は `WpfLearning\WpfLearning.csproj` にあります。自己完結（self-contained）、単一ファイル、ネイティブライブラリの同梱、圧縮を有効にしています。

## GitHub リリース

GitHub でリリースを公開すると、GitHub Actions がそのコミットから `WpfLearning.exe` を発行し、同じリリースに添付します。発行設定は、この PC で `publish\WpfLearning.exe` を作るときと同じです。

リリースのタグ名に特別な形式は不要です。公開したリリースに対して `.github/workflows/release.yml` が動きます。このワークフローが、そのリリースの対象コミットに含まれている必要があります。

## 画面の構成

ウィンドウ本体は枠です。表示する中身は ViewModel を差し替えて切り替えます。

- `MainWindow.xaml` が左のメニュー、見出し、ステータスバーを持ちます。中央の `ContentControl` には、いま表示する画面の ViewModel だけを渡します。
- `App.xaml` の `DataTemplate` が、ViewModel の型と UserControl を対応づけます。`WasteListViewModel` なら一覧、`WasteRegisterViewModel` なら登録画面です。
- 左の「一覧」「新規登録」「Excel 出力」は `MainViewModel.CurrentViewModel` を入れ替えます。画面を増やすときは、ViewModel と UserControl と DataTemplate を 1 組足します。
- 一覧で記録を選び「この記録を編集」を押すと、同じ登録画面にその行が読み込まれます。保存すると、新しい行は増やさずその行を更新します。
- 「Excel 出力」では、出力する記録にチェックを付けて `.xlsx` を保存します。複数選んだときは、記録ごとに Excel のシートを分けます。各シートは登録画面に近いカード＋入力欄のフォーム配置（収集日とマニフェスト、数量と単位は横並び）で、フォントはメイリオです。シート名は収集日と排出事業者名から作ります。
- 登録画面の「カメラを起動」で PC のカメラを表示し、「撮影して添付」で JPEG をその記録に加えます。保存される画像は、カメラが出せる映像のうち幅 1280 ピクセル以下でいちばん大きいものに合わせます。長辺が 1600 ピクセルを超える場合は、1600 ピクセルに縮小します。一覧や登録画面の小さい画像は表示用で、画像をクリックすると保存されたピクセル数で拡大表示します。背景か「閉じる」、または Esc で閉じます。撮影を終えたら「カメラを停止」するか、別の画面へ移るとカメラは止まります。
- Windows がデスクトップ アプリのカメラを止めている場合は、設定の「プライバシーとセキュリティ」→「カメラ」で許可が必要です。
- 入力欄や表の処理はコードビハインドに書いていません。表示はバインディング、ボタン操作は `RelayCommand` です。

登録画面の各入力は `WasteRegisterViewModel` のプロパティへ双方向バインドされます。「登録する」または「変更を保存」が入力を確認し、問題がなければ `SqliteWasteStore` へ保存して一覧へ戻ります。

## データの保存

記録は SQLite に保存します。登録、編集、削除は対象の行だけを更新し、ファイル全体は書き直しません。カメラで撮影した JPEG も、同じデータベースの行として記録と一緒に書き込まれます。

保存場所は、ログイン中の Windows ユーザーのフォルダです。exe をどこへ置いても変わりません。

- `%LOCALAPPDATA%\WpfLearning\waste-records.db` … データベース
- `%LOCALAPPDATA%\WpfLearning\waste-records.key` … データベースの鍵

起動中のウィンドウ最下部の「保存先」にも、データベースのパスが出ます。

## アンインストール

このアプリは PC へインストールしません。レジストリ登録や「アプリのアンインストール」画面はありません。次の 2 つを削除すれば完了です。

1. 置いてある `WpfLearning.exe` を削除する（起動中なら先に終了する）
2. ユーザーデータ `%LOCALAPPDATA%\WpfLearning` をフォルダごと削除する

エクスプローラーのアドレスバーに `%LOCALAPPDATA%\WpfLearning` を貼り付けて開いても構いません。PowerShell なら次でも同じです。

```powershell
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\WpfLearning"
```

## 暗号化

データベースファイル自体を SQLCipher で暗号化しています。メモ帳などで開いても、登録した内容は読めません。

鍵ファイルの中身は、その PC にサインインしている Windows ユーザーに対して DPAPI（`DataProtectionScope.CurrentUser`）で暗号化しています。Microsoft アカウントである必要はなく、ローカルアカウントでも同じです。

鍵ファイルとデータベースの両方を別のユーザーや別の PC へコピーしても、受け取り側は鍵を復号できないため、データベースは開けません。同じ PC の同じ Windows ユーザーでこのアプリを起動したときだけ読めます。

## フォルダ構成

```text
WpfLearning/
  App.xaml                 画面の対応表（DataTemplate）
  MainWindow.xaml          ウィンドウの枠
  Models/WasteRecord.cs    収集記録 1 件
  Services/SqliteWasteStore.cs
  Services/ExcelWasteExporter.cs  チェックした記録の Excel 出力
  ViewModels/              画面の状態と登録・削除・Excel 出力の処理
  Views/                   一覧・登録・Excel 出力の UserControl
  Themes/AppStyles.xaml    色とコントロールの見た目
publish/
  WpfLearning.exe          配布する実行ファイル
```

## この学習アプリの範囲

一覧画面は、表示のために保存済みの記録をすべてメモリへ読み込みます。保存は対象の行だけです。
