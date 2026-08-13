using UnityEngine;
using System.Collections.Generic;

public class PendantManager : MonoBehaviour
{
    public enum SparkColor { Blue, Red, Yellow, Gold }

    private const string BlueSparkKey   = "PendantManager.BlueSpark";
    private const string RedSparkKey    = "PendantManager.RedSpark";
    private const string YellowSparkKey = "PendantManager.YellowSpark";
    private const string GoldSparkKey   = "PendantManager.GoldSpark";

    private bool _blueSparkCollected;
    private bool _redSparkCollected;
    private bool _yellowSparkCollected;
    private bool _goldSparkCollected;

    // Loaded from disk, not just held in memory, so collected sparks survive a scene reload —
    // needed since Page 12's face filter now lives in its own scene (Ar2) rather than switching
    // camera facing in place, so the main scene (Ar1) gets fully torn down and recreated each time
    // a child goes to/from the face filter.
    private void Awake()
    {
        _blueSparkCollected   = PlayerPrefs.GetInt(BlueSparkKey, 0) == 1;
        _redSparkCollected    = PlayerPrefs.GetInt(RedSparkKey, 0) == 1;
        _yellowSparkCollected = PlayerPrefs.GetInt(YellowSparkKey, 0) == 1;
        _goldSparkCollected   = PlayerPrefs.GetInt(GoldSparkKey, 0) == 1;
    }

    // Generic entry point for page completion sequences that award a spark by color (e.g.
    // Page6CompletionSequence's post-overlay reward sequence). Dispatches to the same
    // Collect*Spark() methods below, so existing per-color callers keep working unchanged.
    public void AwardSpark(SparkColor color)
    {
        switch (color)
        {
            case SparkColor.Blue:   CollectBlueSpark();   break;
            case SparkColor.Red:    CollectRedSpark();    break;
            case SparkColor.Yellow: CollectYellowSpark(); break;
            case SparkColor.Gold:   CollectGoldSpark();   break;
        }
    }

    public void CollectBlueSpark()
    {
        _blueSparkCollected = true;
        PlayerPrefs.SetInt(BlueSparkKey, 1);
        PlayerPrefs.Save();
        Debug.Log("[PendantManager] Blue Spark collected.");
    }

    public void CollectRedSpark()
    {
        _redSparkCollected = true;
        PlayerPrefs.SetInt(RedSparkKey, 1);
        PlayerPrefs.Save();
        Debug.Log("[PendantManager] Red Spark collected.");
    }

    public void CollectYellowSpark()
    {
        _yellowSparkCollected = true;
        PlayerPrefs.SetInt(YellowSparkKey, 1);
        PlayerPrefs.Save();
        Debug.Log("[PendantManager] Yellow Spark collected.");
    }

    public void CollectGoldSpark()
    {
        _goldSparkCollected = true;
        PlayerPrefs.SetInt(GoldSparkKey, 1);
        PlayerPrefs.Save();
        Debug.Log("[PendantManager] Gold Spark collected.");
    }

    public List<string> GetCollectedSparks()
    {
        List<string> collectedSparks = new List<string>();
        if (_blueSparkCollected) collectedSparks.Add("Blue");
        if (_redSparkCollected) collectedSparks.Add("Red");
        if (_yellowSparkCollected) collectedSparks.Add("Yellow");
        if (_goldSparkCollected) collectedSparks.Add("Gold");
        return collectedSparks;
    }
}
