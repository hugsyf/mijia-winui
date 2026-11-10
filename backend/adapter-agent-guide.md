# 设备适配包开发指南

适用应用版本：0.20.0。
适配 SDK 版本：0.20.0。
格式版本：`formatVersion: 1`。

本文面向编写适配包的 Agent 和开发者。
宿主使用 WinUI 原生控件显示适配包。
适配包只能提供数据定义和包内图片。

## 条件禁用与多语言

需要条件禁用时，在 `requires` 中声明 `conditions.enabled`。
在控件或组上添加以下字段：

```json
{ "enabledWhen": { "binding": "power", "equals": true } }
```

`power` 必须是本包已声明的可读绑定。
`equals` 必须符合该绑定的实际类型、范围和枚举。
条件不满足或读数缺失时，控件显示为禁用。
组的条件同时约束子控件。
提交属性或动作前，宿主会重新检查条件。
后端使用最新读取的状态再次检查。
`when` 继续控制显示与隐藏。
不得用禁用条件声明额外控制权限。

需要翻译时，在 `requires` 中声明 `labels.localized`。
在根对象中添加 `localization`：

```json
{ "localization": {
  "defaultLocale": "zh-CN",
  "resources": {
    "zh-CN": { "brightness": "亮度" },
    "en-US": { "brightness": "Brightness" }
  }
} }
```

控件保留原有 `label`，并通过 `labelKey` 引用资源。
`options` 和 `states` 中的标签也支持 `labelKey`。
只读 `status` 支持 `options`，用于卡片枚举标签。
不得改变枚举的实际值。

每包最多包含 12 种语言。
每种语言最多包含 300 个键。
资源键最长为 80 个字符。
标签最长为 100 个字符。
所有引用必须在默认语言中存在。
翻译只能覆盖默认语言已有的键。
资源必须保存在适配 JSON 内，不得引用远程文件。
缺少当前语言翻译时，宿主使用默认语言。
旧包可以继续使用普通文字标签。

灯关闭后是否允许预设参数，由适配作者按实际设备行为确定。
需要置灰的控件应明确填写 `enabledWhen`。
示例见 `examples/adapters/lighting`。

## 准备输入资料

1. 从应用导出设备适配资料。
2. 核对资料中的 `model`。
3. 核对资料中的 `specType`。
4. 读取 `schema` 和原始 `miotSpec`。

单设备导出可以附带 `readings` 样本。
批量导出不附带读数。
样本不代表当前设备状态。
不得推测缺失值、权限、单位或枚举。

## 创建包

1. 创建一个源码文件夹。
2. 在根目录创建 `adapter.json`。
3. 在根目录创建 `preview.json`。
4. 在根目录创建 `LICENSE`。
5. 需要说明时，添加 `README.md`。
6. 需要图片时，将 PNG 放入 `assets`。
7. 使用离线工具生成 `.mihome-adapter` 文件。

包不能包含脚本、DLL、HTML 或远程资源。
包不能包含账号、令牌或私人设备资料。
使用自己有权分发的图片。
在 `LICENSE` 中写明本包许可。

- ZIP 条目数：100。
- 压缩文件大小：20 MiB。
- 解压后总大小：20 MiB。
- 单个 JSON 文件：512 KiB。
- PNG 尺寸：1024 × 1024。
- 精确型号数：1 至 30。
- 属性绑定数：40。
- 动作声明数：20。
- 单个动作的输入或输出参数数：16。
- 全部布局的控件总数：80。

`preview.json` 只保留以下字段：

- `model`
- `specType`
- `schema`

`schema` 可以包含 `properties` 和 `actions`。
不要复制名称、房间、设备 ID、令牌或读数。

## 定义清单

下面是一个完整的 `adapter.json` 示例：

```json
{
  "formatVersion": 1,
  "id": "example.thermometer",
  "name": "温湿度计",
  "version": "1.0.0",
  "sdkVersion": "0.20.0",
  "models": ["miaomiaoce.sensor_ht.t9"],
  "category": "temperature-humidity-sensor",
  "bindings": [
    {
      "id": "temperature",
      "service": "temperature-humidity-sensor",
      "property": "temperature",
      "type": "float",
      "unit": "celsius",
      "access": "read",
      "capability": "temperature"
    }
  ],
  "views": {
    "card": [{"kind":"metric","label":"温度","binding":"temperature"}],
    "drawer": [{"kind":"metric","label":"温度","binding":"temperature"}],
    "full": []
  }
}
```

