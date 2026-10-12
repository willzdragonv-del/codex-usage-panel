# 开源发布

建议仓库名 `codex-usage-panel`，许可 MIT。简介：Windows Codex 用量悬浮球与托盘面板，显示剩余额度、重置时间和重置卡。

本项目仓库：[GitHub](https://github.com/willzdragonv-del/codex-usage-panel)，HTTPS 地址为 `https://github.com/willzdragonv-del/codex-usage-panel.git`。

## 远程仓库

在自己的 GitCode 或 GitHub 账号下创建**公开空仓库**，无需初始化 README、许可或 .gitignore。获取网站给出的实际 HTTPS 克隆地址。

```powershell
# 首次初始化，已有仓库无需重复
git init -b main
# 使用你希望公开的作者信息，建议平台提供的隐私邮箱
git config user.name "你的公开名字"
git config user.email "你的公开提交邮箱"
git add .gitignore .gitattributes README.md LICENSE CHANGELOG.md CONTRIBUTING.md build.ps1 package-source.ps1 src tests docs
git diff --cached --stat
git diff --cached --check
git commit -m "Initial open source release"
# 将下一行地址替换为网站提供的真实克隆地址
git remote add origin https://gitcode.com/你的命名空间/codex-usage-panel.git
git push -u origin main
```

认证由 Git 凭证管理器或平台负责；不要把密码、令牌拼入 URL 或提交到仓库。只有创建和推送成功后才有公开源码地址。

## 源码包

运行 `package-source.ps1`，生成 `artifacts\codex-usage-panel-source.zip`。白名单包含源码、项目、应用图标、文档和许可，排除 SDK、真实账号截图、程序产物、快捷方式、会话记录与历史设计草稿。

## Windows 发布包

1. 从托盘退出运行中的程序，执行 `build.ps1` 或 `build.ps1 -SelfContained`。
2. 将 dist 中的 exe、README、LICENSE 一起打包，标注 Windows x64 和是否自带 .NET。
3. 不包含 `.codex`、settings.json、账号截图、SDK 缓存、测试日志和本机快捷方式。
4. 创建版本 Release，上传程序包，注明仍需单独安装 Codex。

自包含发布需保留 .NET 发布产物自带的许可和第三方声明。当前 exe 未做代码签名。
