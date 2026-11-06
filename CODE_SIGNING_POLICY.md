# DeskTemp 代码签名政策（Code signing policy）

**Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).**

## 签名范围

DeskTemp 的 Windows 发布物通过 **SignPath Foundation** 的免费开源代码签名服务签名：

- `DeskTempSetup-<version>.exe`（Inno Setup 安装包）
- 安装包内的 `DeskTemp.exe`（主程序）

签名使用 SignPath Foundation 持有的 OV 级代码签名证书。证书主题是 **SignPath Foundation**，因此 Windows 文件属性中的「发布者」显示为 SignPath Foundation，而不是个人维护者。私钥保存在 SignPath 的 HSM 中，不进入本仓库、CI 日志或维护者电脑。

## 构建来源

- 源码仓库：<https://github.com/yanlouk/DeskTemp>（公开）
- 构建环境：GitHub Actions 的 GitHub 托管 `windows-latest` runner
- 构建入口：`.github/workflows/build-installer.yml`
- 只对从公开、已审核的 tag 构建出的产物发起签名请求；不接受本地构建或来源不明的二进制。
- 构建产物先作为 GitHub Actions artifact 上传，再由 SignPath GitHub App 验证来源后提交签名。

## 发布流程

1. 维护者审核变更并创建版本 tag（例如 `v1.0.1`）。
2. GitHub Actions 从该 tag 构建自包含的 .NET 10 应用和 Inno Setup 安装包。
3. 未签名安装包作为 workflow artifact 上传。
4. 维护者在 SignPath 控制台中**手动批准**该次签名请求。
5. SignPath 返回签名后的安装包，发布到对应的 GitHub Release。
6. Release 说明中公布安装包的 SHA-256。

签名请求不会完全自动批准；每次发布都需要一次人工确认。

## 角色（Team roles）

当前是单人维护项目，GitHub 账号为 [@yanlouk](https://github.com/yanlouk)：

- **Authors**：Vaidurya（[@yanlouk](https://github.com/yanlouk)）— 提交代码、创建 tag。
- **Reviewers**：Vaidurya（[@yanlouk](https://github.com/yanlouk)）— 发布前审阅 diff、构建结果和版本说明；单人项目采用发布前自查，所有外部 PR 必须先审阅。
- **Approvers**：Vaidurya（[@yanlouk](https://github.com/yanlouk)）— 在 SignPath 控制台中逐次手动批准签名请求。

维护者的 GitHub 账号已启用 MFA；SignPath 账号同样要求启用 MFA。

## 产物元数据

主程序在 `DeskTemp.csproj` 中固定：

- Product name：`DeskTemp`
- Product version / File version / Assembly version：`1.0.0` / `1.0.0.0`
- Company：`Vaidurya`

每次发布必须保持产品名一致，并把版本号更新为同一个值。

## 隐私

本项目是纯本地应用，不会向其他网络系统传输任何信息，除非用户明确要求。完整说明见 [PRIVACY.md](PRIVACY.md)。

## 验证

下载安装包后可在 Windows 上验证：

```powershell
Get-AuthenticodeSignature .\DeskTempSetup-1.0.0.exe | Format-List
```

或使用 Windows SDK 的 `signtool`：

```powershell
signtool verify /pa /v .\DeskTempSetup-1.0.0.exe
```

签名只能证明文件来自 SignPath Foundation 的构建/签名流程且未被篡改；SmartScreen 信誉仍会随下载量积累，首次发布可能仍出现提示。

## 当前状态

`v1.0.0` 安装包**尚未签名**；项目正在申请 SignPath Foundation。获批并接入签名后，后续 Release 将使用上述流程。

## 联系

签名相关问题请在 <https://github.com/yanlouk/DeskTemp/issues> 提出。