`id` 长度为 3 至 80 个字符。
使用小写字母、数字、点或连字符。
首尾必须是字母或数字。

`version` 和 `sdkVersion` 使用三段数字版本号。
`version` 标识适配包版本。
`sdkVersion` 标识开发工具版本。
离线工具会补充未声明的 `sdkVersion`。
旧包未声明时，应用显示“未声明”。

`models` 只接受精确型号。
不支持通配符。
可选字段包括 `author`、`specType` 和 `icon`。
`icon` 必须指向包内 PNG。

`category` 支持以下值：

```text
light, switch, outlet, humidifier, temperature-humidity-sensor,
occupancy-sensor, router, appliance, fan, air-purifier, curtain, speaker
```

## 绑定属性

每个绑定必须包含 `id`、`service`、`property`、`type` 和 `access`。
绑定标识必须唯一。
绑定标识以字母开头，最多使用 40 个字符。
其余字符可以是字母、数字、下划线或连字符。

`service` 和 `property` 使用 URN 的语义名称。
例如，使用 `environment` 和 `relative-humidity`。
`type` 必须与规格一致。
`unit` 默认为空字符串，必须与规格一致。
`access` 只能是 `read` 或 `readWrite`。

存在多个同名服务时，用 `siid` 指定服务实例。
厂商命名空间必须提供完整的 `serviceType` 和 `propertyType`。
复制实际 URN，不要按显示名称生成 URN。
不要按属性列表位置推测 PIID。

宿主会在实际规格中重新匹配绑定。
每个绑定必须唯一匹配一个可读属性。
可写绑定还必须具有实际写权限。
类型、单位、枚举、范围和步长由实际规格决定。
适配包不能覆盖这些约束。

## 声明首页能力

使用可选字段 `capability` 声明首页读数。
支持以下能力名称：

```text
temperature, humidity, battery, filter, power,
electricPower, connected, download, upload
```

宿主会核对能力对应的 MIOT 语义、类型和单位。
不要为缺失数据声明其他属性。
路由器速率使用 B/s，并显示为十进制 MB/s。

## 声明布局

`views` 必须包含 `card`、`drawer` 和 `full`。
`drawer` 必须包含至少一个控件。
`full` 表示整页中增加的内容。

- `card`：最多 2 项；`metric`、`status`、`toggle`。
- `cardExpanded`：最多 2 项，可为空；见展开卡片一节。
- `drawer`：每层最多 12 项；状态和主要控制。
- `full`：每层最多 12 项；高级控制和附加读数。

`group` 最多允许两层嵌套。
全部布局总计不能超过 80 个控件。

每个控件必须包含 `kind` 和 `label`。
属性控件还必须包含 `binding`。
`group` 和 `readings` 使用 `children`。
`action` 使用 `action` 引用动作声明。
这三类控件不使用 `binding`。

可以用 `when` 设置显示条件：

```json
{"kind":"metric","label":"湿度","binding":"humidity","when":{"binding":"power","equals":true}}
```

缺少条件读数时，宿主隐藏该控件。
不得把缺失、离线、0 和 false 当作同一状态。

## 选择控件

- `metric`、`status`：显示属性读数。
- `toggle`：绑定可写布尔属性；切换后提交。
- `slider`：绑定具有实际范围的可写数值。
- `select`、`choice`：绑定可写枚举；选择后提交。
- `progress`：显示具有实际范围的读数。
- `group`：用 `children` 组织控件。
- `brightness`：绑定 `light.brightness`；单位为 `percentage`。
- `colorTemperature`：绑定 `light.color-temperature`；单位为 `kelvin`。
- `color`：绑定 `light.color`；单位为 `rgb`；使用 RGB24。
- `duration`：输入时长；实际单位必须是 `seconds`、`minutes` 或 `hours`。
- `countdown`：显示设备报告的剩余时间。
- `number`：提供独立的精确数值输入。
- `level`：显示 2 至 8 个实际整数档位。
- `gauge`：按实际范围显示只读仪表。
- `text`：显示并复制字符串读数。
- `textInput`：编辑可读写字符串。
- `indicator`：按明确映射显示状态和色调。
- `readings`：用自适应布局组合只读控件。
- `action`：执行已声明的动作。

