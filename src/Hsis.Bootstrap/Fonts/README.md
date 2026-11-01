# Noto Sans JP

準備画面の定型文に必要な文字だけを収録した、Regular / SemiBold のサブセットです。元のフォントは Noto Sans JP（Google / Adobe）です。SIL Open Font License 1.1 の全文は `OFL.txt` にあります。

- 公式配布元: https://github.com/google/fonts/tree/main/ofl/notosansjp
- 準備画面に表示する文言を変更した場合、必要な文字を追加してサブセットを再生成してください。
- 元の静的 TTF を Windows のフォントフォルダーに置き、リポジトリ直下で以下を実行します。fonttools は生成時だけ使い、配布先には不要です。

```powershell
uv run --no-project --with fonttools python src/Hsis.Bootstrap/Fonts/subset.py
```

フォントとライセンス文書は起動 EXE のリソースに埋め込み、フォント未導入の PC でもプロセス内だけで読み込みます。点字スピナーの記号には Windows 標準の Segoe UI Symbol を使用します。
