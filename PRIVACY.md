# DeskTemp 隐私政策

DeskTemp 是一个完全本地运行的 Windows 桌面小部件。

## 收集的数据

不收集、不上传任何个人数据。

- 不包含遥测、分析、广告或崩溃上报。
- 不连接网络；没有账号系统，也没有云端同步。
- 启动时只读取本机的硬件传感器（CPU、GPU、主板、硬盘），以及 Windows 提供的系统内存信息。
- 设置保存在本机 `%APPDATA%\DeskTemp\settings.json`，包括窗口位置、置顶/锁定状态和「显示硬盘温度」开关。
- 卸载时会删除安装目录；上述设置文件可在卸载后手动删除。

## 第三方组件

硬件传感器来自 [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)（MPL-2.0），其他依赖及许可证见 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。
这些组件同样在本地运行，不会向网络发送数据。

## 联系方式

如有隐私相关问题，请在 [GitHub Issues](https://github.com/yanlouk/DeskTemp/issues) 中提出。