少量模式优先使用等宽分段选项。
选项较多或空间不足时，宿主使用下拉框。
`presentation` 可设为 `auto`、`segmented` 或 `dropdown`。
`choice`、`select` 和 `level` 支持此字段。
强制分段仍会在空间不足时回退。

一个字段只提供一种主要输入方式。
不要同时显示滑条和数值输入框。

### 数值、颜色与时长

`slider`、`progress`、`brightness`、`colorTemperature` 和 `color` 需要实际数值范围。
`duration`、`countdown`、`number`、`level` 和 `gauge` 也需要实际数值范围。
这些控件不接受布尔、字符串或枚举绑定。
范围的上下限必须不同。
提供步长时，步长必须为正数。
控制项还必须具有实际写权限。

详情页中的数值、颜色和时长先保留本地编辑。
点击“应用”后，宿主提交这些值。
点击“取消”可恢复最近读数。
刷新不得覆盖未提交的编辑。

色温滑条使用实际范围。
高对比度模式使用系统滑条。
颜色选择不提供透明度。
颜色控件不支持 HSV、RGBW 或多属性写入。

`level` 只接受具有正整数步长的整数范围。
例如，`[1,5,1]` 可生成五个档位。
`[1,100,1]` 不适用于此控件。
枚举属性应使用 `choice`。

### 设备倒计时

`countdown` 必须绑定可读的时间数值。
属性语义必须包含 `countdown`，或为 `left-time`、`remaining-time`。
实际单位必须是秒、分钟或小时。

`activeBinding` 可以引用启用状态的布尔绑定。
false 显示“未启用”。
缺失启用读数时，显示“状态未知”。
0 不代表设备任务已经完成。

宿主只显示设备报告的值。
宿主不在两次读取之间推算倒计时。
宿主不创建本机定时任务。

时长与启用状态是独立操作。
设置时长不会自动启用计时。
如果设备需要动作启动计时，必须另行声明该动作。

### 文本与状态

`text` 最多显示 4096 个 UTF-16 单元。
复制操作保留完整字符串读数。

`textInput` 不接受字符串枚举。
`textInput.maxLength` 默认为 128。
允许值为 1 至 256，按 UTF-16 单元计算。
提交内容不能包含控制字符。
同一绑定有多个编辑器时，使用最小长度限制。
不得将设备 JSON 配置字段当作普通名称编辑。

`indicator.states` 的每项包含 `value`、`label` 和 `tone`。
`tone` 可设为 `neutral`、`success`、`warning` 或 `critical`。
映射必须使用真实值，且不能重复。
未映射状态使用中性色调。
缺失值不能显示为成功。

`readings.children` 接受 1 至 6 个只读控件。
只读控件可以是 `metric`、`status`、`gauge`、`text` 或 `indicator`。
不允许控制项或嵌套分组。
宽窗口显示两列，窄窗口显示一列。

## 声明宿主能力

在 `requires` 中列出使用的扩展能力。
宿主不支持某项能力时，会拒绝导入。
`sdkVersion` 不能替代 `requires`。

- `controls.choice`：`choice`。
- `controls.lighting`：亮度、色温和颜色。
- `controls.time`：时长和倒计时。
- `controls.numeric`：`number`、`level`、`gauge`。
- `controls.text`：`text`、`textInput`。
- `controls.indicator`：`indicator`。
- `layout.readings`：`readings`。
- `labels.options`：枚举显示标签。
- `layout.cardExpanded`：展开卡片布局。
- `actions.v1`：MIOT Actions。

## 设置展示语言

展示标签的优先顺序如下：

1. 适配包明确提供的标签。
2. 设备官方中文元数据。
3. 宿主已确认的标准术语。
4. 原始描述。

`choice.options` 和 `select.options` 可修改枚举标签。
每项包含实际 `value` 和显示 `label`。
只能改写已有值的标签。
不得添加值或重复值。

```json
{"kind":"choice","label":"模式","binding":"mode","options":[{"value":10,"label":"自动"}]}
```

