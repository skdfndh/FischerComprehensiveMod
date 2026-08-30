using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using Framework;
using HarmonyLib;
using ProjectCode;
using UnityEngine;

namespace FischerTimeFlow;

[BepInPlugin("local.codex.fischer-time-flow", "Fischer 时间流速", "1.0.15")]
public sealed class FischerTimeFlowPlugin : BaseUnityPlugin
{
    private static readonly float[] Multipliers = { 1f, 2f, 4f, 8f };
    private static readonly FieldInfo? SpotAnimatorField = typeof(Game).GetField("spotAnim", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? MiniGameFishListField = typeof(Game).GetField("fishList", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? StartSprinkMethod = typeof(Spot).GetMethod("StartSprink", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? DialogBoxNpcInfoField = typeof(DialogBox).GetField("npcInfo", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? NextDialogMethod = typeof(DialogBox).GetMethod("OnClickNextDialog", BindingFlags.Instance | BindingFlags.NonPublic);
    private static FischerTimeFlowPlugin? instance;

    private ConfigEntry<int> multiplierIndex = null!;
    private ConfigEntry<KeyboardShortcut> cycleHotkey = null!;
    private ConfigEntry<bool> autoWakeCat = null!;
    private ConfigEntry<bool> autoSprinkleBait = null!;
    private ConfigEntry<bool> autoCompleteNpcTasks = null!;
    private readonly HashSet<FishInfo> knownBasketFish = new HashSet<FishInfo>();
    private readonly HashSet<NpcInfo> automaticDialogNpcs = new HashSet<NpcInfo>();
    private long observedShopRefreshTime = long.MinValue;
    private float shopRefreshRemainingSeconds = -1f;
    private bool basketBaselineReady;
    private Harmony harmony = null!;

    private void Awake()
    {
        instance = this;
        multiplierIndex = Config.Bind("设置", "倍率序号", 0,
            "0=1倍，1=2倍，2=4倍，3=8倍。");
        cycleHotkey = Config.Bind("设置", "切换热键", new KeyboardShortcut(KeyCode.F6),
            "按下后依次切换 1倍、2倍、4倍、8倍。游戏暂停时不会解除暂停。");
        autoWakeCat = Config.Bind("设置", "自动唤醒小猫", false,
            "开启后，小猫开始偷懒时立即自动唤醒。可在左上角面板中切换。");
        autoSprinkleBait = Config.Bind("设置", "自动投放窝料", false,
            "开启后，当前窝料效果结束时自动投放一层。优先消耗手动购买的窝料，再使用按原游戏规则恢复的免费窝料。");
        autoCompleteNpcTasks = Config.Bind("设置", "自动完成伙伴任务", false,
            "开启后，自动识别当前地图中出现的伙伴任务，并在材料齐全时自动提交。");
        multiplierIndex.Value = Mathf.Clamp(multiplierIndex.Value, 0, Multipliers.Length - 1);
        harmony = new Harmony("local.codex.fischer-time-flow");
        harmony.PatchAll(typeof(FischerTimeFlowPlugin).Assembly);
        Logger.LogInfo("Fischer 时间流速已加载。按 F6 切换 1倍、2倍、4倍、8倍。");
    }

    private void Update()
    {
        if (cycleHotkey.Value.IsDown())
        {
            CycleMultiplier();
        }

        if (Time.timeScale > 0f)
        {
            Time.timeScale = CurrentMultiplier;
        }
    }

    private void OnGUI()
    {
        bool canWakeCat = Game.curFishingState == FishingState.SlackingOff;
        float panelHeight = canWakeCat ? 270f : 240f;
        GUI.Box(new Rect(20f, 20f, 220f, panelHeight), "Time Flow");
        GUI.Label(new Rect(35f, 50f, 190f, 25f), "Current: " + CurrentMultiplier + "x");

        if (GUI.Button(new Rect(35f, 78f, 190f, 25f), "Switch (F6)"))
        {
            CycleMultiplier();
        }

        autoWakeCat.Value = GUI.Toggle(new Rect(35f, 108f, 190f, 25f), autoWakeCat.Value, "Auto wake cat");
        autoSprinkleBait.Value = GUI.Toggle(new Rect(35f, 138f, 190f, 25f), autoSprinkleBait.Value, "Auto sprinkle bait");
        if (GUI.Button(new Rect(35f, 168f, 190f, 25f), "Organize magic tank"))
        {
            OrganizeMagicTank();
        }

        if (GUI.Button(new Rect(35f, 198f, 190f, 25f), "Finish fish group"))
        {
            CompleteMiniGame();
        }

        autoCompleteNpcTasks.Value = GUI.Toggle(new Rect(35f, 228f, 190f, 25f), autoCompleteNpcTasks.Value, "Auto complete NPC tasks");

        if (canWakeCat && GUI.Button(new Rect(35f, 258f, 190f, 25f), "Wake cat"))
        {
            Game.startled = true;
        }
    }

    private void LateUpdate()
    {
        if (Time.timeScale <= 0f)
        {
            return;
        }

        SetCatAnimationSpeed();
        SetTimelineAnimationSpeed();
        UpdateAcceleratedShopRefresh();
        SyncAutoPurchase();
        WakeCatIfEnabled();
        SprinkleBaitIfEnabled();
        CompleteNpcTasksIfEnabled();
        ProcessNewBasketFish();
    }

    private void CycleMultiplier()
    {
        multiplierIndex.Value = (multiplierIndex.Value + 1) % Multipliers.Length;
        Logger.LogInfo("时间流速已切换为 " + CurrentMultiplier + "倍。");
    }

    private void SetUnscaledAnimatorSpeed(Animator animator)
    {
        if (animator != null && animator.updateMode == AnimatorUpdateMode.UnscaledTime)
        {
            animator.speed = CurrentMultiplier;
        }
    }

    private void SetCatAnimationSpeed()
    {
        if (CatAnimatorCtrl.Instance == null)
        {
            return;
        }

        SetUnscaledAnimatorSpeed(CatAnimatorCtrl.Instance.anim);
        SetUnscaledAnimatorSpeed(CatAnimatorCtrl.Instance.basket);
    }

    private void SetTimelineAnimationSpeed()
    {
        Game game = UnityEngine.Object.FindObjectOfType<Game>();
        if (game == null)
        {
            return;
        }

        float timelineSpeed = 24f / Consts.Instance().LENGTH_OF_THE_DAY / CurrentMultiplier;
        SetAnimatorSpeed(game.timeAnim, timelineSpeed);
        SetAnimatorSpeed(game.campsiteAnim, timelineSpeed);
        SetAnimatorSpeed(SpotAnimatorField?.GetValue(game) as Animator, timelineSpeed);

        HUD hud = UnityEngine.Object.FindObjectOfType<HUD>();
        if (hud != null && hud.clockAnim != null)
        {
            SetAnimatorSpeed(hud.clockAnim.GetComponentInChildren<Animator>(true), timelineSpeed);
        }
    }

    private void UpdateAcceleratedShopRefresh()
    {
        if (Main.model == null)
        {
            return;
        }

        if (shopRefreshRemainingSeconds < 0f)
        {
            DateTime utcNow = DateTime.UtcNow;
            shopRefreshRemainingSeconds = 3600f - utcNow.Minute * 60f - utcNow.Second;
        }

        shopRefreshRemainingSeconds -= Time.unscaledDeltaTime * CurrentMultiplier;
        if (shopRefreshRemainingSeconds <= 0f)
        {
            while (shopRefreshRemainingSeconds <= 0f)
            {
                shopRefreshRemainingSeconds += 3600f;
            }

            Main.model.backPackModel.propShopStock.Clear();
            Main.model.backPackModel.autoPurchasingSetting.CheckIfAutoPurchase();
            Main.evtMgr.Send(Framework.EventType.OnRefreshPageShop);
            Logger.LogInfo("商店已按当前倍率刷新，已执行自动采购。");
        }

        PageShop shop = UnityEngine.Object.FindObjectOfType<PageShop>();
        if (shop != null && shop.refreshCountDown != null)
        {
            int remainingSeconds = Mathf.CeilToInt(shopRefreshRemainingSeconds);
            int minutes = remainingSeconds / 60;
            int seconds = remainingSeconds % 60;
            shop.refreshCountDown.text = minutes.ToString("00") + ":" + seconds.ToString("00");
        }
    }

    private void SyncAutoPurchase()
    {
        if (Main.model == null)
        {
            return;
        }

        Framework.AutoPurchasingSetting setting = Main.model.backPackModel.autoPurchasingSetting;
        long currentRefreshTime = Main.model.backPackModel.lastShopRefreshTime;
        if (currentRefreshTime == 0L || currentRefreshTime == observedShopRefreshTime)
        {
            return;
        }

        observedShopRefreshTime = currentRefreshTime;
        if (setting.isOn)
        {
            setting.CheckIfAutoPurchase();
            Logger.LogInfo("检测到商店刷新，已执行自动采购。");
        }
    }

    private void WakeCatIfEnabled()
    {
        if (autoWakeCat.Value && Game.curFishingState == FishingState.SlackingOff)
        {
            Game.startled = true;
        }
    }

    private void SprinkleBaitIfEnabled()
    {
        if (!autoSprinkleBait.Value || Main.model == null || Main.model.mapModel.curMapInfo.sprinkleBaitRemainTime > 0f)
        {
            return;
        }

        int purchasedBait = Main.model.mapModel.curMapInfo.regionalSprinkleBait;
        int freeBait = Main.model.playerModel.curRemainSprinkleBateNum;
        if (purchasedBait <= 0 && freeBait <= 0)
        {
            return;
        }

        Spot spot = UnityEngine.Object.FindObjectOfType<Spot>();
        if (spot == null || StartSprinkMethod == null)
        {
            return;
        }

        try
        {
            StartSprinkMethod.Invoke(spot, null);
            Logger.LogInfo("已自动投放一层窝料。");
        }
        catch (TargetInvocationException exception)
        {
            Logger.LogWarning("自动投放窝料失败：" + exception.InnerException?.Message);
        }
    }

    private void CompleteMiniGame()
    {
        MiniGame miniGame = UnityEngine.Object.FindObjectOfType<MiniGame>();
        Game game = UnityEngine.Object.FindObjectOfType<Game>();
        if (miniGame == null || game == null)
        {
            Logger.LogInfo("鱼群聚集小游戏尚未打开。");
            return;
        }

        List<Fg_fish>? fishList = MiniGameFishListField?.GetValue(game) as List<Fg_fish>;
        if (fishList == null || fishList.Count == 0)
        {
            return;
        }

        Main.evtMgr.Send(Framework.EventType.OnAidFinish, new object[1] { new List<Fg_fish>(fishList) });
        miniGame.OnClickClose();
        Logger.LogInfo("已自动完成鱼群聚集小游戏。");
    }

    private void CompleteNpcTasksIfEnabled()
    {
        if (!autoCompleteNpcTasks.Value)
        {
            return;
        }

        CompleteNpcDialogs();
        CompleteNpcTasks();
    }

    private void CompleteNpcDialogs()
    {
        if (Main.model == null || Main.model.mapModel == null)
        {
            return;
        }

        foreach (NpcInfo npcInfo in Main.model.mapModel.curMapInfo.unlockNpc.Values)
        {
            if (!npcInfo.canDialog || npcInfo.isDialoging)
            {
                continue;
            }

            npcInfo.canDialog = false;
            automaticDialogNpcs.Add(npcInfo);
            Main.popMgr.OpenTip(Main.prefabPaths.DialogBox, new object[2] { npcInfo, Vector2.zero });
            Logger.LogInfo("已打开伙伴普通对话。");
            break;
        }

        DialogBox dialogBox = UnityEngine.Object.FindObjectOfType<DialogBox>();
        NpcInfo? dialogNpc = dialogBox == null ? null : DialogBoxNpcInfoField?.GetValue(dialogBox) as NpcInfo;
        if (dialogBox == null || dialogNpc == null || !automaticDialogNpcs.Contains(dialogNpc) || NextDialogMethod == null)
        {
            return;
        }

        try
        {
            NextDialogMethod.Invoke(dialogBox, null);
            if (!dialogNpc.isDialoging)
            {
                automaticDialogNpcs.Remove(dialogNpc);
                Logger.LogInfo("伙伴普通对话已完成。");
            }
        }
        catch (TargetInvocationException exception)
        {
            automaticDialogNpcs.Remove(dialogNpc);
            Logger.LogWarning("自动完成伙伴对话失败：" + exception.InnerException?.Message);
        }
    }

    private void CompleteNpcTasks()
    {
        if (Main.model == null || Main.model.mapModel == null)
        {
            return;
        }

        int acceptedCount = 0;
        int completedCount = 0;
        foreach (NpcInfo npcInfo in Main.model.mapModel.curMapInfo.unlockNpc.Values)
        {
            Fg_task? taskCfg = GetCurrentNpcTask(npcInfo, out bool acceptedTask);
            if (acceptedTask)
            {
                acceptedCount++;
            }

            if (taskCfg == null)
            {
                continue;
            }

            List<FishInfo> requiredFish = GetRequiredFish(taskCfg);
            if (requiredFish.Count < taskCfg.require_number)
            {
                continue;
            }

            Main.evtMgr.Send(Framework.EventType.OnSubmitTask, new object[2] { taskCfg, true });
            npcInfo.SubmitTask(taskCfg, requiredFish, satisfied: true);
            npcInfo.canShowTask = false;
            npcInfo.isDialoging = false;
            completedCount++;
        }

        if (completedCount > 0)
        {
            Main.evtMgr.Send(Framework.EventType.OnFishCountChange);
            DialogBoxChoice dialogBox = UnityEngine.Object.FindObjectOfType<DialogBoxChoice>();
            if (dialogBox != null)
            {
                dialogBox.OnClickClose();
            }
        }

        if (acceptedCount > 0 || completedCount > 0)
        {
            Logger.LogInfo("伙伴任务处理完成：接受 " + acceptedCount + " 个，完成 " + completedCount + " 个。");
        }
    }

    private static Fg_task? GetCurrentNpcTask(NpcInfo npcInfo, out bool acceptedTask)
    {
        acceptedTask = false;
        if (npcInfo.specialTask != 0 && npcInfo.specialDialog == 0)
        {
            return Main.config.Fg_task.GetValue(npcInfo.specialTask);
        }

        if (npcInfo.curDailyTask != 0)
        {
            return Main.config.Fg_task.GetValue(npcInfo.curDailyTask);
        }

        if (!npcInfo.canShowTask)
        {
            return null;
        }

        int taskId = npcInfo.RandomDailyTask();
        Fg_task taskCfg = Main.config.Fg_task.GetValue(taskId);
        if (taskCfg == null)
        {
            return null;
        }

        npcInfo.ReceivingDailyTask(taskId);
        npcInfo.canShowTask = false;
        acceptedTask = true;
        return taskCfg;
    }

    private static List<FishInfo> GetRequiredFish(Fg_task taskCfg)
    {
        List<FishInfo> matchingFish = new List<FishInfo>();
        List<FishInfo> fishBasket = Main.model.backPackModel.fishBasket;
        for (int i = 0; i < fishBasket.Count; i++)
        {
            FishInfo fishInfo = fishBasket[i];
            if (IsFishRequiredByTask(fishInfo, taskCfg))
            {
                matchingFish.Add(fishInfo);
            }
        }

        matchingFish.Sort((left, right) => left.price.CompareTo(right.price));
        if (matchingFish.Count > taskCfg.require_number)
        {
            matchingFish.RemoveRange(taskCfg.require_number, matchingFish.Count - taskCfg.require_number);
        }

        return matchingFish;
    }

    private static bool IsFishRequiredByTask(FishInfo fishInfo, Fg_task taskCfg)
    {
        if ((int)Common.Instance().GetFishScore(fishInfo) > taskCfg.require_quality || (taskCfg.require_glitter == 1 && fishInfo.fishVersion != FishVersion.Glitter))
        {
            return false;
        }

        for (int i = 0; i < taskCfg.requirements.Length; i++)
        {
            if (fishInfo.fishCfg.id == taskCfg.requirements[i])
            {
                return true;
            }
        }

        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Game), "PutFishIntoBasket", new Type[] { typeof(FishInfo) })]
    private static void OnFishCaught(FishInfo fishInfo)
    {
        instance?.HandleFishCaught(fishInfo);
    }

    private void OrganizeMagicTank()
    {
        if (Main.model == null)
        {
            return;
        }

        List<FishInfo> candidates = new List<FishInfo>();
        foreach (FishInfo fishInfo in Main.model.backPackModel.fishBasket)
        {
            if (!IsFishReservedForNpcTask(fishInfo))
            {
                candidates.Add(fishInfo);
            }
        }

        List<FishTankInfo> normalTanks = Main.model.backPackModel.appreciateTanks;
        for (int i = 0; i < normalTanks.Count; i++)
        {
            for (int j = 0; j < normalTanks[i].fishInfos.Count; j++)
            {
                FishInfo fishInfo = normalTanks[i].fishInfos[j];
                if (!IsFishReservedForNpcTask(fishInfo))
                {
                    candidates.Add(fishInfo);
                }
            }
        }

        candidates.Sort((left, right) => right.tankIncome.CompareTo(left.tankIncome));
        int movedCount = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            List<FishInfo>? source = FindFishSource(candidates[i], out int sourceTankIndex);
            if (source != null && TryMoveFishToMagicTank(candidates[i], source, sourceTankIndex))
            {
                movedCount++;
            }
        }

        Logger.LogInfo("神奇鱼缸整理完成，检查了 " + candidates.Count + " 条鱼，移动或替换了 " + movedCount + " 条鱼。");
    }

    private static List<FishInfo>? FindFishSource(FishInfo fishInfo, out int tankIndex)
    {
        List<FishInfo> fishBasket = Main.model.backPackModel.fishBasket;
        if (fishBasket.Contains(fishInfo))
        {
            tankIndex = -1;
            return fishBasket;
        }

        List<FishTankInfo> normalTanks = Main.model.backPackModel.appreciateTanks;
        for (int i = 0; i < normalTanks.Count; i++)
        {
            if (normalTanks[i].fishInfos.Contains(fishInfo))
            {
                tankIndex = i + 1;
                return normalTanks[i].fishInfos;
            }
        }

        tankIndex = -1;
        return null;
    }

    private void ProcessNewBasketFish()
    {
        if (Main.model == null)
        {
            return;
        }

        List<FishInfo> fishBasket = Main.model.backPackModel.fishBasket;
        if (!basketBaselineReady)
        {
            for (int i = 0; i < fishBasket.Count; i++)
            {
                knownBasketFish.Add(fishBasket[i]);
            }

            basketBaselineReady = true;
            return;
        }

        for (int i = 0; i < fishBasket.Count; i++)
        {
            FishInfo fishInfo = fishBasket[i];
            if (knownBasketFish.Add(fishInfo))
            {
                HandleFishCaught(fishInfo);
                break;
            }
        }
    }

    private void HandleFishCaught(FishInfo fishInfo)
    {
        knownBasketFish.Add(fishInfo);
        if (IsFishReservedForNpcTask(fishInfo))
        {
            Main.model.mapModel.curMapInfo.CheckNpcTask();
            Logger.LogInfo("新钓鱼符合伙伴任务，已保留在鱼篓并刷新提交提示。");
            return;
        }

        OrganizeMagicTank();
    }

    private static bool IsFishReservedForNpcTask(FishInfo fishInfo)
    {
        if (Main.model == null || Main.model.mapModel == null)
        {
            return false;
        }

        foreach (NpcInfo npcInfo in Main.model.mapModel.curMapInfo.unlockNpc.Values)
        {
            Fg_task? taskCfg = null;
            if (npcInfo.specialTask != 0 && npcInfo.specialDialog == 0)
            {
                taskCfg = Main.config.Fg_task.GetValue(npcInfo.specialTask);
            }
            else if (npcInfo.curDailyTask != 0)
            {
                taskCfg = Main.config.Fg_task.GetValue(npcInfo.curDailyTask);
            }

            if (taskCfg != null && IsFishRequiredByTask(fishInfo, taskCfg))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryMoveFishToMagicTank(FishInfo caughtFish, List<FishInfo> source, int sourceTankIndex)
    {
        if (caughtFish.isTrash || caughtFish.isTaskItem || caughtFish.isDecorItem || Main.model == null)
        {
            return false;
        }

        List<FishInfo> magicTank = Main.model.backPackModel.incomeTank.fishInfos;
        if (!source.Contains(caughtFish))
        {
            return false;
        }

        int tankCapacity = Main.model.playerModel.tankCfg.value;
        if (magicTank.Count < tankCapacity)
        {
            source.Remove(caughtFish);
            Main.evtMgr.Send(Framework.EventType.OnRemoveFishInTank, new object[2] { caughtFish, sourceTankIndex });
            magicTank.Add(caughtFish);
            NotifyFishTankChanged(caughtFish, 0);
            Logger.LogInfo("已将新钓鱼放入神奇鱼缸。");
            return true;
        }

        FishInfo lowestIncomeFish = magicTank[0];
        for (int i = 1; i < magicTank.Count; i++)
        {
            if (magicTank[i].tankIncome < lowestIncomeFish.tankIncome)
            {
                lowestIncomeFish = magicTank[i];
            }
        }

        if (caughtFish.tankIncome <= lowestIncomeFish.tankIncome)
        {
            return false;
        }

        source.Remove(caughtFish);
        Main.evtMgr.Send(Framework.EventType.OnRemoveFishInTank, new object[2] { caughtFish, sourceTankIndex });
        magicTank.Remove(lowestIncomeFish);
        magicTank.Add(caughtFish);
        Main.evtMgr.Send(Framework.EventType.OnRemoveFishInTank, new object[2] { lowestIncomeFish, 0 });
        MoveDisplacedFish(lowestIncomeFish, Main.model.backPackModel.fishBasket, tankCapacity);
        NotifyFishTankChanged(caughtFish, 0);
        Logger.LogInfo("已用收益更高的新鱼替换神奇鱼缸中的最低收益鱼。");
        return true;
    }

    private static void MoveDisplacedFish(FishInfo displacedFish, List<FishInfo> fishBasket, int tankCapacity)
    {
        if (displacedFish.fishCfg.rare >= 5)
        {
            List<FishTankInfo> normalTanks = Main.model.backPackModel.appreciateTanks;
            FishTankInfo? targetTank = null;
            int targetIndex = -1;
            for (int i = 0; i < normalTanks.Count; i++)
            {
                if (i == 1 || normalTanks[i].fishInfos.Count >= tankCapacity)
                {
                    continue;
                }

                if (targetTank == null || normalTanks[i].fishInfos.Count < targetTank.fishInfos.Count)
                {
                    targetTank = normalTanks[i];
                    targetIndex = i;
                }
            }

            if (targetTank != null)
            {
                targetTank.fishInfos.Add(displacedFish);
                Main.evtMgr.Send(Framework.EventType.OnPutFishIntoTank, new object[2] { displacedFish, targetIndex + 1 });
                return;
            }
        }

        fishBasket.Add(displacedFish);
    }

    private static void NotifyFishTankChanged(FishInfo fishInfo, int tankIndex)
    {
        Main.evtMgr.Send(Framework.EventType.OnFishCountChange);
        Main.evtMgr.Send(Framework.EventType.OnPutFishIntoTank, new object[2] { fishInfo, tankIndex });
        Main.evtMgr.Send(Framework.EventType.OnHourlyEarningsChange);
    }

    private static void SetAnimatorSpeed(Animator? animator, float speed)
    {
        if (animator != null)
        {
            animator.speed = speed;
        }
    }

    private float CurrentMultiplier => Multipliers[Mathf.Clamp(multiplierIndex.Value, 0, Multipliers.Length - 1)];

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
        instance = null;
        Time.timeScale = 1f;
    }
}
