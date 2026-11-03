# 第三者ソフトウェアについて

[LICENSE](LICENSE) の MIT License は、このリポジトリに含まれる ja1bus が作成したソースコードとファイルだけに適用されます。以下の第三者のソフトウェアのライセンスは変更しません。

## 実行時

TimeInset が使うのは .NET 10 と Windows Desktop 共有フレームワーク（WPF・Windows Forms・System.Text.Json）だけです。これらは Microsoft が MIT License で提供しているもので、このリポジトリには含まれていません。Microsoft Store のパッケージには .NET のランタイムを含めています。このリポジトリからビルドして実行する場合は、PC にインストールされている .NET 10 Desktop Runtime を使います。

- .NET: https://github.com/dotnet/runtime (MIT)
- WPF: https://github.com/dotnet/wpf (MIT)
- Windows Forms: https://github.com/dotnet/winforms (MIT)

.NET のランタイム自体にも第三者のコンポーネントが含まれています。その表記は https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT にあります。

.NET・WPF・Windows Forms のライセンス:

```text
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

フォントは同梱していません。Windows にインストールされているフォント（Segoe UI・Yu Gothic UI・Consolas）を使います。

## テストのみ

次の NuGet パッケージは `Tests/TimeInset.Tests.csproj` から参照しています。ビルド時に nuget.org から取得するもので、このリポジトリにもアプリにも含まれていません。

| パッケージ | 版 | ライセンス |
|---|---|---|
| Microsoft.NET.Test.Sdk | 17.11.1 | MIT |
| xunit | 2.9.2 | Apache-2.0 |
| xunit.runner.visualstudio | 2.8.2 | Apache-2.0 |
