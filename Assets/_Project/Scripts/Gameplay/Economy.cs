using System;
using UnityEngine;

// Para ve ilerleme: kazilan obsidyen ve depoya atilan cevherle para kazanilir; Lav Adam'in dukkaninda
// aletler ve kaplar sirayla satin alinir (kasik -> spatula -> kazma, bardak -> surahi -> kova).
public class Economy : MonoBehaviour
{
    public static Economy I;

    public int money;
    public int toolTier;      // 0 kasik, 1 spatula, 2 kazma
    public int vesselTier;    // 0 bardak, 1 surahi, 2 kova

    public static readonly string[] ToolNames = { "Kasik", "Spatula", "Kazma" };
    public static readonly string[] VesselNames = { "Bardak", "Surahi", "Kova" };
    public static readonly string[] ToolInfo =
    {
        "Yavas ve kucuk: obsidyeni parca parca kazar",
        "Daha genis uc, daha hizli kirar",
        "Tam guc: genis alan, az vurusta kirar",
    };
    public static readonly string[] VesselInfo =
    {
        "Cok az su alir, tek cevher tasir",
        "Orta boy, 3 cevher tasir",
        "Genis kap: bol su, 5 cevher",
    };

    // Kademelere gore ozellikler
    public static readonly float[] ToolRadius = { 0.28f, 0.42f, 0.60f };
    public static readonly float[] ToolPower = { 0.12f, 0.22f, 0.34f };
    public static readonly int[] ToolBreakMoney = { 1, 2, 4 };
    public static readonly float[] ToolSwingSpeed = { 1.25f, 1.1f, 1f };         // vurus suresi carpani
    public static readonly float[] VesselCapacity = { 0.2f, 0.5f, 1f };            // kovaya gore su hacmi
    public static readonly int[] VesselOres = { 1, 3, 5 };

    // Fiyatlar: [kademe] (0 = baslangic, ucretsiz)
    public static readonly int[] ToolPrice = { 0, 60, 220 };
    public static readonly int[] VesselPrice = { 0, 90, 260 };
    public const int OreValue = 8;

    public event Action Changed;

    void Awake() { I = this; }

    public static int ToolTier => I != null ? I.toolTier : 2;
    public static int VesselTier => I != null ? I.vesselTier : 2;

    public void Earn(int amount)
    {
        if (amount <= 0) return;
        money += amount;
        Changed?.Invoke();
    }

    public bool CanBuyTool => toolTier < ToolNames.Length - 1 && money >= ToolPrice[toolTier + 1];
    public bool CanBuyVessel => vesselTier < VesselNames.Length - 1 && money >= VesselPrice[vesselTier + 1];

    public bool BuyTool()
    {
        if (!CanBuyTool) return false;
        money -= ToolPrice[toolTier + 1];
        toolTier++;
        Changed?.Invoke();
        return true;
    }

    public bool BuyVessel()
    {
        if (!CanBuyVessel) return false;
        money -= VesselPrice[vesselTier + 1];
        vesselTier++;
        Changed?.Invoke();
        return true;
    }
}
