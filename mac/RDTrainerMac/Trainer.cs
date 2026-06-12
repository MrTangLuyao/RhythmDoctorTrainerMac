using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace RDTrainerMac
{
    // In-game trainer for Rhythm Doctor (单机), macOS-native. F3 opens an overlay with three
    // tabs: 普通 (gameplay toggles), 存档修改 (irreversible save edits), 关卡直达 (level jump),
    // plus a 旧版菜单 button that swaps in the legacy 4-tab UI until the menu is reopened.
    // This is the BepInEx BaseUnityPlugin ported to a plain MonoBehaviour hosted by Loader.
    public class Trainer : MonoBehaviour
    {
        public const string Guid = "com.cohen.rdtrainer";
        public const string Name = "RD Trainer (节奏医生修改器)";
        public const string Version = "2.20-macf3";

        // 防倒卖水印：此串同时用于「显示」与「启动完整性校验」。改动或删除它会导致 SHA256 校验失败、
        // 整个修改器拒绝工作（不挂补丁、不应用任何功能）。谁删水印谁失效。
        public const string Watermark = "本工具免费开源，严禁倒卖 · FREE · github.com/Cohenjikan/RhythmDoctorTrainer";
        private const string ExpectedSig = "aa5b99cb20d0aee2d25454b831b309f2ac6432c6a41ed393ec43de203b4043c1";
        internal static bool IntegrityOK;

        private static readonly KeyCode MenuKey = KeyCode.F3;

        private bool _menuOpen;
        private int _tab;          // new UI: 0 普通, 1 存档修改, 2 关卡直达
        private bool _legacy;      // 旧版菜单 mode; resets to the new UI when the menu is reopened
        private int _legacyTab;    // legacy UI: 0 普通, 1 开发者, 2 高级, 3 关卡直达
        private Rect _win = new Rect(24, 24, 480, 620);
        private Rect _hintWin = new Rect(20, 20, 300, 70);
        private Vector2 _scroll;
        private string _lvFilter = "";
        private Font _cjk;
        private bool _lastSpeedOverride;

        // local UI state
        private bool _wipeConfirm;
        private bool _showMisc;
        private bool _calLoaded;
        private int _bestRound = 1;       // legacy 高级 tab
        private bool _saveLoaded;
        private string _wlRoundText = "0"; // 举重 best infinite round (keyboard input)
        private string _beansText = "0";   // 武士豆子跳 (Beans Hopper 2-B1) high score
        private bool _paigeStays;          // 佩奇结局分支 (Persistence.GetPaigeEnding)

        private void Awake()
        {
            // Anti-resale integrity gate: the watermark must be intact, or the trainer refuses to
            // function (no Harmony patches, no feature application, no menu). 删/改水印即失效。
            IntegrityOK = Sig(Watermark) == ExpectedSig;
            if (!IntegrityOK)
            {
                Log.Error("完整性校验失败：水印被篡改，修改器已禁用。" +
                          "请从 github.com/Cohenjikan/RhythmDoctorTrainer 获取免费正版。");
                return; // do NOT patch or enable anything
            }

            // Patch each [HarmonyPatch] class independently so a single failure (e.g. an
            // unpatchable method on a given runtime) can't take down the rest of the trainer.
            var harmony = new Harmony(Guid);
            int ok = 0, fail = 0;
            foreach (var t in typeof(Trainer).Assembly.GetTypes())
            {
                if (t.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
                try { harmony.CreateClassProcessor(t).Patch(); ok++; }
                catch (Exception e) { fail++; Log.Error($"patch {t.Name} failed: {e.Message}"); }
            }
            Log.Info($"{Name} v{Version} loaded · {Watermark} · Menu key = {MenuKey} · patches ok={ok} fail={fail}");
        }

        private static string Sig(string s)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] h = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
                var sb = new System.Text.StringBuilder(h.Length * 2);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private void Update()
        {
            if (!IntegrityOK) return;
            try
            {
                if (Input.GetKeyDown(MenuKey))
                {
                    _menuOpen = !_menuOpen;
                    if (_menuOpen) _legacy = false; // reopening always returns to the new UI
                }
                if (_menuOpen) { Cursor.visible = true; Cursor.lockState = CursorLockMode.None; }
                ApplyState();
            }
            catch (Exception e) { Log.Error("Update: " + e); }
        }

        private void ApplyState()
        {
            var ds = DebugSettings.instance;
            bool inGame = false;
            try { inGame = scnGame.instance != null; } catch { }

            if (inGame)
            {
                if (ds.Auto != Cheats.autoplay) ds.Auto = Cheats.autoplay;
            }

            // Speed: the engine applies it at level Start via `RDTime.speed = scnGame.levelSpeed`,
            // which then scales bpm AND the song's pitch consistently. So we arm the static
            // `scnGame.levelSpeed`; it takes effect on the next level start / restart.
            try
            {
                if (Cheats.speedOverride)
                {
                    if (scnGame.levelSpeed != Cheats.speed) scnGame.levelSpeed = Cheats.speed;
                }
                else if (_lastSpeedOverride)
                {
                    scnGame.levelSpeed = 1f;
                }
            }
            catch { }
            _lastSpeedOverride = Cheats.speedOverride;

            if (ds.InstantDialogue != Cheats.instantDialogue) ds.InstantDialogue = Cheats.instantDialogue;
            if (ds.SkipMenuTransitions != Cheats.skipTransitions) ds.SkipMenuTransitions = Cheats.skipTransitions;
            if (ds.UnlimitedFramerate != Cheats.unlimitedFps) ds.UnlimitedFramerate = Cheats.unlimitedFps;
            if (ds.Debug != Cheats.debugMode) ds.Debug = Cheats.debugMode;

            bool wantBeat = !Cheats.muteBeatSounds;
            if (ds.BeatSounds != wantBeat) ds.BeatSounds = wantBeat;
            bool wantAch = !Cheats.noAchievements;
            if (ds.GiveAchievements != wantAch) ds.GiveAchievements = wantAch;

            try { if (RDString.samuraiMode != Cheats.samuraiMode) RDString.samuraiMode = Cheats.samuraiMode; } catch { }
        }

        private void EnsureFont()
        {
            if (_cjk != null) return;
            // CreateDynamicFontFromOSFont silently yields an empty (glyph-less) font if the FIRST
            // name doesn't exist on this OS, so pick only names actually present (macOS reports
            // "Heiti SC" etc., NOT "PingFang SC"). Order = preferred Simplified-Chinese families.
            string[] inst;
            try { inst = Font.GetOSInstalledFontNames() ?? new string[0]; } catch { inst = new string[0]; }
            var prefer = new[] { "PingFang SC", "Heiti SC", "Hiragino Sans GB", "Songti SC", "STSong", "STHeiti", "Arial Unicode MS" };
            var pick = prefer.Where(p => Array.IndexOf(inst, p) >= 0).ToArray();
            try
            {
                _cjk = pick.Length > 0
                    ? Font.CreateDynamicFontFromOSFont(pick, 16)
                    : Font.CreateDynamicFontFromOSFont("Arial Unicode MS", 16);
            }
            catch (Exception e) { Log.Error("font create failed: " + e.Message); }
        }

        private void OnGUI()
        {
            if (!IntegrityOK) return;
            EnsureFont();
            var prev = GUI.skin.font;
            if (_cjk != null) GUI.skin.font = _cjk;

            // Top-left "game modified" hint — drawn as a Window (same mechanism as the menu, which
            // renders text correctly). Fixed position; shown only on non-level scenes while the menu
            // is CLOSED (complementary to the menu). Hidden inside a level.
            if (InNonLevelScene() && !_menuOpen)
                GUILayout.Window(740182, _hintWin, DrawHintWindow, "节奏医生修改器");

            if (_menuOpen)
                _win = GUILayout.Window(740181, _win, DrawWindow, $"节奏医生修改器 v{Version}");
            GUI.skin.font = prev;
        }

        // True on any non-level scene (menu/hub/level-select/editor). In a level scnGame.instance is set.
        private static bool InNonLevelScene()
        {
            try { return scnGame.instance == null; } catch { return true; }
        }

        private void DrawHintWindow(int id)
        {
            GUILayout.Label("✓ 游戏已修改");
            GUILayout.Label($"按 {MenuKey} 打开 / 关闭 修改器菜单");
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            if (_legacy)
            {
                if (GUILayout.Toggle(_legacyTab == 0, " 普通 ", "Button")) _legacyTab = 0;
                if (GUILayout.Toggle(_legacyTab == 1, " 开发者 ", "Button")) _legacyTab = 1;
                if (GUILayout.Toggle(_legacyTab == 2, " 高级 ", "Button")) _legacyTab = 2;
                if (GUILayout.Toggle(_legacyTab == 3, " 关卡直达 ", "Button")) _legacyTab = 3;
            }
            else
            {
                if (GUILayout.Toggle(_tab == 0, " 普通 ", "Button")) _tab = 0;
                if (GUILayout.Toggle(_tab == 1, " 存档修改 ", "Button")) _tab = 1;
                if (GUILayout.Toggle(_tab == 2, " 关卡直达 ", "Button")) _tab = 2;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("免费开源 严禁倒卖 · github.com/Cohenjikan/RhythmDoctorTrainer", Lbl());
            GUILayout.Label("macOS 分支：https://github.com/MrTangLuyao/RhythmDoctorTrainerMac", Lbl());
            GUILayout.Space(4);

            _scroll = GUILayout.BeginScrollView(_scroll);
            if (_legacy)
                switch (_legacyTab) { case 1: DrawLegacyDev(); break; case 2: DrawLegacyAdvanced(); break; case 3: DrawLevels(); break; default: DrawLegacyNormal(); break; }
            else
                switch (_tab) { case 1: DrawSave(); break; case 2: DrawLevels(); break; default: DrawNormal(); break; }
            GUILayout.EndScrollView();

            bool inGame = false; try { inGame = scnGame.instance != null; } catch { }
            GUILayout.Label(inGame ? "● 当前在关卡内" : "○ 当前在菜单/编辑器", Lbl());
            if (_legacy)
            {
                if (GUILayout.Button("新版菜单")) _legacy = false;
            }
            else if (GUILayout.Button("旧版菜单")) { _legacy = true; _legacyTab = 0; }
            GUI.DragWindow(new Rect(0, 0, 10000, 24));
        }

        // ---------------- 普通（新版） ----------------
        private void DrawNormal()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("秒通本关")) Run("WinLevel", () => { var g = scnGame.instance; if (g != null) g.WinLevel(); });
            if (GUILayout.Button("跳过本关")) Run("SkipLevel", () => { var g = scnGame.instance; if (g != null) g.SkipLevel(); });
            if (GUILayout.Button("快速重开")) Run("Restart", () => { var g = scnGame.instance; if (g != null) g.Restart(false); });
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            Cheats.speedOverride = GUILayout.Toggle(Cheats.speedOverride, $" 变速 {Cheats.speed:0.00}x（重开本关生效）");
            if (Cheats.speedOverride)
                Indent(() =>
                {
                    GUILayout.BeginHorizontal();
                    Cheats.speed = GUILayout.HorizontalSlider(Cheats.speed, 0.1f, 3.0f);
                    if (GUILayout.Button("1x", GUILayout.Width(36))) Cheats.speed = 1f;
                    GUILayout.EndHorizontal();
                });

            Cheats.autoplay = GUILayout.Toggle(Cheats.autoplay, " Autoplay 自动演奏");
            if (Cheats.autoplay)
                Indent(() => Cheats.forceFlawless = GUILayout.Toggle(Cheats.forceFlawless, " 保留完美计算"));

            Cheats.noFail = GUILayout.Toggle(Cheats.noFail, " 无敌");
            Cheats.instantDialogue = GUILayout.Toggle(Cheats.instantDialogue, " 瞬间对白");
            Cheats.devMode = GUILayout.Toggle(Cheats.devMode, " 开发者总开关");
            Cheats.debugMode = GUILayout.Toggle(Cheats.debugMode, " 调试模式（Debug Mode）");
            Cheats.samuraiMode = GUILayout.Toggle(Cheats.samuraiMode, " 武士模式");
            try { RDC.booth = GUILayout.Toggle(RDC.booth, " 展会模式（谨慎！会破坏存档）"); } catch { }

            GUILayout.Space(6);
            _showMisc = GUILayout.Toggle(_showMisc, _showMisc ? "▼ 杂项调试标志" : "▶ 杂项调试标志", "Button");
            if (_showMisc)
            {
                var ds = DebugSettings.instance;
                Indent(() =>
                {
                    ds.NoPro = GUILayout.Toggle(ds.NoPro, " NoPro");
                    ds.ForceNoSteamworks = GUILayout.Toggle(ds.ForceNoSteamworks, " ForceNoSteamworks");
                    ds.EmulateMobile = GUILayout.Toggle(ds.EmulateMobile, " EmulateMobile（模拟手机端）");
                    ds.RunningOnSteamDeck = GUILayout.Toggle(ds.RunningOnSteamDeck, " RunningOnSteamDeck");
                    ds.DebugAmbience = GUILayout.Toggle(ds.DebugAmbience, " DebugAmbience");
                    ds.PauseOnFocusLost = GUILayout.Toggle(ds.PauseOnFocusLost, " PauseOnFocusLost（失焦暂停）");
                });
            }
        }

        // ---------------- 存档修改（新版） ----------------
        private void DrawSave()
        {
            GUILayout.Label("⚠ 存档修改无法撤回 谨慎修改", Warn());
            GUILayout.Space(4);
            if (!_saveLoaded) LoadSaveFields();

            if (GUILayout.Button("标记通关"))
                Run("SetIsGameDone", () => { Persistence.SetIsGameDone(true); Persistence.SaveAll(); });
            if (GUILayout.Button("存档全 S+"))
                Run("AllLevelsToSPlus", SetAllLevelsToSPlus);
            if (GUILayout.Button("一键推进剧情"))
            { Run("RevealStory", RevealAllStory); _paigeStays = true; }
            if (GUILayout.Button("解锁全部成就（写入 Steam）"))
                Run("UnlockAllAchievements", UnlockAllAchievements);

            bool paige = GUILayout.Toggle(_paigeStays, " 佩奇留下（结局分支，含剧透，改动立即写入存档）");
            if (paige != _paigeStays)
            {
                _paigeStays = paige;
                Run("SetPaigeEnding", () => { Persistence.SetPaigeEnding(paige); Persistence.SaveAll(); });
            }

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Label("举重最佳轮数", GUILayout.Width(110));
            _wlRoundText = GUILayout.TextField(_wlRoundText ?? "", GUILayout.Width(70));
            if (GUILayout.Button("读取", GUILayout.Width(56)))
                Run("GetBestRound", () => _wlRoundText = Persistence.GetBestInfiniteRound().ToString());
            if (GUILayout.Button("写入", GUILayout.Width(56)))
                Run("SetBestRound", () =>
                {
                    int n = ParseCount(_wlRoundText);
                    Persistence.SetRhythmWeightlifterBestInfiniteRound(n);
                    Persistence.SaveAll();
                });
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("豆子跳最高分", GUILayout.Width(110));
            _beansText = GUILayout.TextField(_beansText ?? "", GUILayout.Width(70));
            if (GUILayout.Button("读取", GUILayout.Width(56)))
                Run("GetBeansScore", () => _beansText = Persistence.GetLevelScore(BeansLevel).ToString());
            if (GUILayout.Button("写入", GUILayout.Width(56)))
                Run("SetBeansScore", () =>
                {
                    int n = ParseCount(_beansText);
                    Persistence.SetLevelScore(BeansLevel, n, true);
                    Persistence.SaveAll();
                });
            GUILayout.EndHorizontal();
            GUILayout.Label("武士豆子跳 = 奖励关 2-B1 Beans Hopper，最高分显示在传呼机上", Lbl());

            GUILayout.Space(8);
            _wipeConfirm = GUILayout.Toggle(_wipeConfirm, " 我确认要删除全部存档");
            GUI.enabled = _wipeConfirm;
            if (GUILayout.Button("🗑 删除全部存档（不可恢复）"))
            { Run("DeleteSavedData", () => Persistence.DeleteSavedData()); _wipeConfirm = false; }
            GUI.enabled = true;
        }

        private static string BeansLevel => Level.BeansHopper.ToString();

        private static int ParseCount(string text)
        {
            int n;
            if (!int.TryParse((text ?? "").Trim(), out n) || n < 0)
                throw new Exception("请输入 0 或正整数");
            return n;
        }

        private void LoadSaveFields()
        {
            _saveLoaded = true;
            try { _wlRoundText = Persistence.GetBestInfiniteRound().ToString(); } catch { _wlRoundText = "0"; }
            try { _beansText = Persistence.GetLevelScore(BeansLevel).ToString(); } catch { _beansText = "0"; }
            try { _paigeStays = Persistence.GetPaigeEnding(); } catch { _paigeStays = false; }
        }

        // ---------------- 旧版 · 普通玩家 ----------------
        private void DrawLegacyNormal()
        {
            Section("录制 / 演示");
            Cheats.autoplay = GUILayout.Toggle(Cheats.autoplay, " Autoplay — 全程满分自动演奏（进关卡生效）");
            Indent(() => Cheats.forceFlawless = GUILayout.Toggle(Cheats.forceFlawless, " 保留「完美/JCI」结算标记"));

            GUILayout.Space(6);
            Cheats.speedOverride = GUILayout.Toggle(Cheats.speedOverride, $" 游戏变速：{Cheats.speed:0.00}x（含音高）");
            Indent(() => {
                Cheats.speed = GUILayout.HorizontalSlider(Cheats.speed, 0.1f, 3.0f);
                if (GUILayout.Button("1x", GUILayout.Width(36))) Cheats.speed = 1f;
            });
            Indent(() => {
                GUILayout.Label("⚠ 在关卡开始/重开时生效，无法中途变速（引擎限制）", Lbl());
                if (GUILayout.Button("重开本关并应用"))
                    Run("Restart", () => { var g = scnGame.instance; if (g != null) g.Restart(false); });
            });

            GUILayout.Space(6);
            Cheats.widenJudge = GUILayout.Toggle(Cheats.widenJudge, $" 放宽判定窗口 ×{Cheats.judgeMult:0.0}");
            Indent(() => Cheats.judgeMult = GUILayout.HorizontalSlider(Cheats.judgeMult, 1f, 10f));

            GUILayout.Space(8);
            Section("便利 / 玩法");
            Cheats.noFail = GUILayout.Toggle(Cheats.noFail, " 无敌 — 不会失败/被打断");
            Cheats.instantDialogue = GUILayout.Toggle(Cheats.instantDialogue, " 瞬间对白 — 跳过剧情文本");
            Cheats.skipTransitions = GUILayout.Toggle(Cheats.skipTransitions, " 跳过菜单转场");
            Cheats.unlimitedFps = GUILayout.Toggle(Cheats.unlimitedFps, " 解锁帧率上限");
            Cheats.muteBeatSounds = GUILayout.Toggle(Cheats.muteBeatSounds, " 关闭节拍提示音");

            GUILayout.Space(8);
            Section("关卡控制（需在关卡内）");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("秒通本关")) Run("WinLevel", () => { var g = scnGame.instance; if (g != null) g.WinLevel(); });
            if (GUILayout.Button("跳过本关")) Run("SkipLevel", () => { var g = scnGame.instance; if (g != null) g.SkipLevel(); });
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            Section("解锁");
            if (GUILayout.Button("解锁全部关卡（写入存档）"))
                Run("UnlockAllLevels", UnlockAllLevelsForce);
            GUILayout.Label("点完请退出选关界面再重新进入（或重启游戏）刷新显示；无需删档。", Lbl());

            GUILayout.Space(8);
            Section("彩蛋");
            Cheats.samuraiMode = GUILayout.Toggle(Cheats.samuraiMode, " 武士文本模式（SAMURAI.）");
        }

        // ---------------- 旧版 · 开发者 ----------------
        private void DrawLegacyDev()
        {
            Section("开发者模式");
            Cheats.devMode = GUILayout.Toggle(Cheats.devMode, " 开发者模式总开关（isDev=true）");
            GUILayout.Label("开后游戏内可用 Ctrl+Home 或输入 DESPACIT0 切换调试，并解锁部分 dev 工具。", Lbl());

            GUILayout.Space(6);
            Cheats.debugMode = GUILayout.Toggle(Cheats.debugMode, " 调试模式（Debug）");
            GUILayout.Label("⚠ 会在画面显示调试文字，不适合干净录制。", Lbl());

            GUILayout.Space(8);
            Section("进度 / 评级");
            if (GUILayout.Button("标记游戏通关（SetIsGameDone）"))
                Run("SetIsGameDone", () => Persistence.SetIsGameDone(true));
            if (GUILayout.Button("全部关卡刷成 S 评级"))
                Run("AllLevelsToS", SetAllLevelsToS);
            if (GUILayout.Button("一键推进全部剧情（铺满 hub 角色）"))
                Run("RevealStory", RevealAllStory);
            GUILayout.Label("把所有过场/章节标志设为已播放，让 hub 里角色就位；点完退菜单重进。含 Paige 结局分支。\n（想直接录关卡，更推荐用「关卡直达」标签，无需改剧情）", Lbl());

            GUILayout.Space(8);
            Section("成就");
            Cheats.noAchievements = GUILayout.Toggle(Cheats.noAchievements, " 关闭成就发放（防污染账号）");
            if (GUILayout.Button("⚠ 解锁全部成就（写入 Steam 账号！）"))
                Run("UnlockAllAchievements", UnlockAllAchievements);

            GUILayout.Space(8);
            Section("存档");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("打开存档目录"))
                Run("OpenSaveDir", () => RDUtils.RevealInExplorer(Persistence.DataPath));
            GUILayout.EndHorizontal();
            _wipeConfirm = GUILayout.Toggle(_wipeConfirm, " 我确认要删除全部存档");
            GUI.enabled = _wipeConfirm;
            if (GUILayout.Button("🗑 删除全部存档（不可恢复）"))
            { Run("DeleteSavedData", () => Persistence.DeleteSavedData()); _wipeConfirm = false; }
            GUI.enabled = true;

            GUILayout.Space(8);
            _showMisc = GUILayout.Toggle(_showMisc, _showMisc ? "▼ 杂项调试标志" : "▶ 杂项调试标志", "Button");
            if (_showMisc)
            {
                var ds = DebugSettings.instance;
                GUILayout.Label("（dev/冷门，直接读写 DebugSettings）", Lbl());
                ds.NoPro = GUILayout.Toggle(ds.NoPro, " NoPro");
                ds.ForceNoSteamworks = GUILayout.Toggle(ds.ForceNoSteamworks, " ForceNoSteamworks");
                ds.EmulateMobile = GUILayout.Toggle(ds.EmulateMobile, " EmulateMobile（模拟手机端）");
                ds.RunningOnSteamDeck = GUILayout.Toggle(ds.RunningOnSteamDeck, " RunningOnSteamDeck");
                ds.DebugAmbience = GUILayout.Toggle(ds.DebugAmbience, " DebugAmbience");
                ds.PaigeStays = GUILayout.Toggle(ds.PaigeStays, " PaigeStays（剧情分支，含剧透）");
                ds.PauseOnFocusLost = GUILayout.Toggle(ds.PauseOnFocusLost, " PauseOnFocusLost（失焦暂停）");
            }
        }

        // ---------------- 旧版 · 高级（实验） ----------------
        private void DrawLegacyAdvanced()
        {
            GUILayout.Label("⚠ 实验区：以下涉及场景跳转/底层状态，可能需在特定界面使用，个别可能不稳。出问题重进关卡或重启游戏即可。", Lbl());

            GUILayout.Space(6);
            Section("音画校准（影响对点，谨慎）");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("读取当前")) LoadCalibration();
            if (GUILayout.Button("应用并保存"))
                Run("SetCalibration", () => Persistence.SetCalibrationValues(Cheats.calV, Cheats.calI, Cheats.calIP2, Cheats.calLat));
            GUILayout.EndHorizontal();
            if (_calLoaded)
            {
                CalRow("视觉偏移 v", ref Cheats.calV);
                CalRow("输入偏移 i", ref Cheats.calI);
                CalRow("输入偏移 i(P2)", ref Cheats.calIP2);
                CalRow("延迟 latency", ref Cheats.calLat);
            }
            else GUILayout.Label("先点「读取当前」载入数值。", Lbl());

            GUILayout.Space(8);
            Section("无限模式（举重小游戏的无尽档）");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"最佳轮数：{_bestRound}", GUILayout.Width(110));
            if (GUILayout.Button("-", GUILayout.Width(28))) _bestRound = Mathf.Max(0, _bestRound - 1);
            if (GUILayout.Button("+", GUILayout.Width(28))) _bestRound++;
            if (GUILayout.Button("读取")) Run("GetBestRound", () => _bestRound = Persistence.GetBestInfiniteRound());
            if (GUILayout.Button("写入")) Run("SetBestRound", () => Persistence.SetRhythmWeightlifterBestInfiniteRound(_bestRound));
            GUILayout.EndHorizontal();
            GUILayout.Label("无限模式本身在「举重节奏」打到底即可进入，非作弊；这里只改记录。", Lbl());

            GUILayout.Space(8);
            Section("展会 / 隐藏");
            try { RDC.booth = GUILayout.Toggle(RDC.booth, " 展会(Booth)模式（自助机模式；也会让 isDev 生效）"); } catch { }
            if (GUILayout.Button("狗狗模式（下次进 Les Mis 关生效）"))
                Run("DogMode", () => scnGame.loadDogMode = true);
            if (GUILayout.Button("跳到隐藏曲 Song of the Sea（建议在主菜单点）"))
                Run("SongOfTheSea", () => scnBase.GoToLevelWithEnum(Level.SongOfTheSea));
            GUILayout.Label("手动秘籍：主菜单 JJDF=隐藏曲；选关 ←→ 序列=狗狗模式。", Lbl());
        }

        // ---------------- 关卡直达 ----------------
        private void DrawLevels()
        {
            GUILayout.Label("点关卡名直接进入，绕过 hub 的剧情/NPC 限制。配合「普通」页的 Autoplay 即可录制任意关卡。", Lbl());
            GUILayout.BeginHorizontal();
            GUILayout.Label("筛选:", GUILayout.Width(40));
            _lvFilter = GUILayout.TextField(_lvFilter ?? "");
            if (GUILayout.Button("×", GUILayout.Width(26))) _lvFilter = "";
            GUILayout.EndHorizontal();
            GUILayout.Space(2);

            string f = (_lvFilter ?? "").ToLowerInvariant();
            int shown = 0;
            foreach (Level lv in Enum.GetValues(typeof(Level)))
            {
                if (lv == Level.None) continue;
                string name = lv.ToString();
                if (f.Length > 0 && name.ToLowerInvariant().IndexOf(f) < 0) continue;
                Level captured = lv;
                if (GUILayout.Button(name))
                    Run("GoTo " + name, () => scnBase.GoToLevelWithEnum(captured));
                shown++;
            }
            if (shown == 0) GUILayout.Label("（无匹配关卡）", Lbl());
        }

        private static void RevealAllStory()
        {
            Persistence.SetPlayedPassedLevelCutscene(true);
            Persistence.SetPlayedPostAct2Cutscene(true);
            Persistence.SetPlayedPostAct3Cutscene(true);
            Persistence.SetPlayedPostAct4Cutscene(true);
            Persistence.SetPlayedPreAct5Cutscene(true);
            Persistence.SetPlayedPreAct6Cutscene(true);
            Persistence.SetPlayedHaileyDuetIntroduction(true);
            Persistence.SetPlayedPreBitternessCutscene(true);
            Persistence.SetPlayedRooftopCutscene(true);
            Persistence.SetPlayedAct6Intro(true);
            Persistence.SetPaigeEnding(true);
            Persistence.SetIsGameDone(true);
            UnlockAllLevelsForce();
            try { Persistence.SaveAll(); } catch { }
        }

        private void CalRow(string label, ref float val)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {val:0.0000}", GUILayout.Width(150));
            val = GUILayout.HorizontalSlider(val, -0.3f, 0.3f);
            GUILayout.EndHorizontal();
        }

        private void LoadCalibration()
        {
            Run("GetCalibration", () =>
            {
                Persistence.GetCalibrationValues(out var v, out var i, out var i2, out var l);
                Cheats.calV = v; Cheats.calI = i; Cheats.calIP2 = i2; Cheats.calLat = l;
                _calLoaded = true;
            });
        }

        // Force-write rank -1 (= unlocked / NotPassed) for every level, bypassing the
        // IsBetterRank gate, then mark game done and flush to disk.
        private static void UnlockAllLevelsForce()
        {
            foreach (Level lv in Enum.GetValues(typeof(Level)))
            {
                if (lv == Level.None) continue;
                try { Persistence.SetLevelRank(lv, (Rank)(-1), true, true); } catch { }
            }
            try { Persistence.SetIsGameDone(true); } catch { }
            try { Persistence.SaveAll(); } catch { }
        }

        private static void SetAllLevelsToS()
        {
            SetAllLevelsTo(Rank.FromString("S"));
        }

        private static void SetAllLevelsToSPlus()
        {
            SetAllLevelsTo(Rank.FromString("S+"));
        }

        private static void SetAllLevelsTo(Rank rank)
        {
            foreach (Level lv in Enum.GetValues(typeof(Level)))
            {
                if (lv == Level.None) continue;
                try { Persistence.SetLevelRank(lv, rank, true, true); } catch { }
            }
            try { Persistence.SaveAll(); } catch { }
        }

        private static void UnlockAllAchievements()
        {
            foreach (Achievement a in Enum.GetValues(typeof(Achievement)))
            { try { Persistence.UnlockAchievement(a, storeStats: true); } catch { } }
        }

        // ---------------- helpers ----------------
        private static void Run(string what, Action act)
        {
            try { act(); Log.Info("Trainer action OK: " + what); }
            catch (Exception e) { Log.Error("Trainer action FAILED (" + what + "): " + e); }
        }

        private static void Indent(Action body)
        {
            GUILayout.BeginHorizontal(); GUILayout.Space(18);
            GUILayout.BeginVertical(); body(); GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private static void Section(string title) => GUILayout.Label("── " + title + " ──", Hdr());

        private static GUIStyle _hdr, _lbl, _warn;
        private static GUIStyle Hdr()
        {
            if (_hdr == null) _hdr = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            return _hdr;
        }
        private static GUIStyle Lbl()
        {
            if (_lbl == null) _lbl = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 };
            return _lbl;
        }
        private static GUIStyle Warn()
        {
            if (_warn == null)
            {
                _warn = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = true };
                _warn.normal.textColor = new Color(1f, 0.45f, 0.35f);
            }
            return _warn;
        }
    }
}
