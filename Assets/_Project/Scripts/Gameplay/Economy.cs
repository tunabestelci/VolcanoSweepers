using System;
using UnityEngine;

// Para ve ilerleme: kazilan obsidyen ve depoya atilan cevherle para kazanilir; Lav Adam'in dukkaninda
// kaziyicilar (kasik -> spatula -> ...), kiricilar (20 kademe: cekic, balyoz, matkap, kirici) ve kaplar
// (bardak -> surahi -> kova) sirayla satin alinir.
public class Economy : MonoBehaviour
{
    public static Economy I;

    public int money;
    public int toolTier;      // kaziyici: 0 kasik, 1 kepce, 2 spatula, 3 kucuk kurek, 4 orta kurek, 5 buyuk kurek
    public int breakerTier = 1;   // kirici: 1..20 sahip olunan en iyi kirici (oyun basi 1: marangoz cekici)
    public int vesselTier;    // 0 bardak, 1 surahi, 2 kova

    // ---------- Kaziyicilar ----------
    public static readonly string[] ToolNames = { "Kasik", "Kepce", "Spatula", "Kucuk Kurek", "Orta Kurek", "Buyuk Kurek" };
    public static readonly string[] ToolInfo =
    {
        "Kucuk ve hassas: ust tabakayi ince ince soyar",
        "Derin kase: daha cok kirinti tasir",
        "Duz genis bicak: temiz, genis serit",
        "Sivri uclu bahce kuregi: hizli ve keskin",
        "Yuvarlak burunlu kurek: genis iz, bol yuk",
        "Kar kuregi: devasa bicak, tek seferde yigin",
    };
    public static readonly float[] ToolRadius = { 0.28f, 0.32f, 0.42f, 0.48f, 0.56f, 0.72f };   // (eski) etki alani
    public static readonly float[] ToolPower = { 0.12f, 0.15f, 0.22f, 0.28f, 0.34f, 0.42f };
    public static readonly int[] ToolBreakMoney = { 1, 1, 2, 3, 4, 5 };
    public static readonly int[] ToolPrice = { 0, 40, 110, 280, 650, 1400 };
    // kepceye bir seferde sigan kirinti (gram) - market kartinda da gosterilir
    public static readonly float[] ToolScoopGrams = { 100f, 160f, 260f, 400f, 600f, 950f };
    // bir surtmede alinan kalinlik (kasik katmani; suyla olusan tam obsidyen = 9 kasik katmani)
    public static readonly float[] ToolPassLayers = { 1f, 1.25f, 1.5f, 2f, 2.25f, 3f };

    // ---------- Kiricilar ----------
    public enum BreakerKind { Hammer, Drill, Jackhammer }

    public const int BreakerCount = 20;
    // index 0 = kirici yok
    public static readonly string[] BreakerNames =
    {
        "Kirici yok",
        "Marangoz Cekici", "Kurt Agizli Cekic", "Celik Cekic", "Kaya Kazmasi",
        "Eski Balyoz", "Demirci Balyozu", "Savas Tokmagi", "Tehlike Balyozu",
        "Pasli Matkap", "El Matkabi", "Eski El Burgusu", "Antika Matkap",
        "Elektrikli Matkap", "Agir Matkap", "Sarjli Matkap", "Kirici Delici",
        "Eski Kirici", "Turuncu Kirici", "Kirmizi Kirici", "Sanayi Kirici",
    };

    public static BreakerKind KindOf(int tier) => tier <= 8 ? BreakerKind.Hammer : (tier <= 16 ? BreakerKind.Drill : BreakerKind.Jackhammer);

    public static string BreakerInfo(int tier)
    {
        if (tier <= 0) return "";
        switch (KindOf(tier))
        {
            case BreakerKind.Hammer: return $"Cekic: vur ve kir. Alan {BreakerRadius(tier):F2} m, guc {BreakerPower(tier):F2}";
            case BreakerKind.Drill: return $"Matkap: sol tik basili tut, surekli deler. Alan {BreakerRadius(tier):F2} m";
            default: return $"Kirici: sol tik basili tut, her seyi parcalar. Alan {BreakerRadius(tier):F2} m";
        }
    }

