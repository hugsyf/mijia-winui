# 设备卡片适配

适用应用版本：0.18.0。
文档修订日期：2026-11-08。
下表规格来源在 2026-11-03 核对。

## 适配范围

应用保留 31 个精确型号规则。
规则位于 `backend/device_profiles.json`。
能力模板位于 `backend/device_templates.json`。
前后端读取同一份规则。

精确型号规则优先于能力模板。
运行时使用设备实际返回的 `specType`。
下表链接只标识核对时的公开规格。
它们不会替换设备实际规格。

| 精确型号 | 规则类别 | 核对规格 |
| --- | --- | --- |
| `xiaomi.switch.w2` | switch | [MIoT 2](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Aswitch%3A0000A003%3Axiaomi-w2%3A2%3A0000C809) |
| `chuangmi.camera.81ac1` | camera | [MIoT 4](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Acamera%3A0000A01C%3Achuangmi-81ac1%3A4) |
| `cuco.plug.v3` | outlet | [MIoT 2](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Aoutlet%3A0000A002%3Acuco-v3%3A2) |
| `linp.sensor_ht.ks2bb` | temperature-humidity-sensor | [MIoT 1](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Atemperature-humidity-sensor%3A0000A00A%3Alinp-ks2bb%3A1) |
| `linp.sensor_occupy.es2` | occupancy-sensor | [MIoT 1](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Aoccupancy-sensor%3A0000A0BF%3Alinp-es2%3A1) |
| `xiaomi.humidifier.600` | humidifier | [MIoT 2](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Ahumidifier%3A0000A00E%3Axiaomi-600%3A2%3A0000D061) |
| `soocare.toothbrush.1501` | toothbrush | [MIoT 1](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Atoothbrush%3A0000A07E%3Asoocare-1501%3A1) |
| `miaomiaoce.sensor_ht.t9` | temperature-humidity-sensor | [MIoT 1](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Atemperature-humidity-sensor%3A0000A00A%3Amiaomiaoce-t9%3A1) |
| `xiaomi.fryer.maf16` | air-fryer | [MIoT 2](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Aair-fryer%3A0000A0A4%3Axiaomi-maf16%3A2%3A0000D043) |
| `xiaomi.router.rn07` | router | [MIoT 1](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Arouter%3A0000A036%3Axiaomi-rn07%3A1) |
| `xiaomi.switch.w1` | switch | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Aswitch%3A0000A003%3Axiaomi-w1%3A2%3A0000C808) |
| `xiaomi.switch.w3` | switch | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Aswitch%3A0000A003%3Axiaomi-w3%3A2%3A0000C810) |
| `zimi.switch.dhkg01` | switch | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Aswitch%3A0000A003%3Azimi-dhkg01%3A2%3A0000C808) |
| `zimi.switch.dhkg02` | switch | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Aswitch%3A0000A003%3Azimi-dhkg02%3A2%3A0000C809) |
| `zimi.switch.dhkg05` | switch | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Aswitch%3A0000A003%3Azimi-dhkg05%3A1%3A0000C810) |
| `giot.switch.v6oodm` | switch | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Aswitch%3A0000A003%3Agiot-v6oodm%3A1%3A0000D00D) |
| `yeelink.light.bslamp2` | light | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Alight%3A0000A001%3Ayeelink-bslamp2%3A2) |
| `yeelink.light.lamp22` | light | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Alight%3A0000A001%3Ayeelink-lamp22%3A1%3A0000C802) |
| `yeelink.light.lamp27` | light | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Alight%3A0000A001%3Ayeelink-lamp27%3A1) |
| `leishi.light.wy0a09` | light | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Alight%3A0000A001%3Aleishi-wy0a09%3A1%3A0000C802) |
| `miaomiaoce.sensor_ht.t2` | temperature-humidity-sensor | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Atemperature-humidity-sensor%3A0000A00A%3Amiaomiaoce-t2%3A1) |
| `xiaomi.sensor_ht.new3pr` | temperature-humidity-sensor | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Atemperature-humidity-sensor%3A0000A00A%3Axiaomi-new3pr%3A1%3A0000D063) |
| `linp.sensor_occupy.es5b` | occupancy-sensor | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Aoccupancy-sensor%3A0000A0BF%3Alinp-es5b%3A1) |
| `xiaomi.sensor_occupy.03` | occupancy-sensor | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Aoccupancy-sensor%3A0000A0BF%3Axiaomi-03%3A3) |
| `isa.magnet.dw2hl` | magnet-sensor | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Amagnet-sensor%3A0000A016%3Aisa-dw2hl%3A1) |
| `linp.magnet.m1` | magnet-sensor | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Amagnet-sensor%3A0000A016%3Alinp-m1%3A1) |
| `lumi.curtain.hmcn02` | curtain | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Acurtain%3A0000A00C%3Alumi-hmcn02%3A1%3A0000C817) |
| `yunmi.kettle.v19` | kettle | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Akettle%3A0000A009%3Ayunmi-v19%3A2) |
| `xiaomi.wifispeaker.l15a` | speaker | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Aspeaker%3A0000A015%3Axiaomi-l15a%3A2) |
| `xiaomi.wifispeaker.oh2p` | speaker | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Aspeaker%3A0000A015%3Axiaomi-oh2p%3A1) |
| `xiaomi.router.rd03` | router | [MIoT](https://miot-spec.org/miot-spec-v2/instance?type=urn%3Amiot-spec-v2%3Adevice%3Arouter%3A0000A036%3Axiaomi-rd03%3A1) |

规则只选择已确认的状态和控制。
规则不代表完整设备功能。
产品图片不证明设备具备某项控制能力。

## 卡片状态

- 显示设备名称和房间。
- 根据实际能力显示状态或读数。
- 读数缺失时显示未知，不显示为 0。
- 离线时不把旧状态当作当前状态。
- 故障和无线模式会限制控制。
- 云端读取时间不等于设备测量时间。

摄像机电源使用 `camera-control.on`。
指示灯状态不代表摄像机电源。
摄像机卡片不提供视频预览。

插座供电使用 `switch.on`。
充电保护、指示灯和倒计时开关不代表供电状态。

存在传感器使用实际枚举值。
`occupancy-status=0` 表示无人。
该属性不是布尔值。

牙刷卡片只显示已确认的电量和刷头余量。
空气炸锅卡片不提供开始烹饪快捷操作。
适配包可在详情中声明实际支持的动作。

## 开关通道与用途

完整拆分的单键、双键或三键开关隐藏重复本体。
通道不完整时，列表保留本体入口。
先确定完整通道，再执行房间和搜索筛选。

通道通过 `.sN` 对应服务实例。
实际请求使用父设备 ID 和对应 SIID。
无法匹配服务时，不改用其他通道。

用途来自云端 `subclass_id` 或本机显示设置。
官方编号表见[用途图标](device-purpose-icons.md)。
显示顺序 `rank` 不等于用途编号 `id`。
旧截图推断已由目录记录替换。

用途外观不改变协议能力。
无线通道不提供供电控制。
未知用途使用产品图片或类别图标。
应用不根据设备名称推测用途。

本机外观按账号和完整设备 ID 保存。
云端用途编号变化后，旧外观覆盖会失效。
本机设置不调用云端修改用途接口。

## 能力模板

| 类别 | 可提供的内容 | 约束 |
| --- | --- | --- |
| 灯具 | 电源、亮度、色温 | 使用实际类型、单位和范围 |
| 温湿度计 | 温度、湿度、电量 | 温湿度来自可明确识别的服务 |
| 插座 | 电源、功率 | 未确认模式会限制供电控制 |
| 风扇 | 档位、摇头等快捷控制 | 只使用实际属性 |
| 空气净化器 | 档位和模式等快捷控制 | 只使用实际属性 |
| 窗帘 | 开合度和电机选项 | 不推测枚举含义 |
| 音箱 | 音量和状态 | 不推测厂商私有操作 |

模板按服务、属性、类型和单位匹配。
它们不承诺每类设备都有独立详情页。
有多个候选服务时，模板会回退。
缺少可选能力时，宿主隐藏对应控件。
故障和模式元数据即使不可读，也会保留。

展开卡片最多显示两项内容。
具体控制见[适配包开发指南](../backend/adapter-agent-guide.md)。
在设备页进入编辑模式，可调整卡片尺寸和排序。

## 路由器速率

路由器速率使用 B/s。
应用按十进制换算为 MB/s。
例如，`6343037` 显示为 `6.343 MB/s`。

两个速率字段的公开范围与实际读数不符。
应用仅对这两个只读字段忽略该范围。
读数仍必须有效、非负且读取成功。
其他属性继续使用规格范围。
