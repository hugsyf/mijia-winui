# XAML 界面维护

## 文件职责

将固定布局写入 `.xaml` 文件。
将该界面的事件处理写入对应的 `.xaml.cs` 文件。
用数据模板定义重复的列表项。

| 位置 | 职责 |
| --- | --- |
| `MainWindow.xaml` | 窗口、导航、登录、设备列表和详情抽屉 |
| `Pages/SettingsPage.xaml` | 设置导航、工具栏和滚动区域 |
| `Pages/AppearancePage.xaml` | 主题与刷新设置 |
| `Pages/AccountPage.xaml` | 账号与家庭操作 |
| `Pages/AboutPage.xaml` | 版本、诊断和说明入口 |
| `Pages/LicenseCatalogPage.xaml` | 许可目录及展开模板 |
| `Pages/AdaptersPage.xaml` | 适配包命令、筛选、多选和列表模板 |
| `Controls/HomeOverviewView.xaml` | 首页分区和项目模板 |
| `Controls/LicensesView.xaml` | 许可搜索、列表和阅读区域 |
| `Controls/DeviceCardView.xaml` | 卡片标题、图标、电源和内容位置 |
| `Controls/ExpandedCardView.xaml` | 展开卡片的标题和控制区域 |
| `Controls/AdapterPreviewView.xaml` | 适配预览的状态与显示方式切换 |
| `Controls/AdapterValueEditor.xaml` | 属性编辑的标题、提交和反馈区域 |
| `Controls/ColorEditorView.xaml` | 颜色选择及确认操作 |
| `Controls/ActionConfirmationView.xaml` | 动作确认的说明、参数位置和操作按钮 |
| `Controls/MetricView.xaml` | 数值标题、读数和补充状态 |
| `Controls/PowerStatusView.xaml` | 电源、湿度和附加读数位置 |
| `Panels/*.xaml` | 内置面板、通用面板和适配面板的固定外框 |
| `Dialogs/*.xaml` | 信息、选择、导出、进度和批量导入对话框 |

上述路径均相对于 `src/MiHome.WinUI`。
`Pages` 中的界面使用 `UserControl`。
主窗口负责切换这些控件。

## 可以保留在 C# 中的内容

- 根据实际规格选择控件。
- 根据参数数量生成动作输入项。
- 根据属性权限设置控件状态。
- 根据适配声明生成组、读数和条件内容。
- 根据规格设置范围、单位和枚举值。
- 读取图片并生成设备图形。
- 执行设备请求和处理结果。
- 计算响应式尺寸和拖动位置。
- 管理动画、焦点和控件生命周期。

`AdapterPanel.Build` 渲染声明式适配内容。
`GenericPanel.AddGroups` 按实际服务生成属性行。
`ActionControl.Input` 按动作参数生成输入项。
这些方法不能确定固定的项目数量。
它们可以继续创建动态控件。

`DocumentPresenter` 按文档内容生成文本块。
代码块的固定外框使用 `CodeBlockView.xaml`。

诊断入口可以创建临时测量容器。
诊断容器不属于应用的固定页面。

## 修改步骤

1. 打开需要修改的页面或控件 XAML。
2. 在 XAML 中修改间距、对齐、样式和模板。
3. 在代码中修改绑定数据或事件处理。
4. 检查窄窗口下的排列规则。
5. 对应用项目执行针对性构建。
6. 在实际窗口中确认改动。

设计器中的示例文字只用于预览。
不得将示例读数发送给设备。
控件的无参数构造函数只初始化界面。
它们不启动后端或发送设备请求。

## 维护要求

不得用 XAML 字符串替代独立 XAML 文件。
不得在页面初始化方法中重新拼装固定布局。
新增固定对话框时，先创建对应 XAML。
新增动态控件时，复用已有标题、反馈和操作外框。

保留现有的单次提交、输入校验和回读确认。
保留未提交输入。
不得因布局修改而自动重发设备请求。
