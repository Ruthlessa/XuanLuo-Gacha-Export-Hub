# v1.7.0 更新公告

## 鸣潮目录查找更智能
- 改为按固定客户端目录名 `Wuthering Waves Game` 定位，父目录可以任意，例如 `D:\鸣潮\Wuthering Waves Game`、`D:\Games\mc\Wuthering Waves Game`
- 常见布局优先直接命中：`Wuthering Waves`、`Program Files`、`Program Files (x86)`、`mc`
- 未命中时再扫描各盘符根目录与 `Program Files`，优先进入疑似启动器或游戏目录
- 每层分支上限由 20 提升到 30，盘符根目录文件夹较多时也能找到
- 命中前提是存在 `Client\Saved\Logs`，同名但没有日志的空目录不会被误认

## 鸣潮更新时自动修复目录配置
- 更新阶段如果配置目录失效或尚未配置，会自动查找可用目录
- 找到后立即写回 `app.config.json` 的 `settings.gameLogDirs.wuwa`，下次更新直接使用
- 配置有效时不触发扫描，也不会改写配置
- 查找失败时保留原有配置，并提示「未找到鸣潮游戏目录，请先运行一次游戏」

## 其他
- MiniGetGachaLink 同步目录扫描规则与分支上限
- 关于页版本号改为读取实际安装版本，不再需要随版本手动修改

> 注意：MiniGetGachaLink 生成的 JSON 包含临时抽卡凭证，请勿公开分享。
