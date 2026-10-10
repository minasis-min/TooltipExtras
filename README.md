# Tooltip Extras

アイテム・アクションの追加情報を、元ツールチップに沿った専用パネルへ表示します。作者：min。**Price Insightの分離は初期OFF**です。

## 導入

1. Dalamud設定のカスタムプラグインリポジトリに、次のURLを追加します。
2. プラグインインストーラーから **Tooltip Extras** をインストールします。
3. `/tooltipextras` で設定を開きます。

```text
https://raw.githubusercontent.com/minasis-min/DalamudPlugins/main/pluginmaster.json
```

Dev版は先に無効化し、二重読み込みを避けてください。公式Dalamudリポジトリへの登録は行いません。

## 主な機能

- Price Insight・Item Vendor Locationの追加価格情報を分離。
- Glamour Logの収納状態・衣装セットを表示。
- 内蔵データによる追加パッチ表示。Which Patch Was That?の導入は不要です。
- 表示側・文字サイズ・位置・引用元名を調整し、自動保存。

設定UIは **MinasisUI／Dalamud標準UI** を選択できます（既定：MinasisUI）。

## コマンド

| コマンド | 内容 |
|---|---|
| `/tooltipextras` | 設定・診断を開く |

## 補足

- Windows x64 / Dalamud 15.0.3.6（API 15）と対応DLLに限定されます。更新後に表示・分離が停止する場合があります。
- Simple Tweaks・Allagan Toolsの追加情報分離は未対応です。Item Vendor Locationの分離は実機確認待ちです。
- 「有効」は分離成立の保証ではありません。診断で処理状態を確認してください。録画からのフレーム単位の除外は未確認です。
- 設定・診断はDalamud標準のプラグイン設定フォルダへ保存します。保存先は診断画面で確認できます。
- アンインストールはプラグインインストーラーから行えます。設定を削除する場合は保存先を確認し、TEのファイルだけを削除してください。
- 不具合は [Issues](https://github.com/minasis-min/TooltipExtras/issues) に、版・対象プラグインの版・再現手順を添えて報告してください。対応可能な範囲で確認します。

併用条件・旧設定の移行は [USAGE.md](USAGE.md)、ビルドと依存関係は [DEPENDENCIES.md](DEPENDENCIES.md)、検証範囲は [TESTING.md](TESTING.md) を参照してください。

## ライセンス

自作部分・ユーザー制作アイコン：[BSD-3-Clause](LICENSE)。著作権者：Minasis。借用部分とMinasisUIのMIT表示は [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) に保持しています。

## AI利用

実装・修正、TEのUI、テスト、公開用ビルド処理、文書にCodexのコード生成・編集を使用しています。仕様・表示方針の指定、採用アイコンの制作、Dev実機確認はユーザーが行いました。内蔵パッチデータはWhichPatch由来です。以前のAI生成アイコンは配布に含めません。