`displayName`、`displayService` 和 `displayDescription` 只用于显示。
绑定和控制继续使用原始协议数据。
未知的内部属性应放入高级设置。
不要即时翻译私有字段来推测用途。

## 声明展开卡片

使用 `views.cardExpanded` 定义展开卡片内容。
最多允许两项。
支持以下控件：

```text
metric, status, toggle, slider, brightness, colorTemperature,
choice, select, level, duration, countdown, action
```

不允许表单、分组、长文本或内部滚动条。
同一服务的亮度和色温可以合并为一个切换滑条。
滑条松手后提交。
键盘调整在释放按键后提交。
编辑模式会禁用卡片控制。
窄窗口只保留主要控制。

未声明布局时，宿主可以提供已识别能力的快捷控制。
此行为仍受适配包绑定权限限制。
显式设置 `cardExpanded: []` 可关闭此行为。
没有匹配能力时，宿主保留原卡片布局。

## 声明 MIOT Actions

1. 在 `requires` 中添加 `actions.v1`。
2. 在根级 `actions` 中声明动作。
3. 在布局中添加 `action` 控件。

以下内容是清单字段片段：

```json
{
  "actions": [
    {"id":"pause","service":"play-control","action":"pause","siid":3}
  ]
}
```

动作声明必须包含 `id`、`service` 和 `action`。
可以添加 `siid`、`serviceType` 和 `actionType`。
标准动作可以使用语义名称。
厂商命名空间必须使用实际完整 URN。
匹配必须唯一。
虚拟设备只能调用自己的服务。
仅包含动作的包可以使用空 `bindings`。

动作控件示例：

```json
{"kind":"action","label":"暂停","action":"pause"}
```

动作不是属性写入。
Spec 中的 `in` 和 `out` 列表包含同服务的 PIID。
列表顺序决定参数顺序。
动作参数不要求属性写权限。
宿主不会先写入这些参数属性。

### 参数与结果

没有 `arguments` 时，宿主提供参数表单。
布尔和枚举必须明确选择。
数值必须符合实际类型、范围和步长。
字符串最多包含 4096 个 UTF-16 单元。
当前只支持标量参数。
不接受 null。
不支持数组、对象或脚本。

`arguments` 可以提供有序固定参数。
参数数量和每项约束必须匹配实际规格。
超过 NumberBox 精确整数范围时，不提供手动输入。
此类参数可以使用已核对的 JSON 整数预设。

执行前，宿主显示目标、动作和参数。
用户确认后，宿主只发送一次。
结果分为已接受、拒绝和待确认。
已接受不代表设备任务完成。
输出参数按实际规格校验并显示。

结果未知时，宿主不自动重试。
此时按钮会停用。
先检查设备状态，再决定是否重新打开详情。
同一请求标识在当前后端连接中不会再次发送。
重新连接后不保留去重记录。

### 卡片动作限制

展开卡片只接受无输入参数的标准常用动作。
当前允许的语义如下：

```text
play, pause, stop, previous, next, resume-cook, stop-alarm
```

带参数、私有、重置和开始烹饪动作必须放在详情中。
自清洁操作也必须放在详情中。
宿主会检查当前适配包、设备状态、模式和相关故障。
这些检查不能替代设备自身的保护机制。

### 小爱音箱

兼容音箱使用默认面板时，宿主提供播放控制和托盘音箱区。
通过 `views` 定义适配包的面板。

- 用 `status` 显示 `play-control.playing-state`。
- 用 `slider` 调节 `speaker.volume`。使用规格的范围和步长。
- 用 `toggle` 切换布尔属性 `speaker.mute`。
- 用 `action` 调用 `play-control` 的 `play`、`pause`、`previous` 和 `next`。
- 用 `action` 调用 `intelligent-speaker.play-text`。输入为字符串 `text-content`。
- 用 `action` 调用 `intelligent-speaker.execute-text-directive`。按规格顺序提供 `text-content` 和布尔值 `silent-execution`。

先导出目标音箱的规格。
只声明规格中可唯一匹配的属性和动作。
音箱的 `siid` 和动作输入可能不同。
将文字动作放在 `drawer` 或 `full`。
省略 `arguments`，让用户输入文字并确认发送。
`textInput` 用于可读写属性。它不能提供动作的输入。
`views.cardExpanded` 可以放置音量滑条和无输入的播放动作。

