# 参与开发

## 构建

准备 Windows 11 x64、.NET 10 SDK、带有 pip 的 Python 3.14 和 Inno Setup 6。

```powershell
git clone https://github.com/hugsyf/mijia-winui.git
cd mijia-winui
.\package.ps1
```

安装包输出到 `artifacts`。首次构建需要下载依赖。

生成适配包 SDK：

```powershell
python tools/package_sdk.py
```

SDK 压缩包输出到 `artifacts`。

## 修改代码

将固定布局放入独立的 XAML 页面或控件。仅对设备规格产生的动态界面使用代码构建控件。

界面文字应简短、明确。只保留用户完成操作需要的信息。将维护者需要的实现说明放入开发文档。

修改界面文字时，编辑 `src/MiHome.WinUI/Strings/catalog.json`，然后生成语言资源：

```powershell
python tools/sync_ui_resources.py
```

文档使用短句和明确的操作指令，遵循 ASD-STE100 的简明表达原则。更新说明应描述用户可见的变化。

适配包开发参见 [SDK](sdk/README.md)。应用结构参见[开发文档](docs/README.md)。

## 提交修改

每次修改应围绕一个问题。说明修改后的行为，以及已执行的检查。涉及界面时，请附上截图；涉及设备控制时，请注明验证过的型号。

不要提交账号数据、设备读数、诊断文件或安装包。
