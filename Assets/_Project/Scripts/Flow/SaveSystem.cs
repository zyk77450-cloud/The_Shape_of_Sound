using UnityEngine;

/// <summary>
/// 进度存档（PlayerPrefs）。
/// Stage：0=没玩过；1=到过场景2。
/// Instruments：道具位掩码，bit0~bit3 = 道具0~3 是否已拾取。
/// 通关不清档（结束菜单"放弃"保留存档），只有"新的旅程"会 ClearAll。
/// </summary>
public static class SaveSystem
{
    public static int Stage
    {
        get { return PlayerPrefs.GetInt("sos_stage", 0); }
        set { PlayerPrefs.SetInt("sos_stage", value); PlayerPrefs.Save(); }
    }

    public static int Instruments
    {
        get { return PlayerPrefs.GetInt("sos_instruments", 0); }
        set { PlayerPrefs.SetInt("sos_instruments", value); PlayerPrefs.Save(); }
    }

    public static bool HasInstrument(int id)
    {
        return (Instruments & (1 << id)) != 0;
    }

    public static void SetInstrument(int id, bool on)
    {
        int m = Instruments;
        if (on) m |= (1 << id); else m &= ~(1 << id);
        Instruments = m;
    }

    public static void ClearAll()
    {
        PlayerPrefs.DeleteKey("sos_stage");
        PlayerPrefs.DeleteKey("sos_instruments");
    }
}
