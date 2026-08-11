using UnityEngine;
using System.Collections.Generic;

public class PendantManager : MonoBehaviour
{
    public enum SparkColor { Blue, Red, Yellow, Gold }

    private bool _blueSparkCollected;
    private bool _redSparkCollected;
    private bool _yellowSparkCollected;
    private bool _goldSparkCollected;

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
        Debug.Log("[PendantManager] Blue Spark collected.");
    }

    public void CollectRedSpark()
    {
        _redSparkCollected = true;
        Debug.Log("[PendantManager] Red Spark collected.");
    }

    public void CollectYellowSpark()
    {
        _yellowSparkCollected = true;
        Debug.Log("[PendantManager] Yellow Spark collected.");
    }

    public void CollectGoldSpark()
    {
        _goldSparkCollected = true;
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
