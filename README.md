# 鲸娘钱包 · Whale Pocket

Windows 桌面余额小挂件。奶蓝玻璃外观、三款鲸娘立绘，可分别选择显示的账户；主程序使用 WinForms/GDI，设置按需打开为独立 WPF 进程。Node.js 仅在刷新时运行，不需要 npm 安装。

## 选择显示的账户

1. 在挂件或鲸娘上右键 → **显示账户**。
2. 取消 **MoMo API** 的勾选，该账户整行立即消失，窗口随之缩小；重新勾选即可恢复。
3. 也可以右键 → **账户与外观**，使用各账户的开关。设置会明确标注“显示中”或“已隐藏”。

可全部显示或全部隐藏。全部隐藏时仍保留鲸娘和右键入口。选择自动保存到本机 `config.json`，刷新、重新扫描和重启会保留。隐藏只影响展示，后台仍按刷新间隔读取已发现账户，以便保留充值进度基准。新发现且未设置过偏好的账户默认显示。

左键拖动玻璃条移动；点击鲸娘切换立绘；右键可以刷新、选择立绘、置顶和退出。设置支持 0–90% 玻璃透明度，文字与立绘保持清晰。

## 运行与编译

- Windows 10/11、.NET Framework 4.8（系统通常已带）。
- Node.js **22.13 或更高版本**，加入 PATH；CC Switch 数据库读取使用 Node 内置 `node:sqlite`。也可用 `DSH_NODE` 指定 `node.exe` 的路径。
- 自动查询只会使用在原客户端已保存的凭据；不要求把密钥写入本项目。

**使用已编译版：**解压“可运行版”，双击 `启动.cmd` 或 `WhaleWidget.exe`。首次启动创建 `config.json` 和 `data/`。请放在自己有写入权限的目录。

**从源码编译：**在源码根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\编译.ps1
.\bin\启动.cmd
```

脚本调用 Windows 自带 .NET Framework C# 编译器，输出到 `bin/`。不下载组件，不修改注册表、UAC、防火墙或开机启动项。若安全软件提示，请先核对源码、编译过程和文件来源；本项目不修改安全软件设置。

程序为单实例：旧版挂件运行时，请先在旧版右键退出，再打开新版。未自动迁移旧版账户偏好或余额记录，首次启动会按当前本机登录重新扫描。开机启动项仍需自行指向要使用的版本。

## 支持的账户与边界

| 类型 | 数据来源与行为 |
| --- | --- |
| DeepSeek | DSH 已保存登录或 DeepSeek API Key，查询真实余额 |
| MoMo / 部分中转站 | 优先 `/v1/usage`；适配 New API 的令牌或账户额度接口；未支持的接口显示不可用 |
| Codex 订阅 | 本机 Codex 登录，经 `codex app-server` 查询额度；API 登录不能提供订阅余量 |
| Claude Code 订阅 | 本机 `.credentials.json` 中 OAuth 登录；系统钥匙串尚未适配 |
| OpenAI API | 普通 Key 不提供预付余额；检测到 Admin Key 时查询当月费用，有本地预算才显示预算余量 |

只读发现 Codex、CC Switch、DSH、Claude 的现有配置与相关环境变量；不修改这些客户端。不会把当前全局凭据发送给其它未激活的中转站。请求只允许 HTTPS，禁止带认证请求自动重定向。

充值型钱包的进度为“当前余额 / 本轮余额基准”。首次启用以当前余额为基准；后续检测到入账时更新分母。消费不改变分母。轮询只能记录检测时的余额，赠金或退款也可能被识别为入账。

本机已验证 DeepSeek 与 MoMo 查询；其它适配器有离线解析测试，但未承诺所有服务商、登录方式和未来接口都可用。

## 开源内容与隐私

本仓库包含源码、构建脚本、无凭据配置示例、立绘及其许可证、离线测试。没有用户密钥、真实余额、充值基准、账户缓存、日志或机器绝对路径。未打包 Node/.NET，运行环境由使用者提供。

运行时 `config.json`、`data/`、`bin/` 和日志均已写入 `.gitignore`。分享修改后的代码时，请继续排除这些本机文件；GitHub Releases 可另上传干净的可运行版。

开发披露：本项目程序代码与说明主要由 OpenAI Codex 按需求生成和迭代。发布者负责需求、运行反馈与发布选择；不将 AI 生成代码描述为人工逐行编写，也不把第三方立绘宣称为自行绘制。

项目代码采用 MIT 许可，见 [LICENSE](LICENSE)。鲸娘立绘来自 [oujnit/jinglan-whale-maid-pet](https://github.com/oujnit/jinglan-whale-maid-pet)，保留原 MIT 声明；细节见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## 开发验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\验证.ps1
```

测试使用虚构账户和余额，在 `test-results/` 的独立目录验证 MoMo 菜单隐藏/恢复、全部隐藏、窗口高度、保存与重启恢复、设置开关联动；另外验证凭据绑定、账户去重、金额解析与充值基准。测试不读取本机真实登录，不发出余额网络请求。

主要文件：`WhaleWidget.cs` 为轻量主窗口；`WhalePocket.cs` 与 `界面.xaml` 为设置界面；`AccountPreferences.cs` 为两个进程共享的账户展示偏好；`accounts.mjs` 只读发现账户；`auto-poller.mjs` 查询；`recharge.mjs` 管理充值进度。
