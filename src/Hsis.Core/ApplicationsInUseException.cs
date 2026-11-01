namespace Hsis.Core;

public sealed class ApplicationsInUseException(string applications) : IOException(
    $"アプリが起動中のため処理を開始できません。{applications} を終了してから、もう一度実行してください。");