    // Vurus basina alan (m) ve guc; matkap/kiricida "guc" tik basinadir
    public static float BreakerRadius(int t)
    {
        if (t <= 0) return 0.28f;
        if (t <= 8) return new[] { 0f, 0.50f, 0.52f, 0.55f, 0.62f, 0.78f, 0.82f, 0.88f, 0.92f }[t];
        if (t <= 16) return 0.58f + 0.035f * (t - 9);
        return 1.00f + 0.09f * (t - 17);
    }

    public static float BreakerPower(int t)
    {
        if (t <= 0) return 0.1f;
        if (t <= 8) return new[] { 0f, 0.30f, 0.34f, 0.38f, 0.46f, 0.60f, 0.66f, 0.72f, 0.80f }[t];
        if (t <= 16) return 0.085f + 0.018f * (t - 9);
        return 0.30f + 0.05f * (t - 17);
    }

    // Cekicte vurus suresi carpani (agir balyozlar yavas), matkap/kiricida tik araligi (sn)
    public static float BreakerTempo(int t)
    {
        if (t <= 8) return new[] { 1f, 0.95f, 0.9f, 0.9f, 0.85f, 1.15f, 1.1f, 1.15f, 1.05f }[t];
        if (t <= 16) return 0.075f;
        return 0.06f;
    }

    // Bir tam kirilista ufalanan derinlik (kasik katmani): cekic 3 (= kasikla 3 surtme), balyoz 4.5, matkap 6-7.5, kirici 9 (hepsi)
    public static float BreakLayers(int t)
    {
        if (t <= 4) return 3f;
        if (t <= 8) return 4.5f;
        if (t <= 12) return 6f;
        if (t <= 16) return 7.5f;
        return 9f;
    }

    public static int BreakerMoney(int t) => t <= 0 ? 1 : 2 + t;                        // kirilan obsidyen basina
    public static int BreakerCores(int t) => t <= 0 ? 1 : 1 + t / 8;                    // kirilan basina cevher (azami)

    public static int BreakerPrice(int t)
    {
        if (t <= 0) return 0;
        double p = 90.0 * Math.Pow(1.33, t - 1);
        return (int)(Math.Round(p / 5.0) * 5.0);
    }

    // ---------- Kaplar ----------
    public static readonly string[] VesselNames = { "Bardak", "Surahi", "Kova" };
    public static readonly string[] VesselInfo =
    {
        "Cok az su alir, tek cevher tasir",
        "Orta boy, 3 cevher tasir",
        "Genis kap: bol su, 5 cevher",
    };
    public static readonly float[] VesselCapacity = { 0.2f, 0.5f, 1f };            // kovaya gore su hacmi
    public static readonly int[] VesselOres = { 1, 3, 5 };
    public static readonly int[] VesselPrice = { 0, 90, 260 };
    public const int OreValue = 8;

    public event Action Changed;

    void Awake()
    {
        I = this;
        // oyun basi: su kabi (bardak), kirici (marangoz cekici) ve kaziyici (kasik) elde. Kirmadan kazinamaz.
        if (breakerTier < 1) breakerTier = 1;
    }

    public static int ToolTier => I != null ? I.toolTier : 1;
    public static int BreakerTierNow => I != null ? I.breakerTier : 0;
    public static int VesselTier => I != null ? I.vesselTier : 2;

    public void Earn(int amount)
    {
        if (amount <= 0) return;
        money += amount;
        Changed?.Invoke();
    }

    public bool CanBuyTool => toolTier < ToolNames.Length - 1 && money >= ToolPrice[toolTier + 1];
    public bool CanBuyBreaker => breakerTier < BreakerCount && money >= BreakerPrice(breakerTier + 1);
    public bool CanBuyVessel => vesselTier < VesselNames.Length - 1 && money >= VesselPrice[vesselTier + 1];

    public bool BuyTool()
    {
        if (!CanBuyTool) return false;
        money -= ToolPrice[toolTier + 1];
        toolTier++;
        Changed?.Invoke();
        return true;
    }

    public bool BuyBreaker()
    {
        if (!CanBuyBreaker) return false;
        money -= BreakerPrice(breakerTier + 1);
        breakerTier++;
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
