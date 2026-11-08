# 内置面板开发

适用应用版本：0.20.0。
本文件面向修改应用源码的开发者。
可分发的数据适配包使用[适配包开发指南](../backend/adapter-agent-guide.md)。

## 当前接口

`MiHome.Contracts/PanelContracts.cs` 定义面板接口。
`IDevicePanelProvider` 包含以下成员：

| 成员 | 用途 |
| --- | --- |
| `Id` | 唯一提供者标识 |
| `ContractVersion` | 契约版本；当前值为 1 |
| `Priority` | 同匹配等级下的优先级 |
| `Match` | 返回匹配等级 |
| `CreateDetail` | 创建设备详情 |
| `CreateCard` | 预留卡片工厂；当前主界面未调用 |

`PanelContext` 包含设备、规格和宿主服务。
当前应用注册通用提供者和内置设备体验提供者。
这些提供者随应用编译。
应用不从磁盘加载任意面板 DLL。

## 实施步骤

1. 实现 `IDevicePanelProvider`。
2. 设置唯一 `Id`。
3. 将 `ContractVersion` 设为 1。
4. 在 `Match` 中检查实际 MIOT URN。
5. 用独立 XAML 定义固定布局。
6. 在 `CreateDetail` 中返回对应控件。
7. 通过 `PanelContext.Service` 访问设备。
8. 在应用初始化处注册提供者。

固定布局和动态内容的边界见 [XAML 界面维护](xaml-ui-architecture.md)。
只有按实际规格生成的项目可以继续由代码创建。

需要周期刷新时，实现 `IRefreshablePanel`。
独立类库可以引用 `MiHome.Contracts`。
独立类库仍需由应用显式引用并注册。

## 匹配与回退

注册器按以下等级排序：

1. 精确型号。
2. 精确规格类型。
3. 服务能力。
4. 通用面板。

同等级使用 `Priority` 排序。
同等级且同优先级的多个匹配会报告冲突。
提供者创建失败时，注册器使用通用面板。

设备已选择声明式适配包时，主窗口先使用该适配包。
该路径通过 `AdapterPanel` 和 `DeviceCardView` 渲染。
它不使用预留的 `CreateCard` 工厂。

## 操作要求

用 `ServiceType` 和 `PropertyType` 匹配属性。
用 `RoomId` 匹配房间。
不要用翻译后的名称匹配协议。

保留输入校验、错误反馈和回读确认。
不得自动重试结果未知的控制。
专属面板应保留高级属性入口。
不得直接读取 Python 对象或登录凭据。
