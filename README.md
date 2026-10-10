# Tooltip Extras

アイテム・アクションの追加情報を、元ツールチップに沿った専用パネルへ表示します。

## 導入

1. Dalamud設定のカスタムプラグインリポジトリに、次のURLを追加します。
2. プラグインインストーラーから **Tooltip Extras** をインストールします。
3. `/tooltipextras` で設定を開きます。

```text
https://raw.githubusercontent.com/minasis-min/DalamudPlugins/main/pluginmaster.json
```

## 主な機能

- プラグインによる追加情報を専用パネルへ分離。
- 表示側・文字サイズ・位置・引用元名を調整し、自動保存。

設定UIは **MinasisUI／Dalamud標準UI** を選択できます（既定：MinasisUI）。

## コマンド

| コマンド | 内容 |
|---|---|
| `/tooltipextras` | 設定を開く |

## 補足

- Windows x64 / Dalamud 15.0.3.6（API 15）と対応DLLに限定されます。更新後に表示・分離が停止する場合があります。
- すべての追加情報の分離には対応していません。
- 設定・診断はDalamud標準のプラグイン設定フォルダへ保存します。保存先は診断画面で確認できます。
- アンインストールはプラグインインストーラーから行えます。設定を削除する場合は保存先を確認し、TEのファイルだけを削除してください。
- 不具合は [Issues](https://github.com/minasis-min/TooltipExtras/issues) に、版・対象プラグインの版・再現手順を添えて報告してください。対応可能な範囲で確認します。

併用条件・旧設定の移行は [USAGE.md](docs/USAGE.md)、ビルドと依存関係は [DEPENDENCIES.md](docs/DEPENDENCIES.md)、検証範囲は [TESTING.md](docs/TESTING.md) を参照してください。

## ライセンス

ライセンス：[MIT](LICENSE)
