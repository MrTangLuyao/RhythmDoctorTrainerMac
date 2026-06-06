# 节奏医生 修改器 · macOS 版 · Rhythm Doctor Trainer (macOS)

《节奏医生》(Rhythm Doctor) 的**游戏内图形修改器 —— macOS 原生分支**。进任意关卡按 **F3** 呼出菜单：
Autoplay（全程满分自动演奏）、游戏变速、关卡直达、一键解锁、开发者工具等。

> 本分支是 [Cohenjikan/RhythmDoctorTrainer](https://github.com/Cohenjikan/RhythmDoctorTrainer)（Windows / BepInEx 版）的 **macOS 移植**。
> Unity 6 的 macOS 播放器用 `dlopen`/`dlsym` 动态加载 Mono，导致 BepInEx / Doorstop 在 macOS 上**无法注入**；
> 本分支因此**不用 BepInEx**，改为用 Mono.Cecil 把加载器**静态织入游戏自身的启动函数**，原生运行（Apple Silicon arm64 直接可用，**无需 Rosetta**）。

> 注意：**仅供单机自娱与录制使用**。不修改任何在线/对战逻辑，请勿用于排行榜。与 7th Beat Games 无任何关联。
> 本工具**完全免费开源，严禁倒卖**；内置完整性校验，删除或篡改水印会让修改器直接失效。

## 一键安装 / 卸载（懒人版）

打开「**终端**」(Terminal)，把下面一行粘贴进去回车即可。脚本会自动：装 .NET SDK（若无）→ 拉取源码 → 编译 → 备份游戏文件 → 织入加载器。**安装前请先退出游戏。**

**安装 / 更新**
```bash
curl -fsSL https://raw.githubusercontent.com/MrTangLuyao/RhythmDoctorTrainerMac/refs/heads/main/install.sh | bash
```

**卸载 / 还原原版**
```bash
curl -fsSL https://raw.githubusercontent.com/MrTangLuyao/RhythmDoctorTrainerMac/refs/heads/main/uninstall.sh | bash
```

装好后**正常用 Steam 启动游戏**，进任意画面按 **F3** 开/关菜单。

- 自定义游戏路径：`curl -fsSL .../install.sh | MANAGED="/path/.../Data/Managed" bash`
- **幂等**：游戏更新或 Steam「验证文件完整性」会还原游戏 DLL、补丁随之失效 —— 重跑安装命令即可（几秒钟）。

<details><summary>从源码手动安装 / 卸载</summary>

```bash
git clone https://github.com/MrTangLuyao/RhythmDoctorTrainerMac.git
cd RhythmDoctorTrainerMac
./install.sh      # 安装（自动装 .NET SDK → 编译 → 织入）
./uninstall.sh    # 卸载，还原原版
```
</details>

### 验证是否加载
```bash
grep RDTrainerMac "$HOME/Library/Logs/7th Beat Games/Rhythm Doctor/Player.log"
# 期望：  ... v2.20m loaded · ... · Menu key = F3 · patches ok=4 fail=0
```
非关卡画面（标题 / 医院 / 选关…）左上角会显示一个小窗口「✓ 游戏已修改 · 按 F3 打开/关闭 修改器菜单」；打开菜单或进入关卡时自动隐藏。

## 功能

**普通** — Autoplay（保留「完美/JCI」标记）、游戏变速 0.1×–3×（含音高）、放宽判定窗口、无敌、瞬间对白、跳过菜单转场、解锁帧率上限、关闭节拍提示音、秒通/跳过本关、解锁全部关卡。
**关卡直达** — 列出全部关卡，点一下直接进入，绕过 hub 剧情/NPC 限制，配合 Autoplay 录任意关卡。
**开发者** — 开发者模式 / 调试模式、标记通关 / 全关刷 S / 推进全部剧情、解锁全部成就（写入 Steam 账号）/ 关闭成就发放、打开/删除存档、杂项调试标志。
**高级** — 音画校准、无限模式记录、展会(Booth)模式、狗狗模式、隐藏曲 Song of the Sea。

## 使用

1. 正常通过 **Steam** 启动游戏，进任意关卡按 **F3** 开/关菜单。
2. 录制完美通关：「普通」页开 **Autoplay** → 进关卡 → OBS 录屏。
3. 录被剧情锁的关卡：用「**关卡直达**」页点关卡名直接进。
4. **变速**：拖滑块后需在关卡**开始/重开**时生效（菜单内有「重开本关并应用」）。

## 原理 / 从源码构建

- 修改器逻辑 `mac/RDTrainerMac/`（普通 MonoBehaviour，无 BepInEx 依赖）→ `RDTrainerMac.dll`
- 运行时补丁库 `0Harmony.dll`（Lib.Harmony 2.4.2，自包含；2.4 起原生支持 Apple Silicon arm64）
- 织入器 `mac/Patcher/`（Mono.Cecil；幂等，总是从纯净备份重打，绝不重复注入）
- 注入点：游戏自身的 `RDStartup.Setup` 开头插入一句 `RDTrainerMac.Loader.Init()`

技术细节见 [`mac/README.md`](mac/README.md)。`./install.sh` 已封装全部构建步骤。

## 兼容性

| 项 | 值 |
|---|---|
| 游戏 | Rhythm Doctor（Steam 正式版，已测 r42 / 6000.3.10f1） |
| 引擎 | Unity 6（6000.3.x）/ Mono / 通用二进制（x64 + arm64） |
| 注入 | Mono.Cecil 静态织入（**不需** BepInEx / Doorstop / Rosetta） |
| 运行时补丁 | Lib.Harmony 2.4.2 |

## 免责声明

- **非官方**：粉丝制作的非官方第三方工具，与开发商 [7th Beat Games](https://rhythmdr.com/) 无任何关联，亦未获授权或认可。游戏及其名称、商标、美术与音乐等权利归 7th Beat Games 所有。
- **不含游戏内容**：本仓库仅含作者编写的代码，不分发游戏的任何源代码、DLL、音频、图像或素材；运行时只调用游戏自身已存在的公开函数。安装时改写的是本机游戏的 `Assembly-CSharp.dll`（已自动备份，可一键还原）。
- **仅限单机**：仅供离线单机自娱、练习与录制，请勿用于在线/排行榜/对战或任何影响他人公平性的场景。
- **遵守 EULA**：修改游戏可能违反其 EULA / 服务条款，是否使用由你自行决定并自负后果（账号处罚、存档损坏等）。
- **成就写入账号**：「解锁全部成就」会写入 Steam 账号，介意者请配合「关闭成就发放」。
- **风险自负**：按「现状」提供，不附带任何担保。
- **完全免费**：免费、开源（[MIT](LICENSE)），严禁倒卖。

## 致谢

- 原作者 / 上游：[Cohenjikan/RhythmDoctorTrainer](https://github.com/Cohenjikan/RhythmDoctorTrainer)（Windows / BepInEx 版）。
- 运行时补丁库 [Lib.Harmony](https://github.com/pardeike/Harmony)；织入库 [Mono.Cecil](https://github.com/jbevain/cecil)。
- macOS 分支：[MrTangLuyao/RhythmDoctorTrainerMac](https://github.com/MrTangLuyao/RhythmDoctorTrainerMac)。

本项目以 [MIT](LICENSE) 许可证开源。
