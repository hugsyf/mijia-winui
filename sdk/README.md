# 适配包 SDK

SDK 版本：0.20.0。
对应应用版本：0.20.0。
格式版本：`formatVersion: 1`。

本文面向 Agent 和适配作者。
以下路径相对工具包根目录。

## 内容

- `backend/adapter-agent-guide.md`：开发指南。
- `sdk/adapter.schema.json`：编辑器字段定义。
- `tools/adapter_tool.py`：离线打包与校验工具。
- `backend/adapters.py`：包验证器。
- `backend/action_support.py`：动作规格与参数校验。
- `examples/adapters`：源码示例。
- `LICENSE`：工具代码许可。

## 开发步骤

1. 准备 Python 3.14。
2. 从应用导出适配资料。
3. 阅读 `backend/adapter-agent-guide.md`。
4. 创建适配源码文件夹。
5. 根据实际规格定义绑定和布局。
6. 生成预览资料。
7. 打包并校验适配包。
8. 在应用中检查模拟预览。

离线工具不需要账号。
工具不会连接真实设备。
预览通过不证明真实控制兼容。

## 命令

生成预览文件：

```powershell
python tools/adapter_tool.py preview-from-export device-spec.json examples/my-adapter/preview.json
```

打包源码：

```powershell
python tools/adapter_tool.py pack examples/my-adapter artifacts/my-adapter.mihome-adapter
```

校验包：

```powershell
python tools/adapter_tool.py validate artifacts/my-adapter.mihome-adapter
```

应用支持普通卡片、展开卡片、抽屉和整页布局。
SDK 支持属性读写和标量 MIOT Actions。
参照 `backend/adapter-agent-guide.md` 中的「小爱音箱」示例声明播放状态、音量、静音和文字动作。
SDK 不提供脚本执行、视频、历史数据或本机定时任务。

SDK 0.20.0 增加 `conditions.enabled` 和 `labels.localized`。
灯光示例提供条件禁用及中英文标签。
旧包的格式版本保持为 1。
