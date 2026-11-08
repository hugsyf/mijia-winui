# 版本发布与本地化

## 更新流程

在“设置 → 关于软件”中点击“检查更新”。
自动检查每天最多执行一次。
你可以关闭自动检查。

应用读取 `hugsyf/mijia-winui` 的 GitHub Releases。
应用只接受正式发布版本。
应用按版本数字比较，不按文字排序。
没有公开 Release 时，应用显示暂无发布版本。
访问失败不表示应用已是最新版。

下载完成后，应用检查文件大小和 SHA-256。
校验失败时，应用删除临时文件。
安装前，应用要求用户确认退出。
应用不会自动安装新版本。

未提供可校验安装附件时，应用打开发布页面。
用户可以在浏览器中选择下载文件。

## 发布要求

1. 更新应用版本和安装器版本。
2. 生成安装包。
3. 创建名称为 `vX.Y.Z` 形式的正式 Release。
4. 上传 `MiHomeWinUI-X.Y.Z-x64-Setup.exe` 形式的附件。
5. 在 Release 正文中填写用户更新说明。
6. 确认附件具有 GitHub 返回的 `sha256` 摘要。

仓库标识保存在 `Infrastructure/UpdateService.cs`。
请求格式见 [GitHub Releases API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release)。

## 界面语言

应用支持简体中文和英文。
在“设置 → 外观与行为”中选择语言。
重新打开应用后生效。
“跟随系统”使用系统界面语言。
不支持的系统语言使用简体中文。

固定界面使用 WinUI `.resw` 和 `x:Uid`。
动态文字使用 `UiText`。
两种资源都从 `Strings/catalog.json` 生成。
修改资源目录后，执行以下命令：

```powershell
python tools/sync_ui_resources.py
```

应用加载窗口前设置 Windows App SDK 的语言覆盖。
该方式支持当前未打包部署。
接口说明见 [ApplicationLanguages.PrimaryLanguageOverride](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.globalization.applicationlanguages.primarylanguageoverride)。

## 设备与适配包

宿主按界面语言读取 MIOT 多语言资料。
不同语言使用不同的缓存目录。
设备名称和用户输入不自动翻译。
协议标识、控制值和单位代码不改变。

适配包可以提供自己的翻译字典。
缺少翻译时，使用包的默认语言。
未找到资源键时，使用原有标签。
未知设备字符串可以保留原文。
不得根据英文单词猜测控制值。

完整字段见 [适配包开发指南](../backend/adapter-agent-guide.md)。