以下示例适用于小米 AI 音箱（第二代）的指定规格。

```json
{
  "formatVersion": 1,
  "id": "example.ai-speaker",
  "name": "小米 AI 音箱示例",
  "version": "1.0.0",
  "models": ["xiaomi.wifispeaker.l15a"],
  "specType": "urn:miot-spec-v2:device:speaker:0000A015:xiaomi-l15a:2",
  "category": "speaker",
  "requires": ["actions.v1", "layout.cardExpanded"],
  "bindings": [
    {"id":"state","service":"play-control","property":"playing-state","siid":3,"type":"uint8","unit":"none","access":"read"},
    {"id":"volume","service":"speaker","property":"volume","siid":2,"type":"uint8","unit":"percentage","access":"readWrite"},
    {"id":"mute","service":"speaker","property":"mute","siid":2,"type":"bool","access":"readWrite"}
  ],
  "actions": [
    {"id":"play","service":"play-control","action":"play","siid":3},
    {"id":"pause","service":"play-control","action":"pause","siid":3},
    {"id":"speak","service":"intelligent-speaker","action":"play-text","siid":7},
    {"id":"directive","service":"intelligent-speaker","action":"execute-text-directive","siid":7}
  ],
  "views": {
    "card": [{"kind":"status","label":"播放状态","binding":"state"}],
    "cardExpanded": [
      {"kind":"slider","label":"音量","binding":"volume"},
      {"kind":"action","label":"暂停","action":"pause"}
    ],
    "drawer": [
      {"kind":"slider","label":"音量","binding":"volume"},
      {"kind":"toggle","label":"静音","binding":"mute"},
      {"kind":"action","label":"播放","action":"play"},
      {"kind":"action","label":"暂停","action":"pause"},
      {"kind":"action","label":"朗读文本","action":"speak"},
      {"kind":"action","label":"执行文字指令","action":"directive"}
    ],
    "full": []
  }
}
```

## 验证和交付

1. 创建预览文件：

```powershell
python tools/adapter_tool.py preview-from-export device-spec.json examples/my-adapter/preview.json
```

2. 打包源码文件夹：

```powershell
python tools/adapter_tool.py pack examples/my-adapter artifacts/my-adapter.mihome-adapter
```

3. 校验生成的包：

```powershell
python tools/adapter_tool.py validate artifacts/my-adapter.mihome-adapter
```

4. 在应用中导入生成的包。
5. 使用模拟预览检查布局和控件。
6. 核对适用型号和替换版本。
7. 确认安装。

离线工具与应用使用同一包验证器。
编辑器可关联 `sdk/adapter.schema.json`。
编辑器检查不能替代宿主规格校验。
不要将字段 Schema 放入适配包。

预览支持卡片、展开卡片、抽屉和整页。
预览状态包含正常、关闭、离线和缺失读数。
错误枚举和故障状态尚无完整的专门注入入口。

预览操作只修改内存中的模拟状态。
预览不连接设备，也不证明真机兼容。
适配不兼容时，宿主恢复内置显示路径。
没有内置专属面板时，宿主显示通用属性。

## 示例与限制

- `examples/adapters/thermometer`：温湿度读数和电池仪表。
- `examples/adapters/outlet`：插座状态和控制。
- `examples/adapters/humidifier`：湿度、模式和自清洁动作。
- `examples/adapters/lighting`：亮度、色温和 RGB24 颜色。
- `examples/adapters/countdown`：时长、启用状态和剩余时间。
- `examples/adapters/numeric`：音量、提示音档位和音箱动作。
- `examples/adapters/appliance-actions`：空气炸锅状态和暂停动作。
- `examples/adapters/display-controls`：虚构设备的文本与状态布局。
- `examples/adapters/action-demo`：虚构设备的动作参数和返回值。

示例只覆盖声明的功能。
虚构设备只用于模拟预览。
不得复制虚构设备绑定来声明真实型号兼容。

SDK 不支持 MIOT Events、视频或自定义历史图表。
SDK 不提供本机任务调度和可执行扩展。
不要用相似属性代替缺失能力。
